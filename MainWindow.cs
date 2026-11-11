using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using RelaunchProcess.Properties;
using Discord;
using Discord.Webhook;
using RelaunchProcess;
using RelaunchProcess.Diagnostics;
using RelaunchProcess.Update;
using System.Timers;
using System.Configuration;

namespace Process_Auto_Relaunch
{
    public partial class MainWindow : Form
    {
        [Flags]
        public enum NotifyLevel
        {
            logNone = 0,
            logAlways = 1,          // писать везде
            logUpdateStatus = 2,    // писать в строке состояния
            logHistory = 4,         // писать в окне истории перезапусков
            logDiscord = 8          // писать в Дискорд
        }
        private delegate void UpdateLogDelegate(string text, NotifyLevel level = NotifyLevel.logUpdateStatus);
        private readonly UpdateLogDelegate updateLogDelegate;
        public DiscordWebhook dwhHook;
        private DiscordMessage dwhMessage;
        private Process WatchedProcess;
        private double cpuLastTime = 0;
        private Stopwatch cpuMeasureTimer;
        private System.Timers.Timer waitResponceTimer;

        // Слежение за кодом выхода. TrackExit вызывается и из цикла наблюдения, и из таймера зависания, поэтому под блокировкой.
        private readonly object exitTrackLock = new object();
        private Process exitTrackedProcess;     // процесс с открытым хендлом: без него код выхода после завершения не прочитать
        private int exitTrackFailedPid;         // PID, хендл которого открыть не удалось: повторно не пытаемся

        private const int MaxHistoryLines = 100;   // строк в окне истории; файл лога не ограничен
        private int cpuLastPid;                    // PID, для которого накоплен cpuLastTime
        private string lastLoopError;              // последняя ошибка цикла наблюдения, чтобы не слать одно и то же каждую секунду
        private DateTime lastLoopErrorReported;    // когда она последний раз ушла в историю и Discord (UTC)
        private static readonly TimeSpan LoopErrorRepeatInterval = TimeSpan.FromMinutes(10);

        private readonly bool startedAfterUpdate;   // запущены новой версией после обновления по кнопке

        /// <summary>
        /// Процесс для наблюдения
        /// </summary>
        public string ProcessName { get { return textBoxProcessName.Text; } set { textBoxProcessName.Text = value; } }

        public MainWindow(bool afterUpdate = false, string afterUpdateNote = null)
        {
            InitializeComponent();
            startedAfterUpdate = afterUpdate;
            if (Settings.Default.upgradeSettings)
            {
                Debug.WriteLine("Обновление настроек.");
                try
                {
                    Settings.Default.Upgrade();
                }
                catch (ConfigurationErrorsException ex)
                {
                    Debug.WriteLine($"Ошибка обновления настроек: {ex.Message}");
                    Settings.Default.Reset();
                }
                finally
                {
                    Settings.Default.upgradeSettings = false;
                    Settings.Default.Save();
                }
            }
            FileLog.ProcessName = ProcessName;
            textBoxProcessName.TextChanged += (s, e) => FileLog.ProcessName = ProcessName;
            string buildDate = typeof(MainWindow).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "BuildDate")?.Value;
            FileLog.Write($"Утилита запущена. Версия {Updater.CurrentVersionText}, сборка от {buildDate}, x64: {Environment.Is64BitProcess}.");
            if (afterUpdate)
            {
                FileLog.Write("Запуск после обновления по кнопке." + (afterUpdateNote != null ? $" Внимание: {afterUpdateNote}." : ""));
            }
            this.updateLogDelegate = this.UpdateStatus;
            this.updateLogDelegate += this.SendDiscordMessage;
            this.updateLogDelegate += this.HistoryLog;
            myBackgroundWorker.WorkerSupportsCancellation = true;
            tipProgramStartPath.SetToolTip(this.labelProgramStartPath, Settings.Default.startProgramPath);
            dwhHook = new DiscordWebhook();
            cpuMeasureTimer = new Stopwatch();
        }

        /// <summary>
        /// Событие запуска формы
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Form1_Load(object sender, EventArgs e)
        {
            LoadOldState();
            CheckProgramState();
        }

