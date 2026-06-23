using ManInBlack.AI.Configuration;
using Xunit;

namespace ManInBlack.AI.Tests.Configuration;

public class FileAccessPolicyTests
{
    private readonly string _ws = "/data/ws";

    private FileAccessPolicy Policy(params string[] roots) => new()
    {
        Workspace = _ws,
        ReadableRoots = roots
    };

    [Fact]
    public void IsReadable_workspace内_true()
        => Assert.True(Policy().IsReadable("/data/ws/file.txt"));

    [Fact]
    public void IsReadable_workspace根本身_true()
        => Assert.True(Policy().IsReadable("/data/ws"));

    [Fact]
    public void IsReadable_配置只读根内_true()
        => Assert.True(Policy("/opt/data").IsReadable("/opt/data/x"));

    [Fact]
    public void IsReadable_列表外_false()
    {
        Assert.False(Policy().IsReadable("/root/.man-in-black/workspaces/other/secret"));
        Assert.False(Policy().IsReadable("/root/.man-in-black/settings.json"));
        Assert.False(Policy().IsReadable("/root/.man-in-black/sessions/abc"));
        Assert.False(Policy().IsReadable("/etc/passwd"));
        Assert.False(Policy().IsReadable("/tmp/scratch")); // 系统临时目录不在允许列表
    }

    [Fact]
    public void IsReadable_父目录穿越_false()
        => Assert.False(Policy().IsReadable("/data/ws/../tmp_evil/file"));

    [Fact]
    public void IsReadable_前缀伪匹配_false()
        => Assert.False(Policy().IsReadable("/data/ws-evil/file"));

    [Fact]
    public void IsWritable_workspace内_true()
        => Assert.True(Policy().IsWritable("/data/ws/a.txt"));

    [Fact]
    public void IsWritable_workspace根本身_false()
        => Assert.False(Policy().IsWritable("/data/ws"));

    [Fact]
    public void IsWritable_只读根内_false()
        => Assert.False(Policy("/opt/data").IsWritable("/opt/data/x"));

    [Fact]
    public void IsWritable_列表外_false()
        => Assert.False(Policy().IsWritable("/etc/passwd"));
}
