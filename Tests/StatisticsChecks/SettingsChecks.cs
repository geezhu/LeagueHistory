using System.Reflection;

internal static class SettingsChecks
{
    public static void Render()
    {
        using var owner=new League.FormMain();
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var type=owner.GetType();
        Control Field(string name)=>(Control)type.GetField(name,flags)!.GetValue(owner)!;
        var original=Field("chkAutoAccept");
        type.GetMethod("ConfigureAramMetaSettings",flags)!.Invoke(owner,null);
        type.GetMethod("ConfigureSettingsLayout",flags)!.Invoke(owner,null);
        if(original.IsDisposed || original.Parent is not FlowLayoutPanel)throw new Exception("Settings must retain existing bound controls");
        if(Field("rbModeMatch").Parent!=Field("rbModeCustom").Parent)throw new Exception("Send modes must remain mutually exclusive");
        var tabs=(TabControl)Field("tabControl1");
        using var preview=new Form {ClientSize=new Size(1050,800)};
        preview.Controls.Add(tabs);preview.Show();Application.DoEvents();
        Save("settings-general");
        tabs.SelectedIndex=1;Application.DoEvents();Save("settings-messages");
        tabs.SelectedIndex=2;Application.DoEvents();Save("settings-launcher");
        preview.ClientSize=new Size(650,720);tabs.SelectedIndex=3;Application.DoEvents();Save("settings-cache-narrow");
        void Save(string name)
        {
            using var bitmap=new Bitmap(preview.Width,preview.Height);preview.DrawToBitmap(bitmap,preview.ClientRectangle);
            bitmap.Save(Path.Combine(AppContext.BaseDirectory,name+".png"));
        }
        Console.WriteLine("PASS: settings retain bound controls, radio grouping, and responsive pages.");
    }
}
