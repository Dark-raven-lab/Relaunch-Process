using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using RelaunchProcess.Properties;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Discord;
using Discord.Webhook;
using System.Data;
using static System.Net.Mime.MediaTypeNames;
using RelaunchProcess;


namespace Process_Auto_Relaunch
{
    public partial class Form1 : Form
    {
        private delegate void UpdateLogDelegate(string text, bool add_history = false);
        private UpdateLogDelegate updateLogDelegate;
        private DiscordWebhook dwhHook;
        private DiscordMessage dwhMessage;
        private DiscordSettings discordSettings;

        public Form1()
        {
            InitializeComponent();
            this.updateLogDelegate = this.UpdateStatus;
            this.updateLogDelegate += this.SendDiscordMessage;
            myBackgroundWorker.WorkerSupportsCancellation = true;
            dwhHook = new DiscordWebhook();
            if ( Uri.IsWellFormedUriString(Settings.Default.dwhURL,UriKind.Absolute) && Settings.Default.dwhEnabled && Settings.Default.dwhURL!="") 
            {
                dwhHook.Url = Settings.Default.dwhURL;
            }
            else if (Settings.Default.dwhEnabled) { 
                Debug.WriteLine($"������ � URL ���-���� ({Settings.Default.dwhURL}). ����� � Discord ��������.");
                HistoryLog($"������ � URL ���-���� ({Settings.Default.dwhURL}). ����� � Discord ��������.");
                Settings.Default.dwhEnabled = false;
                Settings.Default.Save();
            }

        }

        /// <summary>
        /// ������� ������� �����
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Form1_Load(object sender, EventArgs e)
        {
            LoadOldState();

            //MessageBox.Show(Environment.UserDomainName);

            CheckProgramState();
        }

        /// <summary>
        /// �������������� ��������
        /// </summary>
        private void LoadOldState()
        {
            if (Settings.Default.saveOldState)
            {
                radioButtonEnableWathing.Checked = Settings.Default.enableWatching;
            }
            
        }

