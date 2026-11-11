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
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            bool afterUpdate = false;
            string afterUpdateNote = null;
            if (args.Length == 2 && args[0] == Updater.AfterUpdateArgument && int.TryParse(args[1], out int oldPid))
            {
                // До создания окна: старая версия должна сохранить настройки и завершиться
                if (!Updater.ConfirmStart(oldPid, out afterUpdateNote))
                {
                    return 2;
                }
                afterUpdate = true;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWindow(afterUpdate, afterUpdateNote));
            return 0;
        }
    }
}
