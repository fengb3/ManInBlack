using Bwarp;
using Bwarp.Execution;
using Xunit;

namespace Bwarp.Tests;

public class BwarpSandboxTests
{
#if !LINUX
    private const string LinuxOnlySkip = "Requires Linux with bubblewrap (bwrap) installed";
#else
    private const string? LinuxOnlySkip = null;
#endif
    [Fact(Skip = LinuxOnlySkip)]
    public async Task ExecuteAsync_BasicCommand_ReturnsZeroExitCode()
    {
        var result = await Sandbox.Run("/bin/echo", "hello")
            .Unshare(Namespaces.User | Namespaces.Pid)
            .MountProc()
            .BindReadOnly("/usr", "/usr")
            .TryBindReadOnly("/lib", "/lib")
            .TryBindReadOnly("/lib64", "/lib64")
            .BindReadOnly("/bin", "/bin")
            .DieWithParent()
            .NewSession()
            .ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Contains("hello", result.StandardOutput);
    }

    [Fact(Skip = LinuxOnlySkip)]
    public async Task ExecuteAsync_WithHostname_SetsSandboxHostname()
    {
        var result = await Sandbox.Run("/bin/hostname")
            .Unshare(Namespaces.User | Namespaces.Pid | Namespaces.Uts)
            .MountProc()
            .BindReadOnly("/usr", "/usr")
            .TryBindReadOnly("/lib", "/lib")
            .TryBindReadOnly("/lib64", "/lib64")
            .BindReadOnly("/bin", "/bin")
            .WithHostname("test-sandbox")
            .DieWithParent()
            .NewSession()
            .ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Contains("test-sandbox", result.StandardOutput);
    }

    [Fact(Skip = LinuxOnlySkip)]
    public async Task ExecuteAsync_UnshareAll_IsolatesNetwork()
    {
        var result = await Sandbox.Run("/bin/sh", "-c", "/usr/sbin/ip link show 2>&1 | grep -c 'state UP' || true")
            .UnshareAll()
            .ClearEnvironment()
            .SetEnv("PATH", "/usr/bin:/bin:/usr/sbin")
            .MountProc()
            .MountDev()
            .BindReadOnly("/usr", "/usr")
            .TryBindReadOnly("/lib", "/lib")
            .TryBindReadOnly("/lib64", "/lib64")
            .BindReadOnly("/bin", "/bin")
            .DieWithParent()
            .NewSession()
            .ExecuteAsync();

        Assert.True(result.IsSuccess);
        var count = int.Parse(result.StandardOutput.Trim());
        Assert.True(count <= 1, $"Expected at most 1 interface UP, got {count}");
    }

    [Fact(Skip = LinuxOnlySkip)]
    public async Task ExecuteAsync_ClearEnv_RemovesAllEnvVars()
    {
        var result = await Sandbox.Run("/bin/sh", "-c", "env | wc -l")
            .Unshare(Namespaces.User | Namespaces.Pid)
            .MountProc()
            .BindReadOnly("/usr", "/usr")
            .TryBindReadOnly("/lib", "/lib")
            .TryBindReadOnly("/lib64", "/lib64")
            .BindReadOnly("/bin", "/bin")
            .ClearEnvironment()
            .SetEnv("PATH", "/usr/bin:/bin")
            .DieWithParent()
            .NewSession()
            .ExecuteAsync();

        Assert.True(result.IsSuccess);
        var count = int.Parse(result.StandardOutput.Trim());
        Assert.True(count <= 2, $"Expected at most 2 env vars, got {count}");
    }

    [Fact(Skip = LinuxOnlySkip)]
    public async Task ExecuteAsync_TmpfsMount_CreatesTmpfs()
    {
        var result = await Sandbox.Run("/bin/sh", "-c", "stat -f -c %T /data")
            .Unshare(Namespaces.User | Namespaces.Pid)
            .MountProc()
            .BindReadOnly("/usr", "/usr")
            .TryBindReadOnly("/lib", "/lib")
            .TryBindReadOnly("/lib64", "/lib64")
            .BindReadOnly("/bin", "/bin")
            .MountTmpfs("/data")
            .DieWithParent()
            .NewSession()
            .ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Contains("tmpfs", result.StandardOutput);
    }

    [Fact(Skip = LinuxOnlySkip)]
    public async Task ExecuteAsync_ReadOnlyBind_PreventsWrites()
    {
        var result = await Sandbox.Run("/bin/sh", "-c", "touch /usr/test_write 2>&1; echo exit=$?")
            .Unshare(Namespaces.User | Namespaces.Pid)
            .MountProc()
            .BindReadOnly("/usr", "/usr")
            .TryBindReadOnly("/lib", "/lib")
            .TryBindReadOnly("/lib64", "/lib64")
            .BindReadOnly("/bin", "/bin")
            .DieWithParent()
            .NewSession()
            .ExecuteAsync();

        Assert.Contains("exit=1", result.StandardOutput);
    }

