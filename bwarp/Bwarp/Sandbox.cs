namespace Bwarp;

public static class Sandbox
{
    public static SandboxBuilder Run(string command, params string[] args)
    {
        return new SandboxBuilder().WithCommand(command, args);
    }

    /// <summary>
    /// Confine a shell command to a working directory (writable), in a default-deny sandbox.
    /// 只读暴露精选系统路径(供命令运行)与 <paramref name="readableRoots"/>;不绑定整个宿主 FS,
    /// 故同级 workspace 与宿主其他用户数据不可见。Network access is allowed.
    /// <paramref name="injectedEnv"/> 中的环境变量经 --setenv 注入沙盒(供命令读取,如 API key);
    /// 宿主 settings.json 等文件仍不可见。
    /// </summary>
    public static SandboxBuilder Confine(
        string workingDirectory,
        string command,
        IReadOnlyList<string>? readableRoots = null,
        IReadOnlyDictionary<string, string>? injectedEnv = null)
    {
        var home = Environment.GetEnvironmentVariable("HOME") ?? "/root";
        var sb = new SandboxBuilder()
            .WithCommand("/bin/bash", "-c", command)
            .ConfineBaseline()
            .CreateDir(workingDirectory)
            .Bind(workingDirectory, workingDirectory);

        if (readableRoots is not null)
        {
            foreach (var root in readableRoots)
                sb.CreateDir(root).BindReadOnly(root, root);
        }

        if (injectedEnv is not null)
        {
            foreach (var (name, value) in injectedEnv)
                sb.SetEnv(name, value);
        }

        return sb
            .TryBind($"{home}/.cache", $"{home}/.cache")
            .WithWorkingDirectory(workingDirectory);
    }

    /// <summary>旧入口,等价于 readableRoots 为 null。</summary>
    public static SandboxBuilder Confine(string workingDirectory, string command)
        => Confine(workingDirectory, command, readableRoots: null);
}
