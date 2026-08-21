namespace ZaprosiVutm
{
    partial class MainForm
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
            this.bStart = new System.Windows.Forms.Button();
            this.tbPriceForLiter = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // bStart
            // 
            this.bStart.Location = new System.Drawing.Point(13, 12);
            this.bStart.Name = "bStart";
            this.bStart.Size = new System.Drawing.Size(154, 23);
            this.bStart.TabIndex = 1;
            this.bStart.Text = "Старт";
            this.bStart.UseVisualStyleBackColor = true;
            this.bStart.Click += new System.EventHandler(this.bStart_Click);
            // 
            // tbPriceForLiter
            // 
            this.tbPriceForLiter.Location = new System.Drawing.Point(13, 47);
            this.tbPriceForLiter.Name = "tbPriceForLiter";
            this.tbPriceForLiter.Size = new System.Drawing.Size(154, 20);
            this.tbPriceForLiter.TabIndex = 4;
            this.tbPriceForLiter.Text = "100";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 76);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(83, 13);
            this.label1.TabIndex = 5;
            this.label1.Text = "Цена за 1 литр";
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(184, 98);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.tbPriceForLiter);
            this.Controls.Add(this.bStart);
            this.MinimumSize = new System.Drawing.Size(200, 137);
            this.Name = "MainForm";
            this.Text = "Робот по списанию пива";
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Button bStart;
        private System.Windows.Forms.TextBox tbPriceForLiter;
        private System.Windows.Forms.Label label1;
    }
}