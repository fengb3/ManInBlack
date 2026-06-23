using System.Text.Json;
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
    public string? LastStdin { get; private set; }
    public ShellResult Result { get; set; } = new();

    public ShellResult Execute(string command, string workingDirectory, int timeoutMs, string? stdin = null)
    {
        LastCommand = command;
        LastWorkingDirectory = workingDirectory;
        LastStdin = stdin;
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
    public async Task 上下文经stdin传入_且command不含文件路径()
    {
        // {RootPath}/hooks 必须存在:全局钩子的 workingDir 落在这里
        var rootDir = Path.Combine(Path.GetTempPath(), $"mib_hookroot_{Guid.NewGuid():N}");
        var hooksDir = Path.Combine(rootDir, "hooks");
        Directory.CreateDirectory(hooksDir);
        try
        {
            const string script = "python check.py";
            var shell = new FakeShellExecutor();
            var executor = new HookExecutor(
                Options.Create(SettingsWithGlobalHook(script)),
                Options.Create(new AgentStorageOptions { RootPath = rootDir }),
                shell,
                new FakeUserWorkspace("u", Path.Combine(Path.GetTempPath(), $"mib_ws_{Guid.NewGuid():N}")),
                NullLogger<HookExecutor>.Instance);

            await executor.ExecuteAsync(
                HookPoint.BeforeToolExecute,
                new HookContext { ToolName = "Read" },
                default);

            // 全局钩子工作目录 = {RootPath}/hooks
            Assert.Equal(hooksDir, shell.LastWorkingDirectory);

            // context 经 stdin 传入(可反序列化回 HookContext,含 ToolName=Read)
            Assert.NotNull(shell.LastStdin);
            var ctx = JsonSerializer.Deserialize<HookContext>(
                shell.LastStdin!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(ctx);
            Assert.Equal("Read", ctx!.ToolName);

            // command 是裸脚本命令,不再拼接临时文件路径
            Assert.Equal(script, shell.LastCommand);
            Assert.DoesNotContain("mib-hook-ctx", shell.LastCommand!);
            Assert.DoesNotContain(hooksDir, shell.LastCommand!);

            // 不再写任何临时文件
            Assert.Empty(Directory.EnumerateFileSystemEntries(hooksDir));
        }
        finally
        {
            if (Directory.Exists(rootDir)) Directory.Delete(rootDir, true);
        }
    }
}