        /// <summary>
        /// Восстановление настроек
        /// </summary>
        private void LoadOldState()
        {
            if (Settings.Default.saveOldState)
            {
                radioButtonEnableWathing.Checked = Settings.Default.enableWatching;
            }

        }

        /// <summary>
        /// Метод для события отключения
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void radioButtonDisableWathing_CheckedChanged(object sender, EventArgs e)
        {
            CheckProgramState();

            if (!radioButtonDisableWathing.Checked)
            {
                return;
            }

            if (myBackgroundWorker.WorkerSupportsCancellation && myBackgroundWorker.IsBusy)
            {
                myBackgroundWorker.CancelAsync();
                UpdateStatus("Отменяем...", NotifyLevel.logUpdateStatus);
            }
        }

        /// <summary>
        /// Метод для события включения
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void radioButtonEnableWathing_CheckedChanged(object sender, EventArgs e)
        {
            if (!radioButtonEnableWathing.Checked)
            {
                return;
            }
            bool error = false;

            if (String.IsNullOrEmpty(textBoxProcessName.Text))
            {
                error = true;
                MessageBox.Show("Имя процесса не может быть пустым!" +
                    "\nУкажите имя процесса", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            if (String.IsNullOrEmpty(Settings.Default.startProgramPath))
            {
                error = true;
                MessageBox.Show("Программа для запуска не указана." +
                    "\nУкажите программу для запуска", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            if (error)
            {
                radioButtonEnableWathing.Checked = false;
                radioButtonDisableWathing.Checked = true;
                return;
            }

            if (!myBackgroundWorker.IsBusy)
            {
                myBackgroundWorker.RunWorkerAsync();
                Status($"Запуск наблюдения за процессом {ProcessName}", NotifyLevel.logDiscord);
            }
        }

        /// <summary>
        /// Обновление статуса в программе
        /// </summary>
        /// <param name="text">Текст для отображения/отправки </param>
        /// <param name="level">Флаги для назначения отправки</param>
        public void UpdateStatus(string text, NotifyLevel level)
        {
            if (!level.HasFlag(NotifyLevel.logAlways) && !level.HasFlag(NotifyLevel.logUpdateStatus)) return;
            labelStatus.Text = text;
        }

        /// <summary>
        /// Добавление строки в Истории Запусков
        /// </summary>
        /// <param name="text">Текст для отображения/отправки </param>
        /// <param name="level">Флаги для назначения отправки</param>
        private void HistoryLog(string text, NotifyLevel level)
        {
            if (!level.HasFlag(NotifyLevel.logAlways) && !level.HasFlag(NotifyLevel.logHistory)) return;
            richTextBoxHistory.AppendText(DateTime.Now.ToString() + ": " + text + "\n");
            TrimHistory();
        }

        /// <summary>
        /// Оставляет в окне истории последние MaxHistoryLines строк (из файла лога ничего не удаляется)
        /// </summary>
        private void TrimHistory()
        {
            // Считаем логические строки по '\n': GetLineFromCharIndex при переносе слов считал бы экранные
            string text = richTextBoxHistory.Text;
            int lines = text.Count(c => c == '\n');
            if (lines > MaxHistoryLines)
            {
                int cut = 0;
                for (int removed = 0; removed < lines - MaxHistoryLines; removed++)
                {
                    cut = text.IndexOf('\n', cut) + 1;
                }
                // SelectedText на ReadOnly-поле может не сработать, поэтому снимаем защиту на время удаления
                bool readOnly = richTextBoxHistory.ReadOnly;
                richTextBoxHistory.ReadOnly = false;
                richTextBoxHistory.Select(0, cut);
                richTextBoxHistory.SelectedText = "";
                richTextBoxHistory.ReadOnly = readOnly;
            }
            richTextBoxHistory.SelectionStart = richTextBoxHistory.TextLength;
            richTextBoxHistory.ScrollToCaret();
        }

        /// <summary>
        /// Отправка статуса в Discord
        /// </summary>
        /// <param name="text">Текст для отображения/отправки </param>
        /// <param name="level">Флаги для назначения отправки</param>
        public void SendDiscordMessage(string text, NotifyLevel level)
        {
            if (!level.HasFlag(NotifyLevel.logAlways) && !level.HasFlag(NotifyLevel.logDiscord)) return;
            if (Settings.Default.dwhEnabled)
            {
                dwhHook.Url = Settings.Default.dwhURL;
                dwhMessage.Username = Settings.Default.dwhBotname;
                dwhMessage.AvatarUrl = Settings.Default.dwhAvatarURL;
                dwhMessage.Content = ":arrows_counterclockwise: " + text;
                try
                {
                    dwhHook.Send(dwhMessage);
                }
                catch (Exception ex)
                {
                    Status($"Ошибка отправки в дискорд.", NotifyLevel.logHistory);
                    Debug.WriteLine($"Discord messaging error: {ex.Message}");
                    //Settings.Default.dwhEnabled = false;
                    //Settings.Default.Save();
                }
            }
        }

        /// <summary>
        /// Обновление статуса в программе
        /// </summary>
        /// <param name="text">Текст для отображения/отправки </param>
        /// <param name="level">Флаги для назначения отправки</param>
        /// <param name="detail">Исключение, полный текст которого пишется только в файл лога</param>
        public void Status(string text, NotifyLevel level = NotifyLevel.logUpdateStatus, Exception detail = null)
        {
            // Файл лога пишем здесь, до Invoke и независимо от него: запись не потеряется,
            // даже если форма закрывается или упал один из обработчиков интерфейса
            if ((level & (NotifyLevel.logAlways | NotifyLevel.logHistory | NotifyLevel.logDiscord)) != 0)
            {
                FileLog.Write(detail == null ? text : text + Environment.NewLine + detail);
            }
            try
            {
                Invoke(updateLogDelegate, text, level);
            }
            catch (Exception ex)
            {
                FileLog.Write($"Не удалось показать сообщение: {ex.Message}");
            }
        }

        private void CheckProgramState()
        {
            bool watching = radioButtonEnableWathing.Checked;
            Debug.WriteLine($"Наблюдение: {watching}");

            groupBoxProcessName.Enabled = !watching;
            groupBoxProgramStart.Enabled = !watching;
            groupBoxActions.Enabled = !watching;
            // btnShowDiscordSettings.Enabled = !watching; //отключаем кнопку настроек дискорда
            // webhookDiscordToolStripMenuItem.Enabled = !watching;
            // отключаем меню настроек
            settingsToolStripMenuItem.Enabled = !watching;

            Settings.Default.enableWatching = watching;
            // Сохраняем сразу: если утилиту убьют, при следующем запуске восстановится актуальное состояние
            Settings.Default.Save();
        }

        /// <summary>
        /// Выбор файла для запуска
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonSetProgramStart_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFile = new OpenFileDialog();
            openFile.Filter = "Исполняемые файлы (*.exe)|*.exe";
            openFile.Title = "Укажите программу запуска";

            if (openFile.ShowDialog() == DialogResult.Cancel)
            {
                return;
            }

            int lastSlash = openFile.FileName.LastIndexOf("\\");
            textBoxProcessName.Text = openFile.FileName.Substring(lastSlash + 1);
            textBoxProcessName.Text = textBoxProcessName.Text.Remove(textBoxProcessName.Text.Length - 4);
            Settings.Default.startProgramPath = openFile.FileName;
            Settings.Default.Save();
            tipProgramStartPath.SetToolTip(this.labelProgramStartPath, Settings.Default.startProgramPath);
            openFile.Dispose();
        }

        /// <summary>
        /// Событие перед закрытием формы
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            dwhHook.CancelProcessing();
            Status("Наблюдение отменено - приложение закрыто.", NotifyLevel.logAlways);
            Settings.Default.Save();
        }

        private bool ProcessByNameIsRuning(string name)
        {
            var sessionid = Process.GetCurrentProcess().SessionId;
            var processes = Process.GetProcessesByName(name);
            Process found = null;
            foreach (var process in processes)
            {
                Debug.WriteLine($"Found proces: {process.ProcessName}. Session Id: {process.SessionId}. Current Session Id: {sessionid}");
                // Завершённый процесс остаётся в списке, пока кто-то держит его хендл (в т.ч. мы сами ради кода выхода).
                // У такого процесса нет потоков: считаем его не запущенным, иначе перезапуск не случится.
                if (found == null && process.SessionId == sessionid && process.Threads.Count > 0)
                {
                    found = process;
                }
                else
                {
                    // лишние объекты освобождаем сразу, не дожидаясь сборщика мусора
                    process.Dispose();
                }
            }
            if (found != null)
            {
                WatchedProcess = found;
                TrackExit(found);
                return true;
            }
            Debug.WriteLine($"Process {name} for current session id {sessionid} not found");
            return false;
        }

        /// <summary>
        /// Начинает следить за завершением процесса. Хендл открывается, пока процесс жив,
        /// иначе после выхода код завершения уже не прочитать.
        /// </summary>
        private void TrackExit(Process process)
        {
            string error;
            lock (exitTrackLock)
            {
                if (exitTrackedProcess != null || process.Id == exitTrackFailedPid)
                {
                    return;
                }
                try
                {
                    _ = process.Handle;
                    exitTrackedProcess = process;
                    return;
                }
                catch (Exception ex)
                {
                    exitTrackFailedPid = process.Id;
                    error = ex.Message;
                }
            }
            // Status ждёт поток интерфейса, поэтому вызываем его уже без блокировки
            Status($"Не удалось открыть процесс {ProcessName} (PID {process.Id}), код выхода не будет известен: {error}", NotifyLevel.logHistory | NotifyLevel.logDiscord);
        }

        /// <summary>
        /// Если отслеживаемый процесс завершился, сообщает его код выхода и прекращает слежение.
        /// -1 (0xFFFFFFFF) - процесс снят через Kill/TerminateProcess (так же завершается сам Torch при рестарте);
        /// 0xC0000xxx - аварийное завершение (код NTSTATUS); иное - ExitProcess/Environment.Exit с этим кодом.
        /// </summary>
        private void ReportTrackedExit()
        {
            Process process;
            lock (exitTrackLock)
            {
                process = exitTrackedProcess;
                if (process == null || !process.HasExited)
                {
                    return;
                }
                exitTrackedProcess = null;
            }

            // ExitTime ненадёжен (бывает нулевым), поэтому пишем время обнаружения: цикл проверяет раз в секунду.
            // Исключение здесь остановило бы весь цикл наблюдения, поэтому оно перехватывается.
            string codeText;
            try
            {
                int code = process.ExitCode;
                codeText = $"с кодом {code} (0x{code:X8})";
            }
            catch (Exception ex)
            {
                codeText = $"(код выхода прочитать не удалось: {ex.Message})";
            }
            Status($"Процесс {ProcessName} (PID {process.Id}) завершился {codeText}, обнаружено в {DateTime.Now:HH:mm:ss}.", NotifyLevel.logHistory | NotifyLevel.logDiscord);
            // Хендл закрываем сразу: пока он открыт, завершённый процесс виден в списке процессов
            Interlocked.CompareExchange(ref WatchedProcess, null, process);
            process.Dispose();
        }

        /// <summary>
        /// Обнаружение зависшего процесса по нагрузке
        /// </summary>
        /// <param name="cpuResponding"></param>
        /// <param name="cpuPrecent"></param>
        private void ProcessCheckResponding(bool cpuResponding, double cpuPrecent)
        {
            if (cpuResponding || cpuPrecent > 0.01)
            {
                // Процесс отвечает
                // Если таймер ожидания запущен - отключаем.
                if (TimerResponceRuning())
                {
                    StopTimerWaitingResponce();
                }
                return;
            }

            // Процесс не отвечает
            // Если таймер ожидания не запущен - запускаем
            if (!TimerResponceRuning())
            {
                StartTimerWaitingResponce(5000, ProcessNotResponding);
            }
        }

        /// <summary>
        /// Проверяет запущен ли таймер ожидания процесса
        /// </summary>
        /// <returns>True, если запущен</returns>
        private bool TimerResponceRuning()
        {
            return waitResponceTimer != null /*&& waitResponceTimer.Enabled*/;
        }

        /// <summary>
        /// Метод запуска таймера для ожидания процесса
        /// </summary>
        /// <param name="interval">Интервал таймера</param>
        /// <param name="timerElapsed">Метод, вызываемый после окончания таймера</param>
        private void StartTimerWaitingResponce(double interval, ElapsedEventHandler timerElapsed)
        {
            Debug.WriteLine($"Запуск таймера {interval} ms.");
            waitResponceTimer = new System.Timers.Timer(interval);
            waitResponceTimer.Elapsed += timerElapsed;
            waitResponceTimer.AutoReset = false;
            waitResponceTimer.Enabled = true;
        }

        /// <summary>
        /// Остановка таймера ожидания процесса
        /// </summary>
        private void StopTimerWaitingResponce()
        {
            Debug.WriteLine("Остановка таймера ожидания процесса.");
            waitResponceTimer.Dispose();
            waitResponceTimer = null;
        }

        /// <summary>
        /// Метод события для таймера если процесс не отвечает
        /// </summary>
        private void ProcessNotResponding(Object source, ElapsedEventArgs e)
        {
            // System.Timers.Timer молча глотает исключения из Elapsed, поэтому ловим и пишем сами
            try
            {
                HandleNotResponding();
            }
            catch (Exception ex)
            {
                Status($"Ошибка обработки зависшего процесса: {ex.Message}", NotifyLevel.logHistory | NotifyLevel.logDiscord, ex);
            }
        }

        private void HandleNotResponding()
        {
            Debug.WriteLine("Таймер ожидания ответа процесса вышел.");
            Status($"Процесс {ProcessName} не отвечает уже какое-то время, возможно завис!", NotifyLevel.logHistory | NotifyLevel.logDiscord);

            // Если процесс не завершаем, тогда только информируем
            if (!checkBoxCloseFreezeProcess.Checked)
            {
                Status("@everyone Завершение процесса отключено. Сделайте что-нибудь!", NotifyLevel.logHistory | NotifyLevel.logDiscord);
                return;
            }

            // Пытаемся самостоятельно завершить процесс
            // ! Обнаружение зависшего процесса может быть некорректным !
            if (ProcessByNameIsRuning(ProcessName))
            {
                // Локальная копия: цикл наблюдения переприсваивает WatchedProcess каждую секунду
                Process target = WatchedProcess;
                CaptureDump(target);
                ProcessKill(target);
            }
        }

        /// <summary>
        /// Снимает облегчённый дамп и managed-стеки перед завершением процесса.
        /// Неудача не мешает завершению: ошибка только записывается.
        /// </summary>
        private void CaptureDump(Process process)
        {
            if (process == null || process.HasExited)
            {
                return;
            }
            Status($"Снимаю дамп процесса {ProcessName} (PID {process.Id})...", NotifyLevel.logHistory);
            Stopwatch timer = Stopwatch.StartNew();
            DumpResult dump = ProcessDumper.Capture(process, ProcessName);
            string dumpText = dump.DumpPath ?? $"не снят ({dump.DumpError})";
            string stacksText = dump.StacksPath ?? $"не сняты ({dump.StacksError})";
            Status($"Дамп: {dumpText}. Стеки: {stacksText}. Заняло {timer.Elapsed.TotalSeconds:f1} с.", NotifyLevel.logHistory | NotifyLevel.logDiscord);
        }

        private void ProcessStart(string path, string args)
        {
            if (ProcessByNameIsRuning(ProcessName))
            {
                return;
            }

            // Процесс не запущен
            WatchedProcess = Process.Start(path, args);
            if (WatchedProcess != null)
            {
                // Хендл сразу: если процесс упадёт до следующей проверки, код выхода не потеряется
                TrackExit(WatchedProcess);
            }
            // cpuLastTime сбрасывается в WatchIteration по смене PID
            cpuMeasureTimer.Start();
            Status($"Процесс {ProcessName} запущен.", NotifyLevel.logAlways);
        }

        /// <summary>
        /// Метод принудительного завершения процесса
        /// </summary>
        /// <param name="process"></param>
        private void ProcessKill(Process process)
        {
            if (process == null || process.HasExited)
            {
                return;
            }

            try
            {
                Status($"Попытка завершения процесса {process.ProcessName}", NotifyLevel.logHistory | NotifyLevel.logDiscord);
                process.Kill();
            }
            catch (Exception ex)
            {
                Status(ex.Message, NotifyLevel.logHistory | NotifyLevel.logDiscord);
                return;
            }

            Status($"Процесс был успешно завершён.", NotifyLevel.logHistory | NotifyLevel.logDiscord);
        }

        private void BackgroundWorkerDoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            int i = (int)numericUpDown1.Value;

            while (!worker.CancellationPending)
            {
                // Ошибка одной итерации не должна останавливать наблюдение: пишем её и продолжаем
                try
                {
                    WatchIteration(ref i);
                }
                catch (Exception ex)
                {
                    ReportLoopError(ex);
                }

                Thread.Sleep(1000);
            }

            if (worker.CancellationPending)
            {
                e.Cancel = true;
                processInformationLabel.Text = "";
            }
        }

        /// <summary>
        /// Пишет ошибку цикла наблюдения: стек всегда в файл, в историю и Discord - новая ошибка
        /// или та же, но не чаще раза в LoopErrorRepeatInterval
        /// </summary>
        private void ReportLoopError(Exception ex)
        {
            string key = ex.GetType().FullName + ": " + ex.Message;
            DateTime now = DateTime.UtcNow;
            if (key == lastLoopError && now - lastLoopErrorReported < LoopErrorRepeatInterval)
            {
                FileLog.Write($"Ошибка в цикле наблюдения (повтор): {ex}");
                return;
            }
            lastLoopError = key;
            lastLoopErrorReported = now;
            Status($"Ошибка в цикле наблюдения: {ex.Message}. Наблюдение продолжается.", NotifyLevel.logHistory | NotifyLevel.logDiscord, ex);
        }

        /// <summary>
        /// Одна проверка процесса (раз в секунду)
        /// </summary>
        /// <param name="i">Обратный отсчёт до перезапуска, сек</param>
        private void WatchIteration(ref int i)
        {
            ReportTrackedExit();
            if (ProcessByNameIsRuning(ProcessName))
            {
                double cpuTotalTime, cpuPercent;
                string ProcessAnswer;

                // Сменился PID (перезапуск не нами): старое накопленное время ЦП к новому процессу не относится
                if (WatchedProcess.Id != cpuLastPid)
                {
                    cpuLastPid = WatchedProcess.Id;
                    cpuLastTime = 0;
                }

                cpuMeasureTimer.Stop();
                try
                {
                    cpuTotalTime = WatchedProcess.TotalProcessorTime.TotalMilliseconds - cpuLastTime;
                    cpuLastTime = WatchedProcess.TotalProcessorTime.TotalMilliseconds;
                    cpuPercent = cpuTotalTime * 100 / (Environment.ProcessorCount * cpuMeasureTimer.ElapsedMilliseconds);
                    ProcessAnswer = (WatchedProcess.Responding) ? "Активен" : "Неактивен";
                    ProcessCheckResponding(WatchedProcess.Responding, cpuPercent);
                }
                catch
                {
                    cpuTotalTime = 0;
                    cpuPercent = 0;
                    ProcessAnswer = "Неактивен";
                }
                cpuMeasureTimer.Reset();
                cpuMeasureTimer.Start();

                Status($"Процесс {ProcessName} уже запущен.", NotifyLevel.logUpdateStatus);
                processInformationLabel.Text = $"Интерфейс: {ProcessAnswer}. ЦПУ: {cpuPercent:f2}% {cpuTotalTime:f2}мсек";
                if (i < (int)numericUpDown1.Value) Status($"Процесс {ProcessName} запущен.", NotifyLevel.logDiscord);
                i = (int)numericUpDown1.Value;
            }
            else
            {
                processInformationLabel.Text = "";
                if (radioButtonRestartTimer.Checked)
                {
                    if (i == (int)numericUpDown1.Value) Status($"Процесс {ProcessName} не найден. Запуск через {i} сек", NotifyLevel.logDiscord);
                    i--;
                    Status($"Процесс {ProcessName} не найден. Запуск через {i}", NotifyLevel.logUpdateStatus);
                }

                if (i <= 0 || radioButtonRestartNow.Checked)
                {
                    i = (int)numericUpDown1.Value;
                    Status($"Запускаем {ProcessName}", NotifyLevel.logUpdateStatus | NotifyLevel.logDiscord);
                    ProcessStart(Settings.Default.startProgramPath, textBoxArguments.Text);
                }
            }
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                Status("Наблюдение отменено.", NotifyLevel.logUpdateStatus | NotifyLevel.logDiscord);
            }
            else if (e.Error != null)
            {
                string error_message = "Error: " + e.Error.Message + "\n" + e.Error.StackTrace;
                Status("Произошла ошибка! Наблюдение остановлено.\n" + error_message, NotifyLevel.logUpdateStatus | NotifyLevel.logDiscord);
                MessageBox.Show(error_message, "Ошибка наблюдения", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                radioButtonDisableWathing.Checked = true;
            }
            else
            {
                Status("Наблюдение остановлено.", NotifyLevel.logUpdateStatus | NotifyLevel.logDiscord);
            }
        }

        private void webhookDiscordToolStripMenuItem_Click(object sender, EventArgs e)
        {
            WebhookSettings discordSettings;
            discordSettings = new WebhookSettings(this);
            discordSettings.ShowDialog(this);
            discordSettings.Dispose();
        }

        private void AboutProgram_Click(object sender, EventArgs e)
        {
            AboutBox aboutBox = new AboutBox();
            aboutBox.ShowDialog(this);
            aboutBox.Dispose();
        }

        /// <summary>
        /// Окно показано: если это первый запуск после обновления, убираем *.old и скачанные файлы.
        /// Раньше нельзя: если новая версия упадёт до показа окна, *.old нужны для ручного отката.
        /// </summary>
        private void MainWindow_Shown(object sender, EventArgs e)
        {
            if (!startedAfterUpdate)
            {
                return;
            }
            Updater.CompleteUpdate();
            Status($"Утилита обновлена до версии {Updater.CurrentVersion}.", NotifyLevel.logHistory);
        }

        /// <summary>
        /// Проверка и установка обновления по кнопке. Наблюдение во время проверки и скачивания продолжается.
        /// </summary>
        private async void UpdateMenuItem_Click(object sender, EventArgs e)
        {
            Version current = Updater.CurrentVersion;
            updateMenuItem.Enabled = false;
            try
            {
                Status("Проверка обновлений...");
                ReleaseInfo release = await Updater.GetLatestReleaseAsync();
                if (release.Version <= current)
                {
                    string upToDate = $"Установлена последняя версия {current}.";
                    Status(upToDate);
                    MessageBox.Show(this, upToDate, "Обновление",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string notes = release.Notes.Length > 1500 ? release.Notes.Substring(0, 1500) + "\n..." : release.Notes;
                var answer = MessageBox.Show(this,
                    $"Доступна версия {release.Version} (установлена {current}).\n\n{notes}\n\n" +
                    "Обновить? Утилита перезапустится, Torch при этом не перезапускается.",
                    "Обновление", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    Status("Обновление отменено.");
                    return;
                }

                Status($"Скачивание версии {release.Version}...");
                string files = await Updater.DownloadAsync(release);
                Status($"Обновление с версии {current} до {release.Version}, перезапуск утилиты.", NotifyLevel.logHistory | NotifyLevel.logDiscord);
                await Updater.InstallAndRestartAsync(files);
                Close();
            }
            catch (Exception ex)
            {
                Status($"Обновление не удалось: {ex.Message}. Работает версия {current}.", NotifyLevel.logUpdateStatus | NotifyLevel.logHistory | NotifyLevel.logDiscord, ex);
                MessageBox.Show(this, $"Обновление не удалось:\n{ex.Message}", "Обновление",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                updateMenuItem.Enabled = true;
            }
        }
    }
}
