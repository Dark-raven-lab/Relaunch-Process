using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace RelaunchProcess.Diagnostics
{
    /// <summary>
    /// Файловый лог: &lt;папка exe&gt;\Logs\&lt;имя процесса&gt;\Relauncher-yyyy-MM-dd.log.
    /// Пишется из разных потоков (UI, цикл наблюдения, таймер зависания), поэтому под блокировкой.
    /// Ошибки записи не выбрасываются: лог не должен ронять наблюдение.
    /// </summary>
    public static class FileLog
    {
        private static readonly object writeLock = new object();
        private static volatile string processName;

        /// <summary>
        /// Имя наблюдаемого процесса, по нему выбирается папка лога.
        /// Задаётся из потока интерфейса, читается из любого потока.
        /// </summary>
        public static string ProcessName
        {
            get { return processName; }
            set { processName = value; }
        }

        public static string BaseDirectory => AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>
        /// Превращает имя процесса в безопасное имя папки или файла.
        /// </summary>
        public static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "_unknown";
            }
            var builder = new StringBuilder(name.Trim());
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                builder.Replace(c, '_');
            }
            return builder.ToString();
        }

        public static void Write(string text)
        {
            try
            {
                DateTime now = DateTime.Now;
                string directory = Path.Combine(BaseDirectory, "Logs", SafeName(processName));
                string path = Path.Combine(directory, $"Relauncher-{now:yyyy-MM-dd}.log");
                string line = $"{now:yyyy-MM-dd HH:mm:ss.fff} {text}{Environment.NewLine}";
                lock (writeLock)
                {
                    Directory.CreateDirectory(directory);
                    using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var writer = new StreamWriter(stream)) // UTF-8 без BOM
                    {
                        writer.Write(line);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FileLog: ошибка записи: {ex.Message}");
            }
        }
    }
}
