using ManInBlack.AI.Abstraction.Storage;

namespace ManInBlack.AI.Configuration;

/// <summary>
/// 流式构建 StorageSettings。
/// </summary>
public sealed class StorageBuilder
{
    internal StorageSettings Settings { get; } = new();

    /// <summary>设置存储根路径。</summary>
    public StorageBuilder RootPath(string rootPath) { Settings.RootPath = rootPath; return this; }

    /// <summary>配置工作空间设置。</summary>
    public StorageBuilder Workspace(Action<WorkspaceBuilder> configure)
    {
        var b = new WorkspaceBuilder();
        configure(b);
        Settings.Workspace = b.Settings;
        return this;
    }

    /// <summary>追加一个额外只读根(bwarp 与 FileTools 均据此放行)。</summary>
    public StorageBuilder AddReadableRoot(string root)
    {
        Settings.FileIsolation ??= new FileIsolationSettings();
        Settings.FileIsolation.ReadableRoots.Add(root);
        return this;
    }
}

/// <summary>
/// 流式构建 WorkspaceSettings。
/// </summary>
public sealed class WorkspaceBuilder
{
    internal WorkspaceSettings Settings { get; } = new();

    /// <summary>设置工作空间模式。</summary>
    public WorkspaceBuilder Mode(WorkspaceMode mode) { Settings.Mode = mode; return this; }

    /// <summary>设置 CustomPath 模式下的显式路径。</summary>
    public WorkspaceBuilder CustomPath(string path) { Settings.CustomPath = path; return this; }
}
