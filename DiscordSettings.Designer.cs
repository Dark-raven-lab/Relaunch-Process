﻿namespace RelaunchProcess
{
    partial class DiscordSettings
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.btnOk = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.lblDwhURL = new System.Windows.Forms.Label();
            this.btnClearUrlField = new System.Windows.Forms.Button();
            this.lblDwhBotname = new System.Windows.Forms.Label();
            this.lblDwhAvatarUrl = new System.Windows.Forms.Label();
            this.textDwhAvatarUrl = new System.Windows.Forms.TextBox();
            this.textDwhBotName = new System.Windows.Forms.TextBox();
            this.textDwhURL = new System.Windows.Forms.TextBox();
            this.chbxDiscordEnabled = new System.Windows.Forms.CheckBox();
            this.btnClearAvatarUrlField = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // btnOk
            // 
            this.btnOk.Location = new System.Drawing.Point(215, 155);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new System.Drawing.Size(75, 23);
            this.btnOk.TabIndex = 0;
            this.btnOk.Text = "Сохранить";
            this.btnOk.UseVisualStyleBackColor = true;
            this.btnOk.Click += new System.EventHandler(this.BtnOk_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(296, 155);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 1;
            this.btnCancel.Text = "Отменить";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.BtnCancel_Click);
            // 
            // lblDwhURL
            // 
            this.lblDwhURL.AutoSize = true;
            this.lblDwhURL.Location = new System.Drawing.Point(9, 87);
            this.lblDwhURL.Name = "lblDwhURL";
            this.lblDwhURL.Size = new System.Drawing.Size(147, 13);
            this.lblDwhURL.TabIndex = 4;
            this.lblDwhURL.Text = "URL-адрес Discord веб-хука";
            // 
            // btnClearUrlField
            // 
            this.btnClearUrlField.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.btnClearUrlField.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(192)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))));
            this.btnClearUrlField.Location = new System.Drawing.Point(352, 103);
            this.btnClearUrlField.Name = "btnClearUrlField";
            this.btnClearUrlField.Size = new System.Drawing.Size(19, 19);
            this.btnClearUrlField.TabIndex = 5;
            this.btnClearUrlField.Text = "X";
            this.btnClearUrlField.UseVisualStyleBackColor = true;
            this.btnClearUrlField.Click += new System.EventHandler(this.ClearUrl);
            // 
            // lblDwhBotname
            // 
            this.lblDwhBotname.AutoSize = true;
            this.lblDwhBotname.Location = new System.Drawing.Point(9, 9);
            this.lblDwhBotname.Name = "lblDwhBotname";
            this.lblDwhBotname.Size = new System.Drawing.Size(127, 13);
            this.lblDwhBotname.TabIndex = 6;
            this.lblDwhBotname.Text = "Имя бота в сообщении:";
            // 
            // lblDwhAvatarUrl
            // 
            this.lblDwhAvatarUrl.AutoSize = true;
            this.lblDwhAvatarUrl.Location = new System.Drawing.Point(9, 48);
            this.lblDwhAvatarUrl.Name = "lblDwhAvatarUrl";
            this.lblDwhAvatarUrl.Size = new System.Drawing.Size(102, 13);
            this.lblDwhAvatarUrl.TabIndex = 6;
            this.lblDwhAvatarUrl.Text = "URL аватара бота:";
            // 
            // textDwhAvatarUrl
            // 
            this.textDwhAvatarUrl.DataBindings.Add(new System.Windows.Forms.Binding("Text", global::RelaunchProcess.Properties.Settings.Default, "dwhAvatarURL", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));
            this.textDwhAvatarUrl.Location = new System.Drawing.Point(9, 64);
            this.textDwhAvatarUrl.Name = "textDwhAvatarUrl";
            this.textDwhAvatarUrl.Size = new System.Drawing.Size(337, 20);
            this.textDwhAvatarUrl.TabIndex = 7;
            this.textDwhAvatarUrl.Text = global::RelaunchProcess.Properties.Settings.Default.dwhAvatarURL;
            // 
            // textDwhBotName
            // 
            this.textDwhBotName.DataBindings.Add(new System.Windows.Forms.Binding("Text", global::RelaunchProcess.Properties.Settings.Default, "dwhBotname", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));
            this.textDwhBotName.Location = new System.Drawing.Point(9, 25);
            this.textDwhBotName.MaxLength = 40;
            this.textDwhBotName.Name = "textDwhBotName";
            this.textDwhBotName.Size = new System.Drawing.Size(211, 20);
            this.textDwhBotName.TabIndex = 7;
            this.textDwhBotName.Text = global::RelaunchProcess.Properties.Settings.Default.dwhBotname;
            // 
            // textDwhURL
            // 
            this.textDwhURL.DataBindings.Add(new System.Windows.Forms.Binding("Text", global::RelaunchProcess.Properties.Settings.Default, "dwhURL", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));
            this.textDwhURL.Location = new System.Drawing.Point(9, 103);
            this.textDwhURL.Name = "textDwhURL";
            this.textDwhURL.Size = new System.Drawing.Size(337, 20);
            this.textDwhURL.TabIndex = 3;
            this.textDwhURL.Text = global::RelaunchProcess.Properties.Settings.Default.dwhURL;
            // 
            // chbxDiscordEnabled
            // 
            this.chbxDiscordEnabled.AutoSize = true;
            this.chbxDiscordEnabled.Checked = global::RelaunchProcess.Properties.Settings.Default.dwhEnabled;
            this.chbxDiscordEnabled.DataBindings.Add(new System.Windows.Forms.Binding("Checked", global::RelaunchProcess.Properties.Settings.Default, "dwhEnabled", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));
            this.chbxDiscordEnabled.Location = new System.Drawing.Point(9, 129);
            this.chbxDiscordEnabled.Name = "chbxDiscordEnabled";
            this.chbxDiscordEnabled.Size = new System.Drawing.Size(232, 17);
            this.chbxDiscordEnabled.TabIndex = 2;
            this.chbxDiscordEnabled.Text = "Включить отправку сообщений в Discord";
            this.chbxDiscordEnabled.UseVisualStyleBackColor = true;
            // 
            // btnClearAvatarUrlField
            // 
            this.btnClearAvatarUrlField.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.btnClearAvatarUrlField.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(192)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))));
            this.btnClearAvatarUrlField.Location = new System.Drawing.Point(352, 65);
            this.btnClearAvatarUrlField.Name = "btnClearAvatarUrlField";
            this.btnClearAvatarUrlField.Size = new System.Drawing.Size(19, 19);
            this.btnClearAvatarUrlField.TabIndex = 5;
            this.btnClearAvatarUrlField.Text = "X";
            this.btnClearAvatarUrlField.UseVisualStyleBackColor = true;
            this.btnClearAvatarUrlField.Click += new System.EventHandler(this.ClearUrl);
            // 
            // DiscordSettings
            // 
            this.AcceptButton = this.btnOk;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(387, 188);
            this.ControlBox = false;
            this.Controls.Add(this.textDwhAvatarUrl);
            this.Controls.Add(this.textDwhBotName);
            this.Controls.Add(this.lblDwhAvatarUrl);
            this.Controls.Add(this.lblDwhBotname);
            this.Controls.Add(this.btnClearAvatarUrlField);
            this.Controls.Add(this.btnClearUrlField);
            this.Controls.Add(this.lblDwhURL);
            this.Controls.Add(this.textDwhURL);
            this.Controls.Add(this.chbxDiscordEnabled);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOk);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "DiscordSettings";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.Text = "Настройки Discord webhook";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnOk;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.CheckBox chbxDiscordEnabled;
        private System.Windows.Forms.TextBox textDwhURL;
        private System.Windows.Forms.Label lblDwhURL;
        private System.Windows.Forms.Button btnClearUrlField;
        private System.Windows.Forms.Label lblDwhBotname;
        private System.Windows.Forms.TextBox textDwhBotName;
        private System.Windows.Forms.Label lblDwhAvatarUrl;
        private System.Windows.Forms.TextBox textDwhAvatarUrl;
        private System.Windows.Forms.Button btnClearAvatarUrlField;
    }
}