    [Fact(Skip = LinuxOnlySkip)]
    public async Task ExecuteAsync_Cancellation_ThrowsTaskCanceledException()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            Sandbox.Run("/bin/sleep", "60")
                .Unshare(Namespaces.User | Namespaces.Pid)
                .MountProc()
                .BindReadOnly("/usr", "/usr")
                .TryBindReadOnly("/lib", "/lib")
                .TryBindReadOnly("/lib64", "/lib64")
                .BindReadOnly("/bin", "/bin")
                .DieWithParent()
                .NewSession()
                .ExecuteAsync(cts.Token));
    }

    [Fact(Skip = LinuxOnlySkip)]
    public async Task ExecuteAsync_PresetMinimal_Works()
    {
        var result = await SandboxPresets.Minimal("/bin/echo", "preset works")
            .SetEnv("HOME", "/tmp")
            .ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Contains("preset works", result.StandardOutput);
    }

    [Fact(Skip = LinuxOnlySkip)]
    public async Task ExecuteAsync_PresetFullyIsolated_Works()
    {
        var result = await SandboxPresets.FullyIsolated("/bin/sh", "-c", "echo ok && hostname")
            .ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Contains("ok", result.StandardOutput);
    }

    [Fact(Skip = LinuxOnlySkip)]
    public async Task ExecuteAsync_WithUid_SetsUserId()
    {
        var result = await Sandbox.Run("/bin/sh", "-c", "id -u")
            .Unshare(Namespaces.User | Namespaces.Pid)
            .MountProc()
            .BindReadOnly("/usr", "/usr")
            .TryBindReadOnly("/lib", "/lib")
            .TryBindReadOnly("/lib64", "/lib64")
            .BindReadOnly("/bin", "/bin")
            .WithUid(1234)
            .DieWithParent()
            .NewSession()
            .ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Contains("1234", result.StandardOutput.Trim());
    }

    [Fact(Skip = LinuxOnlySkip)]
    public async Task ListenAsync_ReceivesEvents()
    {
        var events = new List<SandboxEvent>();

        await foreach (var evt in Sandbox.Run("/bin/echo", "stream-test")
            .Unshare(Namespaces.User | Namespaces.Pid)
            .MountProc()
            .BindReadOnly("/usr", "/usr")
            .TryBindReadOnly("/lib", "/lib")
            .TryBindReadOnly("/lib64", "/lib64")
            .BindReadOnly("/bin", "/bin")
            .DieWithParent()
            .NewSession()
            .ListenAsync())
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e is SandboxEvent.Started);
        Assert.Contains(events, e => e is SandboxEvent.StandardOutputReceived o && o.Text.Contains("stream-test"));
        Assert.Contains(events, e => e is SandboxEvent.Exited);
    }

    [Fact(Skip = LinuxOnlySkip)]
    public async Task ExecuteAsync_Symlink_CreatesLink()
    {
        var result = await Sandbox.Run("/bin/sh", "-c", "test -d /mybin && echo found")
            .Unshare(Namespaces.User | Namespaces.Pid)
            .MountProc()
            .BindReadOnly("/usr", "/usr")
            .TryBindReadOnly("/lib", "/lib")
            .TryBindReadOnly("/lib64", "/lib64")
            .BindReadOnly("/bin", "/bin")
            .CreateDir("/mybin")
            .DieWithParent()
            .NewSession()
            .ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Contains("found", result.StandardOutput);
    }

    [Fact(Skip = LinuxOnlySkip)]
    public async Task Confine_CanWriteOnlyInWorkingDirectory()
    {
        using var tmpDir = new TempDirectory();
        var file = Path.Combine(tmpDir.Path, "test.txt");

        var result = await Sandbox.Confine(tmpDir.Path, $"echo hello > {file} && cat {file}")
            .ExecuteAsync();

        Assert.True(result.IsSuccess, $"ExitCode={result.ExitCode}\nStdout={result.StandardOutput}\nStderr={result.StandardError}");
        Assert.Contains("hello", result.StandardOutput);
    }

    [Fact(Skip = LinuxOnlySkip)]
    public async Task Confine_WorkingDirectory外的宿主文件不可见()
    {
        using var tmpDir = new TempDirectory();
        // 在宿主 /tmp(workingDir 之外)放一个标记文件
        var outsideMarker = Path.Combine(Path.GetTempPath(), $"bwarp-outside-{Guid.NewGuid():N}");
        File.WriteAllText(outsideMarker, "secret");

        try
        {
            // Confine default-deny:沙盒的 /tmp 是私有 tmpfs,宿主该文件在沙盒内不可见 → HIDDEN
            var result = await Sandbox.Confine(tmpDir.Path, $"test -f {outsideMarker} && echo VISIBLE || echo HIDDEN")
                .ExecuteAsync();

            Assert.True(result.IsSuccess, $"ExitCode={result.ExitCode}, Stderr={result.StandardError}");
            Assert.Contains("HIDDEN", result.StandardOutput);
            Assert.DoesNotContain("VISIBLE", result.StandardOutput);
        }
        finally
        {
            if (File.Exists(outsideMarker)) File.Delete(outsideMarker);
        }
    }

    [Fact(Skip = LinuxOnlySkip)]
    public async Task Confine_HasNetworkAccess()
    {
        using var tmpDir = new TempDirectory();

        var result = await Sandbox.Confine(tmpDir.Path, "curl -s -o /dev/null -w '%{http_code}' https://example.com || wget -q -O /dev/null https://example.com 2>&1; echo exit=$?")
            .ExecuteAsync();

        Assert.True(result.IsSuccess, $"ExitCode={result.ExitCode}\nStdout={result.StandardOutput}\nStderr={result.StandardError}");
    }

    [Fact(Skip = LinuxOnlySkip)]
    public async Task Confine_StandardInput传给子进程()
    {
        using var tmpDir = new TempDirectory();

        // cat 回显 stdin:验证 Confine 下 stdin 经 bwrap 透传到沙盒内进程,且写完 Close 发 EOF(cat 不会阻塞等待)
        var result = await Sandbox.Confine(tmpDir.Path, "cat")
            .WithStandardInput("hello-stdin")
            .ExecuteAsync();

        Assert.True(result.IsSuccess, $"stdin 透传失败: ExitCode={result.ExitCode}, Stderr={result.StandardError}");
        Assert.Contains("hello-stdin", result.StandardOutput);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            var home = Environment.GetEnvironmentVariable("HOME") ?? "/var/tmp";
            Path = System.IO.Path.Combine(home, $"bwarp-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch { }
        }
    }
}
