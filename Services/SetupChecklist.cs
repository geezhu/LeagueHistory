namespace League.Services;

public enum SetupItemState
{
    Checking,
    Ready,
    NeedsAction,
    Optional,
}

public sealed record SetupItem(SetupItemState State, string Detail);

/// <summary>
/// Turns what was detected on this computer into the 准备情况 page's items. An item is only "needs action" when a
/// feature the user has chosen cannot work without it; something unused stays optional rather than looking broken.
/// </summary>
public static class SetupChecklist
{
    public static SetupItem Administrator(bool elevated) =>
        elevated
            ? new(SetupItemState.Ready, "已以管理员身份运行")
            : new(
                SetupItemState.NeedsAction,
                "未以管理员身份运行 · 游戏内快捷键（F9 / F11）和 Game Bar 发送需要管理员权限，否则游戏不会响应"
            );

    /// <param name="gameBarChosen">The loading overlay is on and shown through Game Bar.</param>
    /// <param name="status">The component's versions, or <c>null</c> while they are still being read.</param>
    public static SetupItem GameBar(bool gameBarChosen, GameBarPackageStatus? status)
    {
        if (status == null)
        {
            return new(SetupItemState.Checking, "正在检查组件…");
        }

        if (status.State == GameBarPackageState.Current)
        {
            return new(SetupItemState.Ready, status.Text);
        }

        if (status.State == GameBarPackageState.NotBundled && status.Installed != null)
        {
            // Installed by hand or by an older release; nothing here can update it.
            return new(SetupItemState.Ready, status.Text);
        }

        return gameBarChosen
            ? new(SetupItemState.NeedsAction, status.Text + " · 对局速览选择了 Game Bar，需要先安装组件")
            : new(SetupItemState.Optional, status.Text + " · 独占全屏时用 Game Bar 显示对局速览才需要");
    }

    public static SetupItem VoiceModel(bool available) =>
        available
            ? new(SetupItemState.Ready, "中文语音模型已下载")
            : new(
                SetupItemState.Optional,
                $"未下载 · 只在语音发送时需要，约 {VoiceModelStore.Files.Sum(file => file.Size) / (1024 * 1024)} MiB"
            );
}
