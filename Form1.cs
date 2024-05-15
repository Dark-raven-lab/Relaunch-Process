using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using RelaunchProcess.Properties;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace Process_Auto_Relaunch
{
    public partial class Form1 : Form
    {
        private delegate void UpdateLogDelegate(string text);
        private UpdateLogDelegate updateLogDelegate = null;

        public Form1()
        {
            InitializeComponent();
            this.updateLogDelegate = new UpdateLogDelegate(this.UpdateStatus);
            myBackgroundWorker.WorkerSupportsCancellation = true;
        }

        /// <summary>
        /// ������� ������� �����
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Form1_Load(object sender, EventArgs e)
        {
            LoadOldState();


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

        private void UpdateStatus(string text)
        {
            labelStatus.Text = text;
        }

        private void Status(string text)
        {
            Invoke(updateLogDelegate, new[] { text });
        }

        private void CheckProgramState()
        {
            bool watching = radioButtonEnableWathing.Checked;
            Debug.WriteLine($"����������: {watching}");

            groupBoxProcessName.Enabled = !watching;
            groupBoxProgramStart.Enabled = !watching;
            groupBoxActions.Enabled = !watching;

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
            openFile.Title = "������� ��������� �������";

            if (openFile.ShowDialog() == DialogResult.Cancel)
            {
                return;
            }

            //labelProgramStartPath.Text = openFile.FileName;
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
            return Process.GetProcessesByName(name).Length > 0;
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
                    i = (int)numericUpDown1.Value;
                }
                else
                {
                    if (radioButtonRestartTimer.Checked)
                    {
                        i--;
                        Status($"������� {textBoxProcessName.Text} �� ������. ������ ����� {i}");
                    }

                    if (i <= 0 || radioButtonRestartNow.Checked)
                    {
                        Status("���������...");
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
                Status("��������� ������. ���������� �����������!");
                radioButtonDisableWathing.Checked = true;
            }
            else
            {
                Status("���������� �����������.");
            }
        }
    }
}
