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
        IEnumerable<Control> All(Control root)=>root.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(All(c)));
        var count=All(tabs).OfType<ComboBox>().Single(c=>c.Name=="CardMatchHistoryCount");
        foreach(var name in new[]{"ShutdownPlanSummary","ShutdownNextRun","CreateShutdownPlan","DeleteShutdownPlan","ShutdownLog"})
            if(!All(tabs).Any(c=>c.Name==name))throw new Exception("Missing daily shutdown management control: "+name);
        var shutdownCard=All(tabs).Single(c=>c.Name=="ShutdownPlanSummary").Parent!;
        if(!All(shutdownCard).OfType<Button>().Select(c=>c.Name).SequenceEqual(new[]{"CreateShutdownPlan","DeleteShutdownPlan","ShutdownLog"}))
            throw new Exception("Shutdown settings only expose create, delete and logs");
        if(All(tabs).Any(c=>c.Name=="LoadingOverlayHotkey"))throw new Exception("Removed overlay shortcut must not remain configurable");
        if(!All(tabs).Any(c=>c.Text.Contains("Win＋G")))throw new Exception("Game Bar management instructions remain available");
        if(!All(tabs).OfType<CheckBox>().Single(c=>c.Name=="AutoShowLoadingOverlay").Checked)throw new Exception("Automatic loading overlay defaults on");
        if(!All(tabs).OfType<Button>().Single(c=>c.Name=="PreviewLoadingOverlay").Enabled)throw new Exception("Desktop overlay preview is available without a match");
        if(!count.Items.Cast<int>().SequenceEqual(new[]{10,20,30,50}) || !Equals(count.SelectedItem,20))throw new Exception("Upstream history count choices default to 20");
        var delay=All(tabs).OfType<ComboBox>().Single(c=>c.Name=="AutoAcceptDelaySeconds");
        ((CheckBox)original).Checked=true;
        if(!delay.Enabled || delay.Items.Count!=3)throw new Exception("Auto-accept delay follows enable switch");
        ((CheckBox)original).Checked=false;
        if(delay.Enabled)throw new Exception("Disabled auto accept also disables delay");
        using var preview=new Form {ClientSize=new Size(1050,800)};
        preview.Controls.Add(tabs);preview.Show();Application.DoEvents();
        Save("settings-general");
        var shutdown=All(tabs).Single(c=>c.Name=="ShutdownPlanSummary");
        ((FlowLayoutPanel)shutdown.Parent!.Parent!).AutoScrollPosition=new Point(0,Math.Max(0,shutdown.Parent.Top-16));
        Application.DoEvents();Save("settings-shutdown");
        preview.ClientSize=new Size(650,720);Application.DoEvents();Save("settings-shutdown-narrow");preview.ClientSize=new Size(1050,800);
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
