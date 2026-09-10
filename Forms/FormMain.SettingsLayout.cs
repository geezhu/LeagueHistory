namespace League;

public partial class FormMain
{
    private void ConfigureSettingsLayout()
    {
        tabControl1.Font=new Font("Microsoft YaHei UI",9);
        tabControl1.SizeMode=TabSizeMode.Fixed;tabControl1.ItemSize=new Size(116,36);
        tabControl1.DrawMode=TabDrawMode.OwnerDrawFixed;
        tabControl1.DrawItem+=(_,e)=>
        {
            bool selected=e.Index==tabControl1.SelectedIndex;
            using var background=new SolidBrush(selected?Color.White:Color.FromArgb(239,244,248));
            e.Graphics.FillRectangle(background,e.Bounds);
            TextRenderer.DrawText(e.Graphics,tabControl1.TabPages[e.Index].Text,tabControl1.Font,e.Bounds,selected?Color.FromArgb(0,143,153):Color.FromArgb(95,113,130),TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter);
            if(selected){using var line=new SolidBrush(Color.FromArgb(0,153,163));e.Graphics.FillRectangle(line,e.Bounds.X+18,e.Bounds.Bottom-3,e.Bounds.Width-36,3);}
        };
        var general=SettingsPage(tabPage4,"功能辅助","配置对局流程、英雄预选和客户端维护。开关修改后自动保存。");
        chkAutoAccept.Text="自动接受对局";
        SettingsCard(general,"对局自动化",checkBoxFilterMode,chkAutoAccept,chkSkipHonor,chkSkipEndOfGameStats);
        var assistant=new CheckBox {Text="选人时自动显示侧边助手",Checked=_appConfig?.EnableChampionSelectAssistant??true};
        assistant.CheckedChanged+=(_,_)=>{if(_appConfig!=null){_appConfig.EnableChampionSelectAssistant=assistant.Checked;SaveAppConfig();}};
        SettingsCard(general,"选人助手",assistant,new Label {Text="贴靠客户端显示英雄推荐，可固定位置或收起；离开选人后自动隐藏。符文仅在点击“应用符文”时修改。"});
        lkbPreliminary.Text="配置预选英雄 →";
        SettingsCard(general,"英雄预选",new Label {Text="分路信息来自英雄联盟攻略中心，可按模式分别启用。"},lkbPreliminary,chkNormal,chkRanked,chkAram,chkNexus);
        label6.Text="大厅、选人或读秒界面卡顿时，可尝试重启客户端界面。";
        label7.Text="退出游戏后，清理仍占用资源的客户端进程。";
        SettingsCard(general,"客户端维护",label6,btnRestartUx,label7,btnCloseClients);
        SettingsCard(general,"关于与交流",label4,lk_QQGroup,label5,lk_GitPro);
        groupBox1.Dispose();groupBox2.Dispose();groupBox3.Dispose();

        var messages=SettingsPage(tabPage5,"消息发送","选择发送内容，并配置显示方式。实际发送仍由快捷键触发。");
        label2.Text="【游戏内快捷键需要管理员权限】请以管理员身份运行本程序。\n选人窗口：Ctrl + F7 发送我方战绩。游戏内：F9 发送我方战绩，F11 发送双方战绩。\n大乱斗 / 海克斯大乱斗：Ctrl + F9 发送我方梯队与胜率，Ctrl + F11 发送对方梯队与胜率（发到当前游戏聊天频道）。";
        label3.Text="自定义内容每行一条，使用相同快捷键发送。\n游戏内发送前请关闭聊天输入框和游戏菜单，按快捷键后松开 Ctrl，并保持游戏在前台。程序启动时需要允许管理员权限。";
        SettingsCard(messages,"发送内容",rbModeMatch,rbModeCustom,label2,label3,txtCustomContent);
        SettingsCard(messages,"战绩显示",chkHideSelf,chkUseChampionName);
        var logButton=new Button{Text="打开快捷发送日志",AutoSize=true};
        logButton.Click+=(_,_)=>{try{var path=Services.ChatSendDiagnostics.Shared.LogPath;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo{FileName=System.IO.File.Exists(path)?path:System.IO.Path.GetDirectoryName(path)!,UseShellExecute=true});}
            catch{Services.ChatSendDiagnostics.Shared.Report("log-error","无法打开日志文件，请检查本地文件权限。");}};
        SettingsCard(messages,"快捷发送状态",new Controls.ChatSendStatusLabel(),logButton,
            new Label{Text="日志记录触发、数据读取和输入结果，不记录聊天正文。路径："+Services.ChatSendDiagnostics.Shared.LogPath});
        foreach(TabPage page in tabControl1.TabPages)
        {
            if(page==tabPage4 || page==tabPage5)continue;
            if(page.Text=="客户端目录")
            {
                var group=page.Controls.OfType<GroupBox>().First();
                var launcher=group.Controls[0];group.Controls.Remove(launcher);
                var layout=SettingsPage(page,"客户端目录","管理英雄联盟安装路径，用于启动客户端和检测连接。");
                launcher.Dock=DockStyle.None;
                SettingsCard(layout,"英雄联盟安装目录",launcher);group.Dispose();
            }
            else if(page.Text=="数据缓存")
            {
                var original=page.Controls.OfType<FlowLayoutPanel>().First();
                var children=original.Controls.Cast<Control>().ToArray();
                foreach(var child in children)original.Controls.Remove(child);
                var layout=SettingsPage(page,"数据缓存","复用本地统计数据，减少重复请求与加载等待。");
                SettingsCard(layout,"arammeta · 海克斯大乱斗",children.Take(3).ToArray());
                SettingsCard(layout,"aramonly · 普通大乱斗平衡",children.Skip(3).ToArray());original.Dispose();
            }
        }
    }

    private FlowLayoutPanel SettingsPage(TabPage page,string title,string description)
    {
        page.Controls.Clear();page.Padding=Padding.Empty;page.UseVisualStyleBackColor=false;
        page.BackColor=Color.FromArgb(244,247,251);
        var flow=new FlowLayoutPanel {Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(24,20,24,20)};
        page.Controls.Add(flow);
        var heading=new Label {Text=title,AutoSize=true,Font=new Font(tabControl1.Font.FontFamily,17,FontStyle.Bold),ForeColor=Color.FromArgb(35,58,76),Margin=new Padding(0,0,0,8)};
        flow.Controls.Add(heading);
        flow.Controls.Add(new Label {Text=description,AutoSize=true,ForeColor=Color.FromArgb(112,129,145),Margin=new Padding(0,0,0,22)});
        flow.SizeChanged+=(_,_)=>ResizeSettings(flow);
        return flow;
    }

    private void SettingsCard(FlowLayoutPanel parent,string title,params Control[] controls)
    {
        var card=new FlowLayoutPanel {FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,Padding=new Padding(20,16,20,16),BackColor=Color.White,Margin=new Padding(0,0,0,14)};
        card.Controls.Add(new Label {Text=title,AutoSize=true,Font=new Font(tabControl1.Font,FontStyle.Bold),ForeColor=Color.FromArgb(39,62,79),Margin=new Padding(0,0,0,14)});
        foreach(var control in controls)
        {
            control.Dock=DockStyle.None;control.Anchor=AnchorStyles.Top|AnchorStyles.Left;
            control.Margin=new Padding(0,0,0,10);control.ForeColor=Color.FromArgb(59,81,99);
            if(control is CheckBox check)check.AutoSize=true;
            if(control is RadioButton radio)radio.AutoSize=true;
            if(control is Label label){label.AutoSize=true;if(label is not LinkLabel)label.ForeColor=Color.FromArgb(105,122,137);}
            if(control is LinkLabel link){link.LinkColor=Color.FromArgb(0,143,153);link.LinkBehavior=LinkBehavior.HoverUnderline;}
            if(control is Button button){button.AutoSize=false;button.Size=new Size(136,34);button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=Color.FromArgb(207,220,230);button.BackColor=Color.FromArgb(247,250,252);}
            if(control is TextBox box && box.Multiline){box.Height=132;box.BorderStyle=BorderStyle.FixedSingle;}
            card.Controls.Add(control);
        }
        parent.Controls.Add(card);ResizeSettings(parent);
    }

    private void ResizeSettings(FlowLayoutPanel flow)
    {
        int width=Math.Max(300,Math.Min(940,flow.ClientSize.Width-64));
        foreach(var label in flow.Controls.OfType<Label>())label.MaximumSize=new Size(width,0);
        foreach(var card in flow.Controls.OfType<FlowLayoutPanel>())
        {
            card.MinimumSize=new Size(width,0);card.MaximumSize=new Size(width,0);card.Width=width;
            foreach(Control control in card.Controls)
            {
                if(control is Label or CheckBox or RadioButton)control.MaximumSize=new Size(width-40,0);
                else if(control is TextBox || control is League.Controls.LolLauncherPanel)control.Width=width-40;
            }
        }
    }
}
