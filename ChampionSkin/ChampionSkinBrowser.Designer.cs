namespace League.ChampionSkin
{
    partial class ChampionSkinBrowser
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
            components = new System.ComponentModel.Container();
            SuspendLayout();
            //
            // ChampionSkinBrowser
            //
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1180, 760);
            Font = new Font("Microsoft YaHei UI", 9F);
            KeyPreview = true;
            MinimumSize = new Size(900, 600);
            Name = "ChampionSkinBrowser";
            StartPosition = FormStartPosition.CenterParent;
            Text = "英雄皮肤";
            ResumeLayout(false);
        }

        #endregion
    }
}
