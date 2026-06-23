using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Hooks;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Services;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ManInBlack.AI.Tests.Services;

/// <summary>
/// 捕获命令与工作目录的 IShellExecutor 假实现,返回预设 stdout。
/// </summary>
public class FakeShellExecutor : IShellExecutor
{
    public string? LastCommand { get; private set; }
    public string? LastWorkingDirectory { get; private set; }
    public ShellResult Result { get; set; } = new();

    public ShellResult Execute(string command, string workingDirectory, int timeoutMs)
    {
        LastCommand = command;
        LastWorkingDirectory = workingDirectory;
        return Result;
    }
}

public class HookExecutorTests
{
    private static ManInBlackSettings SettingsWithGlobalHook(string script) => new()
    {
        Hooks = [new HookSettings { Name = "h", HookPoint = "BeforeToolExecute", Script = script }]
    };

    [Fact]
    public async Task 上下文临时文件落在workingDir而非系统tmp_且执行后清理()
    {
        // {RootPath}/hooks 必须存在:全局钩子的 workingDir 与上下文临时文件都落在这里
        var rootDir = Path.Combine(Path.GetTempPath(), $"mib_hookroot_{Guid.NewGuid():N}");
        var hooksDir = Path.Combine(rootDir, "hooks");
        Directory.CreateDirectory(hooksDir);
        try
        {
            var shell = new FakeShellExecutor();
            var executor = new HookExecutor(
                Options.Create(SettingsWithGlobalHook("python check.py")),
                Options.Create(new AgentStorageOptions { RootPath = rootDir }),
                shell,
                new FakeUserWorkspace("u", Path.Combine(Path.GetTempPath(), $"mib_ws_{Guid.NewGuid():N}")),
                NullLogger<HookExecutor>.Instance);

            await executor.ExecuteAsync(
                HookPoint.BeforeToolExecute,
                new HookContext { ToolName = "Read" },
                default);

            // 全局钩子工作目录 = {RootPath}/hooks
            Assert.NotNull(shell.LastCommand);
            Assert.Equal(hooksDir, shell.LastWorkingDirectory);

            // 临时文件路径应在 workingDir(hooksDir)之下,而非系统 /tmp 根
            Assert.Contains(hooksDir, shell.LastCommand!);

            // 执行后临时文件应被清理(hooksDir 回到空)
            Assert.Empty(Directory.EnumerateFileSystemEntries(hooksDir));
        }
        finally
        {
            if (Directory.Exists(rootDir)) Directory.Delete(rootDir, true);
        }
    }
}
