using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RelaunchProcess.Diagnostics;

namespace RelaunchProcess.Update
{
    /// <summary>
    /// Релиз на GitHub, найденный при проверке обновлений.
    /// </summary>
    public sealed class ReleaseInfo
    {
        public string Tag;
        public Version Version;
        public string Notes;
        public string ZipName;
        public string ZipUrl;
        public string ShaUrl;
    }

    /// <summary>
    /// Обновление утилиты из GitHub Releases по кнопке.
    /// Запущенные exe и DLL нельзя перезаписать, но можно переименовать: текущие файлы
    /// переименовываются в *.old, новые копируются на их место, затем запускается новая версия.
    /// Новая версия подтверждает запуск именованным событием; без подтверждения файлы откатываются.
    /// </summary>
    public static class Updater
    {
        public const string AfterUpdateArgument = "--after-update";
        private const string LatestReleaseUrl = "https://api.github.com/repos/Dark-raven-lab/Relaunch-Process/releases/latest";
        private const string OldSuffix = ".old";
        private static readonly Regex zipNamePattern = new Regex(@"^Relaunch-Process-v[0-9.]+-x64\.zip$", RegexOptions.IgnoreCase);
        private static readonly TimeSpan handshakeTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan oldProcessExitTimeout = TimeSpan.FromSeconds(30);
        private static readonly HttpClient http = CreateHttpClient();

        public static string AppDirectory => FileLog.BaseDirectory;
        public static string UpdatesDirectory => Path.Combine(AppDirectory, "Updates");
        private static string ExePath => typeof(Updater).Assembly.Location;

        /// <summary>
        /// Версия утилиты из AssemblyInformationalVersion (вида 1.7.2).
        /// </summary>
        public static Version CurrentVersion
        {
            get
            {
                string text = typeof(Updater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                return Version.TryParse(text, out Version version) ? version : new Version(0, 0, 0);
            }
        }

        private static HttpClient CreateHttpClient()
        {
            // На .NET Framework 4.8 TLS 1.2 включён не на всех системах, без него GitHub не ответит
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Relaunch-Process/{CurrentVersion}");
            return client;
        }

        private static string EventName(int oldPid) => $"RelaunchProcess-Updated-{oldPid}";

        #region Проверка и скачивание

        /// <summary>
        /// Читает последний релиз с GitHub. Черновики и pre-release GitHub здесь не возвращает.
        /// </summary>
        public static async Task<ReleaseInfo> GetLatestReleaseAsync()
        {
            string json;
            using (var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl))
            {
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using (HttpResponseMessage response = await http.SendAsync(request).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException($"GitHub ответил {(int)response.StatusCode} {response.ReasonPhrase}");
                    }
                    json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }

            JObject release = JObject.Parse(json);
            string tag = (string)release["tag_name"] ?? throw new InvalidOperationException("в ответе GitHub нет tag_name");
            if (!Version.TryParse(tag.TrimStart('v'), out Version version))
            {
                throw new InvalidOperationException($"тег релиза {tag} не похож на версию");
            }

            var assets = (release["assets"] as JArray ?? new JArray())
                .Select(a => new { Name = (string)a["name"], Url = (string)a["browser_download_url"] })
                .Where(a => a.Name != null && a.Url != null)
                .ToList();
            // Отсутствие файлов здесь не ошибка: если версия не новее, скачивать нечего.
            // Проверяется в DownloadAsync.
            var zip = assets.FirstOrDefault(a => zipNamePattern.IsMatch(a.Name));
            var sha = zip == null ? null
                : assets.FirstOrDefault(a => a.Name.Equals(zip.Name + ".sha256", StringComparison.OrdinalIgnoreCase));

            return new ReleaseInfo
            {
                Tag = tag,
                Version = version,
                Notes = (string)release["body"] ?? "",
                ZipName = zip?.Name,
                ZipUrl = zip?.Url,
                ShaUrl = sha?.Url,
            };
        }

