using Process_Auto_Relaunch;
using System;
using System.Windows.Forms;
using RelaunchProcess.Update;

namespace RelaunchProcess
{
    internal static class Program
    {
        /// <summary>
        /// Главная точка входа для приложения.
        /// Аргументы после обновления: --after-update &lt;PID старой версии&gt; [--watch]
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            bool afterUpdate = false;
            string afterUpdateNote = null;
            int index = Array.IndexOf(args, Updater.AfterUpdateArgument);
            if (index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out int oldPid))
            {
                // До создания окна: старая версия должна сохранить настройки и завершиться
                if (!Updater.ConfirmStart(oldPid, out afterUpdateNote))
                {
                    return 2;
                }
                afterUpdate = true;
            }
            bool resumeWatching = afterUpdate && Array.IndexOf(args, Updater.WatchArgument) >= 0;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWindow(afterUpdate, afterUpdateNote, resumeWatching));
            return 0;
        }
    }
}
