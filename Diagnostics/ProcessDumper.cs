using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Win32.SafeHandles;

namespace RelaunchProcess.Diagnostics
{
    /// <summary>
    /// Результат снятия дампа. Пути заполнены только для успешно записанных файлов.
    /// </summary>
    public sealed class DumpResult
    {
        public string DumpPath;
        public string DumpError;
        public string StacksPath;
        public string StacksError;
    }

    /// <summary>
    /// Снимает облегчённый дамп и managed-стеки потоков перед завершением зависшего процесса.
    /// Файлы: &lt;папка exe&gt;\Dumps\&lt;имя процесса&gt;\&lt;имя&gt;_yyyy-MM-dd_HH-mm-ss_PID.dmp и .txt.
    /// </summary>
    public static class ProcessDumper
    {
        public const int KeepDumps = 5;
        private static readonly TimeSpan stacksTimeout = TimeSpan.FromSeconds(60);

        // Облегчённый дамп: стеки и контексты потоков, хендлы, выгруженные модули, без памяти кучи.
        private const uint MiniDumpWithHandleData = 0x00000004;
        private const uint MiniDumpWithUnloadedModules = 0x00000020;
        private const uint MiniDumpWithThreadInfo = 0x00001000;
        private const uint DumpType = MiniDumpWithHandleData | MiniDumpWithUnloadedModules | MiniDumpWithThreadInfo;

        [DllImport("dbghelp.dll", SetLastError = true)]
        private static extern bool MiniDumpWriteDump(IntPtr hProcess, uint processId, SafeFileHandle hFile, uint dumpType,
            IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);

        public static DumpResult Capture(Process process, string processName)
        {
            var result = new DumpResult();
            if (!Environment.Is64BitProcess)
            {
                result.DumpError = result.StacksError = "утилита собрана как x86, дамп 64-битного процесса снять нельзя. Нужна сборка x64.";
                return result;
            }

            string safeName = FileLog.SafeName(processName);
            string directory = Path.Combine(FileLog.BaseDirectory, "Dumps", safeName);
            int pid;
            string basePath;
            try
            {
                pid = process.Id;
                Directory.CreateDirectory(directory);
                basePath = Path.Combine(directory, $"{safeName}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{pid}");
            }
            catch (Exception ex)
            {
                result.DumpError = result.StacksError = ex.Message;
                return result;
            }

            WriteMiniDump(process, pid, basePath + ".dmp", result);
            WriteStacksWithTimeout(pid, basePath + ".txt", result);

            if (result.DumpPath != null || result.StacksPath != null)
            {
                CleanupOldDumps(directory, safeName);
            }
            return result;
        }

        private static void WriteMiniDump(Process process, int pid, string path, DumpResult result)
        {
            try
            {
                bool ok;
                int error;
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    ok = MiniDumpWriteDump(process.Handle, (uint)pid, stream.SafeFileHandle, DumpType, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    error = ok ? 0 : Marshal.GetLastWin32Error();
                }
                if (ok)
                {
                    result.DumpPath = path;
                    return;
                }
                TryDelete(path);
                // MiniDumpWriteDump возвращает HRESULT в GetLastError
                result.DumpError = $"MiniDumpWriteDump: 0x{error:X8} {new System.ComponentModel.Win32Exception(error).Message}";
            }
            catch (Exception ex)
            {
                TryDelete(path);
                result.DumpError = ex.Message;
            }
        }

        private static void WriteStacksWithTimeout(int pid, string path, DumpResult result)
        {
            // Обход стеков может зависнуть на повреждённом процессе: ждём ограниченное время,
            // после этого всё равно продолжаем, процесс будет завершён.
            Task task = Task.Run(() => WriteStacks(pid, path));
            try
            {
                if (!task.Wait(stacksTimeout))
                {
                    result.StacksError = $"не уложились в {stacksTimeout.TotalSeconds:0} с";
                    return;
                }
                result.StacksPath = path;
            }
            catch (AggregateException ex)
            {
                TryDelete(path);
                result.StacksError = ex.InnerException?.Message ?? ex.Message;
            }
        }

        private static void WriteStacks(int pid, string path)
        {
            var text = new StringBuilder();
            text.AppendLine($"Managed-стеки процесса PID {pid}, снято {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            using (DataTarget target = DataTarget.AttachToProcess(pid, suspend: true))
            {
                if (target.ClrVersions.Length == 0)
                {
                    throw new InvalidOperationException("в процессе не найден CLR");
                }
                foreach (ClrInfo clr in target.ClrVersions)
                {
                    using (ClrRuntime runtime = clr.CreateRuntime())
                    {
                        text.AppendLine($"CLR {clr.Version} ({clr.Flavor})");
                        foreach (ClrThread thread in runtime.Threads)
                        {
                            AppendThread(text, thread);
                        }
                    }
                }
            }
            File.WriteAllText(path, text.ToString()); // UTF-8 без BOM
        }

        private static void AppendThread(StringBuilder text, ClrThread thread)
        {
            if (!thread.IsAlive)
            {
                return;
            }
            text.AppendLine();
            text.Append($"Поток managed={thread.ManagedThreadId} OS=0x{thread.OSThreadId:X} GC={thread.GCMode} locks={thread.LockCount}");
            if (thread.IsFinalizer)
            {
                text.Append(" finalizer");
            }
            text.AppendLine();

            ClrException exception = thread.CurrentException;
            if (exception != null)
            {
                text.AppendLine($"  Исключение: {exception.Type?.Name}: {exception.Message}");
            }

            try
            {
                foreach (ClrStackFrame frame in thread.EnumerateStackTrace().Take(1000))
                {
                    string name = frame.Method != null
                        ? frame.Method.Signature ?? $"{frame.Method.Type?.Name}.{frame.Method.Name}"
                        : $"[{frame.FrameName ?? frame.Kind.ToString()}]";
                    text.AppendLine($"  {frame.StackPointer:X16} {frame.InstructionPointer:X16} {name}");
                }
            }
            catch (Exception ex)
            {
                text.AppendLine($"  (ошибка обхода стека: {ex.Message})");
            }
        }

        /// <summary>
        /// Оставляет KeepDumps самых новых комплектов (.dmp и .txt с одним именем), остальные удаляет.
        /// Трогает только файлы по шаблону имени утилиты.
        /// </summary>
        private static void CleanupOldDumps(string directory, string safeName)
        {
            try
            {
                var groups = new DirectoryInfo(directory)
                    .GetFiles(safeName + "_*")
                    .Where(f => f.Extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase)
                             || f.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(f => Path.GetFileNameWithoutExtension(f.Name), StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Max(f => f.LastWriteTimeUtc))
                    .Skip(KeepDumps);
                foreach (var group in groups)
                {
                    foreach (FileInfo file in group)
                    {
                        TryDelete(file.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                FileLog.Write($"Ошибка очистки старых дампов: {ex.Message}");
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProcessDumper: не удалось удалить {path}: {ex.Message}");
            }
        }
    }
}
