using System.IO;
using FeishuAdaptor.EventHandlers;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FeishuAdaptor.Tests;

/// <summary>
/// 验证飞书文件上传落到「发送者所属」的工作空间。
/// 修复前:HandleMessage 在 Agent 运行前的独立 scope 解析 IUserWorkspace,
/// 此时 AgentContext 未被填充,所有用户的文件都会落到「空字符串用户」的工作空间。
/// </summary>
public class AgentLauncherFileWorkspaceTests
{
    private static ServiceCollection BuildServices(string tempRoot)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<AgentStorageOptions>>(
            Options.Create(new AgentStorageOptions { RootPath = tempRoot }));
        services.AddScoped<AgentContext>();
        services.AddSingleton<IUserStorage, FakeUserStorage>();
        services.AddScoped<IUserWorkspace>(sp => new FileUserWorkspace(
            sp.GetRequiredService<IOptions<AgentStorageOptions>>(),
            sp.GetRequiredService<AgentContext>(),
            sp.GetRequiredService<IUserStorage>()));
        return services;
    }

    [Fact]
    public void ResolveWorkspaceDirectory_按真实userId解析_落到该用户空间而非空字符串用户()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"mib_ws_test_{System.Guid.NewGuid():N}");
        try
        {
            using var scope = BuildServices(tempRoot).BuildServiceProvider().CreateScope();

            var dir = AgentLauncher.ResolveWorkspaceDirectory(scope.ServiceProvider, "feishu-user-1");

            // 应落到发送者(feishu-user-1 -> SelfHostUserId=1)的空间
            Assert.EndsWith(Path.Combine("workspaces", "1"), dir);
            // 关键:不能落到空字符串用户(workspace 3,即 bug 现象)
            Assert.DoesNotContain(Path.Combine("workspaces", "3"), dir);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void ResolveWorkspaceDirectory_不同用户得到不同工作空间()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"mib_ws_test_{System.Guid.NewGuid():N}");
        var services = BuildServices(tempRoot);
        try
        {
            using var scope1 = services.BuildServiceProvider().CreateScope();
            using var scope2 = services.BuildServiceProvider().CreateScope();

            var dir1 = AgentLauncher.ResolveWorkspaceDirectory(scope1.ServiceProvider, "feishu-user-1");
            var dir2 = AgentLauncher.ResolveWorkspaceDirectory(scope2.ServiceProvider, "feishu-user-2");

            Assert.EndsWith(Path.Combine("workspaces", "1"), dir1);
            Assert.EndsWith(Path.Combine("workspaces", "2"), dir2);
            Assert.NotEqual(dir1, dir2);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    /// <summary>
    /// 内存版 IUserStorage,模拟 SQLite 自增主键:不同 userId 对应不同 SelfHostUserId。
    /// 其中空字符串用户映射到 "3",复现历史 bug 的归宿。
    /// </summary>
    private sealed class FakeUserStorage : IUserStorage
    {
        public Task<UserEntry> GetOrCreateUser(string userId)
        {
            var selfHostId = userId switch
            {
                "" => "3",
                "feishu-user-1" => "1",
                "feishu-user-2" => "2",
                _ => "99",
            };
            return Task.FromResult(new UserEntry { UserId = userId, SelfHostUserId = selfHostId });
        }

        public Task SaveUserAsync(UserEntry userEntry) => Task.CompletedTask;

        public Task<string> CreateNewSessionIdAsync(string userId) => Task.FromResult($"{userId}_1");
    }
}
