namespace League.ChampionSkin
{
    partial class ChampionSkinCard
    {
        /// <summary> 
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region 组件设计器生成的代码

        /// <summary> 
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            picHero = new PictureBox();
            flpSkins = new FlowLayoutPanel();
            ((System.ComponentModel.ISupportInitialize)picHero).BeginInit();
            SuspendLayout();
            // 
            // picHero
            // 
            picHero.Image = Properties.Resources.无极剑圣;
            picHero.Location = new Point(3, 3);
            picHero.Name = "picHero";
            picHero.Size = new Size(150, 150);
            picHero.SizeMode = PictureBoxSizeMode.Zoom;
            picHero.TabIndex = 1;
            picHero.TabStop = false;
            // 
            // flpSkins
            // 
            flpSkins.Location = new Point(3, 159);
            flpSkins.Name = "flpSkins";
            flpSkins.Size = new Size(150, 109);
            flpSkins.TabIndex = 2;
            // 
            // ChampionSkinCard
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(flpSkins);
            Controls.Add(picHero);
            Name = "ChampionSkinCard";
            Size = new Size(158, 272);
            ((System.ComponentModel.ISupportInitialize)picHero).EndInit();
            ResumeLayout(false);
        }

        #endregion
        private PictureBox picHero;
        private FlowLayoutPanel flpSkins;
    }
}