        /// <summary>
        /// ����� ��� ������� ����������
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
                UpdateStatus("��������...");
            }
        }

        /// <summary>
        /// ����� ��� ������� ���������
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
                MessageBox.Show("��� �������� �� ����� ���� ������!" +
                    "\n������� ��� ��������", "������", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            if (String.IsNullOrEmpty(Settings.Default.startProgramPath))
            {
                error = true;
                MessageBox.Show("��������� ��� ������� �� �������." +
                    "\n������� ��������� ��� �������", "������", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            }
        }

        /// <summary>
        /// ���������� ������� � ���������
        /// </summary>
        /// <param name="text">����� ��� �����������</param>
        /// <param name="add_history">���������� ������ � ���� �������</param>
        public void UpdateStatus(string text, bool add_history = false)
        {
            labelStatus.Text = text;

            if (add_history)
            {
                HistoryLog(text);
            }
        }

        private void HistoryLog(string text)
        {
            richTextBoxHistory.Text += DateTime.Now.ToString() + ": " + text + "\n";
        }

        public void Status(string text, bool add_history = false)
        {
            Invoke(updateLogDelegate, text, add_history);
        }

        private void CheckProgramState()
        {
            bool watching = radioButtonEnableWathing.Checked;
            Debug.WriteLine($"����������: {watching}");

            groupBoxProcessName.Enabled = !watching;
            groupBoxProgramStart.Enabled = !watching;
            groupBoxActions.Enabled = !watching;
            btnShowDiscordSettings.Enabled = !watching;

            Settings.Default.enableWatching = watching;

            
        }

        /// <summary>
        /// ����� ����� ��� �������
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonSetProgramStart_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFile = new OpenFileDialog();
            openFile.Filter = "����������� ����� (*.exe)|*.exe";
            openFile.Title = "������� ��������� �������";

            if (openFile.ShowDialog() == DialogResult.Cancel)
            {
                return;
            }

            int lastSlash = openFile.FileName.LastIndexOf("\\");
            textBoxProcessName.Text = openFile.FileName.Substring(lastSlash+1);
            textBoxProcessName.Text = textBoxProcessName.Text.Remove(textBoxProcessName.Text.Length-4);
            Settings.Default.startProgramPath = openFile.FileName;
            Settings.Default.Save();

            openFile.Dispose();
        }

        /// <summary>
        /// ������� ����� ��������� �����
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            Settings.Default.Save();
        }

        private bool ProcessByNameIsRuning(string name)
        {
            var sessionid = Process.GetCurrentProcess().SessionId;
            var processes = Process.GetProcessesByName(name);
            foreach (var process in processes)
            {
                Debug.WriteLine($"Found proces: {process.ProcessName}. Session Id: {process.SessionId}. Current Session Id: {sessionid}");
                if (process.SessionId == sessionid)
                    return true;
            }

            Debug.WriteLine($"Process {name} for current session id {sessionid} not found");
            return false;
        }

        private void ProcessStart(string path, string args)
        {
            if (checkBoxCheckProcess.Checked)
            {
                if (ProcessByNameIsRuning(path))
                {
                    return;
                }
            }

            Status("������� ��� �������.", true);
            Process.Start(path, args);
        }

        private void BackgroundWorkerDoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            int i = (int)numericUpDown1.Value;

            while (!worker.CancellationPending)
            {
                if (ProcessByNameIsRuning(textBoxProcessName.Text))
                {
                    Status($"������� {textBoxProcessName.Text} ��� �������");
                    if (i < (int)numericUpDown1.Value) SendDiscordMessage($"������� {textBoxProcessName.Text} �������.",true);
                    i = (int)numericUpDown1.Value;
                }
                else
                {
                    if (radioButtonRestartTimer.Checked)
                    {
                        if (i==(int)numericUpDown1.Value) SendDiscordMessage($"������� {textBoxProcessName.Text} �� ������. ������ ����� {i}",true);
                        i--;
                        Status($"������� {textBoxProcessName.Text} �� ������. ������ ����� {i}");
                    }

                    if (i <= 0 || radioButtonRestartNow.Checked)
                    {
                        i = (int)numericUpDown1.Value;
                        Status("���������...");
                        SendDiscordMessage($"��������� {textBoxProcessName.Text}",true);
                        ProcessStart(Settings.Default.startProgramPath, textBoxArguments.Text);
                    }
                }

                Thread.Sleep(1000);
            }

            if (worker.CancellationPending)
            {
                e.Cancel = true;
            }
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                Status("���������� ��������.");
            }
            else if (e.Error != null)
            {
                MessageBox.Show("Error: " + e.Error.Message, "������", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                Status("��������� ������! ���������� �����������.", true);
                radioButtonDisableWathing.Checked = true;
            }
            else
            {
                Status("���������� �����������.");
            }
        }

        /// <summary>
        /// �������� ��������� � �������
        /// </summary>
        /// <param name="text">����� ��� ��������</param>
        public void SendDiscordMessage(string message, bool addToHistory = false)
        {
            if (Settings.Default.dwhEnabled && addToHistory)
            {
                dwhHook.Url = Settings.Default.dwhURL;
                dwhMessage.Username = "Relaunch process";
                dwhMessage.Content = ":arrows_counterclockwise: " + message;
                try
                {
                    dwhHook.Send(dwhMessage);
                }
                catch (Exception ex)
                {
                    HistoryLog($"Discord messaging error: {ex.Message}");
                    Debug.WriteLine($"Discord messaging error: {ex.Message}");
                    Settings.Default.dwhEnabled = false;
                    Settings.Default.Save();
                }
            }

        }

        private void btnShowDiscordSettings_Click(object sender, EventArgs e)
        {
            discordSettings = new DiscordSettings();
            discordSettings.ShowDialog(this);
            discordSettings.Dispose();
        }
    }
}
