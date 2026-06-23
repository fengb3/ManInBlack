using Bwarp;
using Bwarp.Mounts;
using Xunit;

namespace ManInBlack.AI.Tests.Services;

public class BwarpShellExecutorMountTests
{
    private static List<MountEntry> MountsOf(string ws, string command, string[]? roots = null) =>
        Sandbox.Confine(ws, command, roots ?? []).Build().Mounts.ToList();

    [Fact]
    public void 不绑定整个根目录()
    {
        var mounts = MountsOf("/data/ws/42", "ls");
        // 不允许 ro-bind "/ /"(那会泄露同级 workspace 与密钥)
        Assert.DoesNotContain(mounts, m => m is BindMount b && b.Source == "/");
        Assert.DoesNotContain(mounts, m => m is BindMount b && b.Destination == "/");
    }

    [Fact]
    public void 含精选系统只读路径()
    {
        var mounts = MountsOf("/data/ws/42", "ls");
        Assert.Contains(mounts, m => m is BindMount b && b.Destination == "/usr" && b.Access == MountAccess.ReadOnly);
        Assert.Contains(mounts, m => m is BindMount b && b.Destination == "/etc" && b.Access == MountAccess.ReadOnly);
        Assert.Contains(mounts, m => m is ProcMount);
        Assert.Contains(mounts, m => m is DevMount);
        Assert.Contains(mounts, m => m is TmpfsMount t && t.Destination == "/tmp");
    }

    [Fact]
    public void workspace可写绑定_且在系统路径之后()
    {
        var mounts = MountsOf("/data/ws/42", "ls");
        var wsIdx = mounts.FindIndex(m => m is BindMount b
            && b.Source == "/data/ws/42" && b.Destination == "/data/ws/42" && b.Access == MountAccess.ReadWrite);
        var usrIdx = mounts.FindIndex(m => m is BindMount b && b.Destination == "/usr");
        Assert.True(wsIdx >= 0, "缺少 workspace 可写绑定");
        Assert.True(usrIdx >= 0 && usrIdx < wsIdx, "workspace 可写绑定必须在系统路径之后");
    }

    [Fact]
    public void 只读根被只读绑定()
    {
        var mounts = MountsOf("/data/ws/42", "ls", ["/opt/data"]);
        Assert.Contains(mounts, m => m is BindMount b
            && b.Source == "/opt/data" && b.Destination == "/opt/data" && b.Access == MountAccess.ReadOnly);
    }

    [Fact]
    public void 同级workspace路径未被挂载()
    {
        var mounts = MountsOf("/data/ws/42", "ls");
        Assert.DoesNotContain(mounts, m => m is BindMount b
            && b.Destination.StartsWith("/data/ws/", StringComparison.Ordinal)
            && b.Destination != "/data/ws/42");
    }

    [Fact]
    public void 注入的环境变量出现在SetEnvVars()
    {
        var env = new Dictionary<string, string>
        {
            ["FEISHU_APP_ID"] = "cli_x",
            ["OPENAI_API_KEY"] = "sk-y",
        };

        var options = Sandbox.Confine("/data/ws/42", "ls", [], env).Build();

        Assert.Equal(2, options.SetEnvVars.Count);
        Assert.Equal("cli_x", options.SetEnvVars["FEISHU_APP_ID"]);
        Assert.Equal("sk-y", options.SetEnvVars["OPENAI_API_KEY"]);
    }
}