        /// <summary>
        /// Скачивает архив, сверяет SHA-256, распаковывает и проверяет новый exe.
        /// Возвращает папку с файлами новой версии.
        /// </summary>
        public static async Task<string> DownloadAsync(ReleaseInfo release)
        {
            if (release.ZipUrl == null)
            {
                throw new InvalidOperationException($"в релизе {release.Tag} нет архива Relaunch-Process-v*-x64.zip");
            }
            if (release.ShaUrl == null)
            {
                throw new InvalidOperationException($"в релизе {release.Tag} нет файла {release.ZipName}.sha256, проверить архив нельзя");
            }
            CleanupLeftovers();

            string directory = Path.Combine(UpdatesDirectory, release.Tag);
            string zipPath = Path.Combine(directory, release.ZipName);
            string filesDirectory = Path.Combine(directory, "files");
            Directory.CreateDirectory(filesDirectory);

            byte[] zipBytes = await http.GetByteArrayAsync(release.ZipUrl).ConfigureAwait(false);
            string shaText = await http.GetStringAsync(release.ShaUrl).ConfigureAwait(false);
            File.WriteAllBytes(zipPath, zipBytes);

            string expected = shaText.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            string actual;
            using (var sha256 = SHA256.Create())
            {
                actual = BitConverter.ToString(sha256.ComputeHash(zipBytes)).Replace("-", "");
            }
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"SHA-256 архива не совпадает: ожидался {expected}, получен {actual.ToLowerInvariant()}");
            }

