using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.Options;
using Xunit;

namespace ManInBlack.AI.Tests.Configuration;

public class FileAccessPolicyResolverTests
{
    private static IOptions<AgentStorageOptions> Storage(string rootPath) =>
        Options.Create(new AgentStorageOptions { RootPath = rootPath });

    [Fact]
    public void Resolve_无配置_仅workspace与temp与skills可读()
    {
        var ws = "/data/ws/42";
        var resolver = new FileAccessPolicyResolver(
            new FakeUserWorkspace("42", ws),
            Options.Create(new ManInBlackSettings()),
            Storage("/data/root"));

        var policy = resolver.Resolve();

        // 规范化会去掉尾部分隔符;跨平台用同一 Canonicalize 比较以保持断言精确。
        Assert.Equal(FileAccessPolicy.Canonicalize(ws), policy.Workspace);
        Assert.True(policy.IsReadable($"{ws}/file"));
        Assert.True(policy.IsReadable("/data/root/skills/skill-creator/SKILL.md")); // 系统只读根
        Assert.False(policy.IsReadable("/root/.man-in-black/workspaces/other/x"));
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
            Options.Create(settings),
            Storage("/data/root"));

        var policy = resolver.Resolve();

        // 配置 2 个 + 系统根 {RootPath}/skills 共 3 个
        Assert.Equal(3, policy.ReadableRoots.Count);
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
            Options.Create(settings),
            Storage("/data/root"));

        var policy = resolver.Resolve();

        // 配置根排在最前,去尾分隔符
        Assert.Equal(FileAccessPolicy.Canonicalize("/opt/data"), policy.ReadableRoots[0]);
    }

    [Fact]
    public void Resolve_自动放行skills目录且只读()
    {
        var resolver = new FileAccessPolicyResolver(
            new FakeUserWorkspace("42", "/data/ws/42"),
            Options.Create(new ManInBlackSettings()),
            Storage("/data/root"));

        var policy = resolver.Resolve();

        // {RootPath}/skills 始终作为系统只读根(随 RootPath 走,跨平台)
        Assert.Contains(FileAccessPolicy.Canonicalize("/data/root/skills"), policy.ReadableRoots);
        Assert.True(policy.IsReadable("/data/root/skills/any-skill/SKILL.md"));
        Assert.False(policy.IsWritable("/data/root/skills/any-skill/SKILL.md")); // 只读
    }
}
