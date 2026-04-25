using System.Diagnostics;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Services;

/// <summary>
/// 提供 bash 命令的沙箱化执行能力
/// </summary>
public interface IBashSandbox
{
    /// <summary>
    /// bwrap 沙箱是否可用（Linux + bwrap 已安装 + 配置启用）
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 为命令构造沙箱化的 (可执行文件, 参数列表)。
    /// 返回 null 表示不启用沙箱，调用者应直接使用 bash。
    /// </summary>
    /// <param name="command">用户要执行的 bash 命令</param>
    /// <param name="workspace">工作目录（沙箱内可读写）</param>
    /// <returns>沙箱化参数，或 null 回退到直接执行</returns>
    (string fileName, IReadOnlyList<string> arguments)? WrapCommand(string command, string workspace);
}

/// <summary>
/// 基于 Bubblewrap (bwrap) 的 bash 命令沙箱。
/// 在 Linux 上通过 user namespace 将文件系统挂为只读，
/// 仅 workspace 可读写，/home、/root 等敏感目录用 tmpfs 隐藏。
/// </summary>
[ServiceRegister.Scoped.As<IBashSandbox>]
public class BashSandbox : IBashSandbox
{
    private bool? _isAvailable;
    private readonly bool _sandboxEnabled;
    private readonly ILogger<BashSandbox> _logger;

    public BashSandbox(IOptions<AgentStorageOptions> options, ILogger<BashSandbox> logger)
    {
        _sandboxEnabled = options.Value.SandboxEnabled;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsLinux() || !_sandboxEnabled)
                return false;

            if (_isAvailable.HasValue)
                return _isAvailable.Value;

            _isAvailable = CheckBwrapAvailable();
            if (!_isAvailable.Value)
                _logger.LogWarning("bwrap is not installed. Bash commands will run without filesystem sandbox. Install with: apt install bubblewrap");

            return _isAvailable.Value;
        }
    }

    /// <inheritdoc />
    public (string fileName, IReadOnlyList<string> arguments)? WrapCommand(string command, string workspace)
    {
        if (!OperatingSystem.IsLinux() || !_sandboxEnabled)
            return null;

        if (!IsAvailable)
            return null;

        // bwrap 参数：整个文件系统只读，workspace 可读写，敏感目录用 tmpfs 隐藏
        var args = new List<string>
        {
            "--ro-bind", "/", "/",
            "--bind", workspace, workspace,
            "--chdir", workspace,
            "--dev", "/dev",
            "--proc", "/proc",
            "--tmpfs", "/tmp",
            "--tmpfs", "/var/tmp",
            "--tmpfs", "/home",
            "--tmpfs", "/root",
            "--tmpfs", "/mnt",
            "--tmpfs", "/media",
            "--die-with-parent",
            "--new-session",
            "bash", "-c", command,
        };

        _logger.LogDebug("Sandboxed command with bwrap: {Command}", command);
        return ("bwrap", args);
    }

    private static bool CheckBwrapAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "bwrap",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--version");
            using var process = Process.Start(psi);
            if (process == null) return false;
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