            ExtractTopLevelFiles(zipPath, filesDirectory);
            ValidateNewExe(Path.Combine(filesDirectory, Path.GetFileName(ExePath)), release.Version);
            return filesDirectory;
        }

        /// <summary>
        /// Распаковывает только файлы верхнего уровня. Папки и пути с разделителями или ".." отклоняются,
        /// чтобы архив не мог записать файл за пределы папки утилиты.
        /// </summary>
        private static void ExtractTopLevelFiles(string zipPath, string destination)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string name = entry.FullName;
                    if (name.Length == 0 || name.IndexOfAny(new[] { '/', '\\' }) >= 0 || name.Contains("..")
                        || name.IndexOfAny(invalid) >= 0 || name.EndsWith(OldSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"в архиве недопустимая запись \"{name}\", ожидаются только файлы без папок");
                    }
                }
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    entry.ExtractToFile(Path.Combine(destination, entry.FullName), overwrite: true);
                }
            }
        }

        private static void ValidateNewExe(string path, Version expected)
        {
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"в архиве нет {Path.GetFileName(path)}");
            }
            string productVersion = FileVersionInfo.GetVersionInfo(path).ProductVersion;
            if (!Version.TryParse(productVersion, out Version version) || version != expected)
            {
                throw new InvalidOperationException($"версия exe в архиве {productVersion}, а релиз {expected}");
            }
            if (AssemblyName.GetAssemblyName(path).ProcessorArchitecture != ProcessorArchitecture.Amd64)
            {
                throw new InvalidOperationException("exe в архиве собран не под x64");
            }
        }

        #endregion

        #region Установка и откат

        private sealed class ReplacedFile
        {
            public string Target;
            public bool HadOriginal;
        }

        /// <summary>
        /// Заменяет файлы утилиты новыми, запускает новую версию и ждёт её подтверждения.
        /// При любой ошибке возвращает старые файлы и выбрасывает исключение; текущая версия продолжает работать.
        /// После успешного возврата вызывающий должен закрыть утилиту.
        /// </summary>
        public static async Task InstallAndRestartAsync(string filesDirectory)
        {
            var replaced = new List<ReplacedFile>();
            try
            {
                foreach (string source in Directory.GetFiles(filesDirectory))
                {
                    string target = Path.Combine(AppDirectory, Path.GetFileName(source));
                    var item = new ReplacedFile { Target = target, HadOriginal = File.Exists(target) };
                    if (item.HadOriginal)
                    {
                        File.Move(target, target + OldSuffix);
                    }
                    replaced.Add(item);
                    File.Copy(source, target);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"не удалось заменить файлы: {ex.Message}{Rollback(replaced)}", ex);
            }

            string failure;
            int oldPid = Process.GetCurrentProcess().Id;
            using (var confirmed = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(oldPid)))
            {
                Process newProcess;
                try
                {
                    newProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = ExePath,
                        Arguments = $"{AfterUpdateArgument} {oldPid}",
                        WorkingDirectory = AppDirectory,
                        UseShellExecute = false,
                    });
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"не удалось запустить новую версию: {ex.Message}{Rollback(replaced)}", ex);
                }

                using (newProcess)
                {
                    failure = await Task.Run(() => WaitForConfirmation(confirmed, newProcess)).ConfigureAwait(false);
                    if (failure == null)
                    {
                        return;
                    }
                    StopProcess(newProcess);
                }
            }
            throw new InvalidOperationException(failure + Rollback(replaced));
        }

        private static string WaitForConfirmation(EventWaitHandle confirmed, Process newProcess)
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < handshakeTimeout)
            {
                if (confirmed.WaitOne(250))
                {
                    return null;
                }
                if (newProcess.HasExited)
                {
                    return $"новая версия завершилась при запуске с кодом {newProcess.ExitCode}";
                }
            }
            return $"новая версия не подтвердила запуск за {handshakeTimeout.TotalSeconds:0} с";
        }

        private static void StopProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
                // Пока новый exe работает, он заблокирован и вернуть старый на его место нельзя
                process.WaitForExit(10000);
            }
            catch (Exception ex)
            {
                FileLog.Write($"Обновление: не удалось завершить новую версию: {ex.Message}");
            }
        }

        /// <summary>
        /// Возвращает старые файлы. Возвращает пустую строку или текст ошибок для сообщения.
        /// </summary>
        private static string Rollback(List<ReplacedFile> replaced)
        {
            var errors = new List<string>();
            for (int i = replaced.Count - 1; i >= 0; i--)
            {
                ReplacedFile item = replaced[i];
                try
                {
                    if (File.Exists(item.Target))
                    {
                        File.Delete(item.Target);
                    }
                    if (item.HadOriginal)
                    {
                        File.Move(item.Target + OldSuffix, item.Target);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(item.Target)}: {ex.Message}");
                }
            }
            if (errors.Count == 0)
            {
                return ". Старые файлы возвращены";
            }
            string text = ". Откат не удался для: " + string.Join("; ", errors) + ". Верните файлы *.old вручную";
            FileLog.Write("Обновление: " + text);
            return text;
        }

        /// <summary>
        /// Удаляет остатки прошлого обновления: *.old и папку Updates.
        /// Если не получается, обновление отменяется.
        /// </summary>
        private static void CleanupLeftovers()
        {
            try
            {
                foreach (string file in Directory.GetFiles(AppDirectory, "*" + OldSuffix))
                {
                    File.Delete(file);
                }
                if (Directory.Exists(UpdatesDirectory))
                {
                    Directory.Delete(UpdatesDirectory, recursive: true);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"не удалось удалить остатки прошлого обновления ({ex.Message}). Удалите вручную файлы *.old и папку Updates рядом с exe", ex);
            }
        }

        #endregion

        #region Запуск новой версии

        /// <summary>
        /// Вызывается новой версией при запуске с --after-update до создания окна:
        /// проверяет, что загружаются все DLL, подтверждает запуск старой версии и ждёт её выхода.
        /// Возвращает false, если новая версия неработоспособна и должна завершиться.
        /// Лог здесь не пишется: настройки (и имя процесса для папки лога) читаются только после выхода старой версии.
        /// </summary>
        public static bool ConfirmStart(int oldPid, out string note)
        {
            try
            {
                LoadLocalDependencies();
            }
            catch (Exception ex)
            {
                note = null;
                Debug.WriteLine($"Обновление: не загружаются зависимости: {ex}");
                return false;
            }

            if (EventWaitHandle.TryOpenExisting(EventName(oldPid), out EventWaitHandle confirmed))
            {
                using (confirmed)
                {
                    confirmed.Set();
                }
            }

            note = WaitForOldProcess(oldPid);
            return true;
        }

        /// <summary>
        /// Рекурсивно загружает сборки, на которые ссылается утилита и которые лежат в её папке.
        /// Так проверяется, что все DLL на месте и подходят к binding redirects из .exe.config.
        /// </summary>
        private static void LoadLocalDependencies()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<Assembly>();
            queue.Enqueue(typeof(Updater).Assembly);
            while (queue.Count > 0)
            {
                foreach (AssemblyName reference in queue.Dequeue().GetReferencedAssemblies())
                {
                    if (seen.Add(reference.Name) && File.Exists(Path.Combine(AppDirectory, reference.Name + ".dll")))
                    {
                        queue.Enqueue(Assembly.Load(reference));
                    }
                }
            }
        }

        private static string WaitForOldProcess(int oldPid)
        {
            try
            {
                using (Process old = Process.GetProcessById(oldPid))
                {
                    if (!old.WaitForExit((int)oldProcessExitTimeout.TotalMilliseconds))
                    {
                        return $"старая версия (PID {oldPid}) не завершилась за {oldProcessExitTimeout.TotalSeconds:0} с";
                    }
                }
            }
            catch (ArgumentException)
            {
                // Старая версия уже завершилась
            }
            return null;
        }

        /// <summary>
        /// Вызывается новой версией, когда окно уже показано: удаляет *.old и папку Updates.
        /// Ошибки только пишутся в лог.
        /// </summary>
        public static void CompleteUpdate()
        {
            try
            {
                CleanupLeftovers();
            }
            catch (Exception ex)
            {
                FileLog.Write("Обновление: " + ex.Message);
            }
        }

        #endregion
    }
}
