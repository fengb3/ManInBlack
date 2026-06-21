using FeishuAdaptor;
using ManInBlack.AI;
using ManInBlack.AI.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FeishuAdaptor.Tests;

/// <summary>
/// 验证 AddFeishu 扩展方法能正确注册飞书配置。
/// </summary>
public class FeishuBuilderExtensionsTests
{
    [Fact]
    public void AddFeishu_ShouldRegisterFeishuSettings()
    {
        var services = new ServiceCollection();
        services.AddManInBlack().AddFeishu(f => f.AppId = "cli_xxx");
        var feishu = services.BuildServiceProvider().GetRequiredService<IOptions<FeishuSettings>>().Value;

        Assert.Equal("cli_xxx", feishu.AppId);
    }

    [Fact]
    public void AddFeishu_ShouldReturnSameBuilder_ForFluentChaining()
    {
        var services = new ServiceCollection();
        var builder = services.AddManInBlack();
        var result = builder.AddFeishu(_ => { });

        Assert.Same(builder, result);
    }
}
