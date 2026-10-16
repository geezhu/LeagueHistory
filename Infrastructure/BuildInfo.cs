using System.Globalization;
using System.Reflection;
using League.Models;

namespace League.Infrastructure;

/// <summary>
/// 构建来源：commit 由 .NET SDK 写进产品版本（版本号+完整 commit），未提交改动和构建时间由 League.csproj 写入程序集元数据。
/// 显示用的版本号仍然来自 version.txt，自动更新按它和上游比较，所以这里不改版本号规则。
/// </summary>
public sealed record BuildInfo(string Version, string Commit, bool? Dirty, DateTime? BuiltAtUtc)
{
    public static BuildInfo Current { get; } = FromAssembly(typeof(BuildInfo).Assembly);

    public string ShortCommit => Commit.Length > 7 ? Commit[..7] : Commit;

    /// <summary>设置页和诊断报告用的一行说明，例如「v1.1.4 · 提交 07688b8（含未提交改动）」。</summary>
    public string Describe()
    {
        string version = Version == "0.0.0" ? "开发版" : "v" + Version;
        if (Commit.Length == 0)
        {
            return version + " · 提交未知";
        }

        string state = Dirty switch
        {
            true => "（含未提交改动）",
            null => "（无法确认是否有未提交改动）",
            false => "",
        };
        return $"{version} · 提交 {ShortCommit}{state}";
    }

    public static BuildInfo FromAssembly(Assembly assembly)
    {
        string informational =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var metadata = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(a => a.Key, a => a.Value ?? "", StringComparer.Ordinal);
        return Parse(VersionInfo.GetLocalVersion(), informational, metadata);
    }

    /// <summary>
    /// 产品版本形如「1.0.0+commit」；没有 git 的环境下构建时没有「+」部分，commit 视为未知。
    /// </summary>
    public static BuildInfo Parse(
        string version,
        string informationalVersion,
        IReadOnlyDictionary<string, string> metadata
    )
    {
        int plus = informationalVersion.IndexOf('+');
        string commit = plus >= 0 ? informationalVersion[(plus + 1)..].Trim() : "";
        bool? dirty = metadata.TryGetValue("SourceDirty", out var value)
            ? value switch
            {
                "true" => true,
                "false" => false,
                _ => null,
            }
            : null;
        DateTime? builtAt =
            metadata.TryGetValue("BuildTimeUtc", out var time)
            && DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed.ToUniversalTime()
                : null;
        return new BuildInfo(version, commit, dirty, builtAt);
    }
}
