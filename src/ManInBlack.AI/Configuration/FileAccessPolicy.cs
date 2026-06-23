namespace ManInBlack.AI.Configuration;

/// <summary>
/// 文件访问「纯允许列表」策略:FileTools(.NET 校验)与 bwarp(挂载)的唯一共享真相。
/// 默认拒绝:仅 Workspace、Temp(可读写)与配置的 ReadableRoots(只读)可读。其余一律不可读。
/// 隔离强度 = 所配 ReadableRoots 的窄度;配 "/" 等于关闭隔离(操作者显式开关,见 spec §7)。
/// </summary>
public sealed record FileAccessPolicy
{
    /// <summary>可读写:当前用户 workspace(= IUserWorkspace.WorkingDirectory)。</summary>
    public string Workspace { get; init; } = "";

    /// <summary>可读写:系统临时目录。</summary>
    public string Temp { get; init; } = "";

    /// <summary>额外只读根(经配置添加)。默认空。</summary>
    public IReadOnlyList<string> ReadableRoots { get; init; } = [];

    /// <summary>
    /// 注入沙盒的环境变量(env 名 → 明文值),供 bwarp 命令读取。FileTools 忽略此项。
    /// 默认空;值经配置(FileIsolation.InjectedEnv)提供。settings.json 文件本身不因此可见。
    /// </summary>
    public IReadOnlyDictionary<string, string> InjectedEnv { get; init; } = new Dictionary<string, string>();

    public bool IsReadable(string resolvedPath) =>
        IsUnderOrEqual(resolvedPath, Workspace)
        || IsUnderOrEqual(resolvedPath, Temp)
        || ReadableRoots.Any(r => IsUnderOrEqual(resolvedPath, r));

    public bool IsWritable(string resolvedPath) =>
        IsUnder(resolvedPath, Workspace) || IsUnder(resolvedPath, Temp);

    /// <summary>规范化:取绝对路径并去掉尾部目录分隔符。</summary>
    internal static string Canonicalize(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>p 严格在 root 之下(以 "root/" 为前缀,且非仅前缀字符串相同)。</summary>
    internal static bool IsUnder(string path, string root)
    {
        var p = Canonicalize(path);
        var r = Canonicalize(root);
        if (r.Length == 0 || p.Length <= r.Length) return false;
        return p.StartsWith(r, StringComparison.OrdinalIgnoreCase)
            && (p[r.Length] == Path.DirectorySeparatorChar
                || p[r.Length] == Path.AltDirectorySeparatorChar);
    }

    /// <summary>p 等于 root 或在其下。</summary>
    internal static bool IsUnderOrEqual(string path, string root)
    {
        var p = Canonicalize(path);
        var r = Canonicalize(root);
        if (r.Length == 0) return false;
        return string.Equals(p, r, StringComparison.OrdinalIgnoreCase) || IsUnder(p, r);
    }
}
