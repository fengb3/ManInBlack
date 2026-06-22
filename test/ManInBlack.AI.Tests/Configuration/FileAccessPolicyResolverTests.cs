using ManInBlack.AI.Configuration;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.Options;
using Xunit;

namespace ManInBlack.AI.Tests.Configuration;

public class FileAccessPolicyResolverTests
{
    [Fact]
    public void Resolve_无配置_仅workspace与temp可读()
    {
        var ws = "/data/ws/42";
        var resolver = new FileAccessPolicyResolver(
            new FakeUserWorkspace("42", ws),
            Options.Create(new ManInBlackSettings()));

        var policy = resolver.Resolve();

        // 规范化会去掉尾部分隔符;跨平台用同一 Canonicalize 比较以保持断言精确。
        Assert.Equal(FileAccessPolicy.Canonicalize(ws), policy.Workspace);
        Assert.True(policy.IsReadable($"{ws}/file"));
        Assert.False(policy.IsReadable("/root/.man-in-black/workspaces/other/x"));
        Assert.Empty(policy.ReadableRoots);
    }

    [Fact]
    public void Resolve_有ReadableRoots_纳入只读根()
    {
        var settings = new ManInBlackSettings
        {
            Storage = new StorageSettings
            {
                FileIsolation = new FileIsolationSettings { ReadableRoots = ["/opt/data", "/srv/shared"] }
            }
        };
        var resolver = new FileAccessPolicyResolver(
            new FakeUserWorkspace("42", "/data/ws/42"),
            Options.Create(settings));

        var policy = resolver.Resolve();

        Assert.Equal(2, policy.ReadableRoots.Count);
        Assert.True(policy.IsReadable("/opt/data/sub/x"));
        Assert.False(policy.IsWritable("/opt/data/sub/x")); // 只读根不可写
    }

    [Fact]
    public void Resolve_只读根规范化()
    {
        var settings = new ManInBlackSettings
        {
            Storage = new StorageSettings
            {
                FileIsolation = new FileIsolationSettings { ReadableRoots = ["/opt/data/"] }
            }
        };
        var resolver = new FileAccessPolicyResolver(
            new FakeUserWorkspace("42", "/data/ws/42"),
            Options.Create(settings));

        var policy = resolver.Resolve();

        Assert.Equal(FileAccessPolicy.Canonicalize("/opt/data"), policy.ReadableRoots[0]); // 去尾分隔符
    }
}
