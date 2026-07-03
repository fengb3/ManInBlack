using FeishuAdaptor.EventHandlers;
using FeishuNetSdk.Core;
using FeishuNetSdk.Im.Events;
using Xunit;

namespace FeishuAdaptor.Tests;

/// <summary>
/// 验证 AgentLauncher.ResolveMentions:把文本里的 @_user_N 占位符
/// 内联替换为被@者的可读信息(名字 + 全部可获取的标识字段,只输出非空)。
/// </summary>
public class ResolveMentionsTests
{
    private static ImMessageReceiveV1EventBodyDto.EventMessage.MentionEvent Mention(
        string key,
        string? name = "某用户",
        string? openId = null,
        string? userId = null,
        string? unionId = null) =>
        new()
        {
            Key = key,
            Name = name!,
            Id = new UserIdSuffix { OpenId = openId!, UserId = userId!, UnionId = unionId! }
        };

    [Fact]
    public void 单个提及_替换为名字加全部标识()
    {
        var mentions = new[]
        {
            Mention("@_user_1", "张三", "ou_zhang", "zhangsan", "on_zhang")
        };

        var result = AgentLauncher.ResolveMentions("@_user_1 你好", mentions);

        Assert.Equal("@张三(open_id:ou_zhang, user_id:zhangsan, union_id:on_zhang) 你好", result);
    }

    [Fact]
    public void 多个提及_各自替换()
    {
        var mentions = new[]
        {
            Mention("@_user_1", "张三", "ou_zhang", "zhangsan", "on_zhang"),
            Mention("@_user_2", "李四", "ou_li", "lisi", "on_li")
        };

        var result = AgentLauncher.ResolveMentions("@_user_1 把报告发给@_user_2", mentions);

        Assert.Equal(
            "@张三(open_id:ou_zhang, user_id:zhangsan, union_id:on_zhang) 把报告发给@李四(open_id:ou_li, user_id:lisi, union_id:on_li)",
            result);
    }

    [Fact]
    public void 外部用户_缺user_id_只输出存在的字段()
    {
        var mentions = new[]
        {
            Mention("@_user_1", "李四", openId: "ou_li", unionId: "on_li")
        };

        var result = AgentLauncher.ResolveMentions("@_user_1 hi", mentions);

        Assert.Equal("@李四(open_id:ou_li, union_id:on_li) hi", result);
    }

    [Fact]
    public void 所有人_openid为all_只输出名字()
    {
        var mentions = new[] { Mention("@_user_1", "所有人", openId: "all") };

        var result = AgentLauncher.ResolveMentions("@_user_1 大家注意", mentions);

        Assert.Equal("@所有人 大家注意", result);
    }

    [Fact]
    public void mentions为null_原样返回()
    {
        var result = AgentLauncher.ResolveMentions("你好", null);
        Assert.Equal("你好", result);
    }

    [Fact]
    public void 空集合_原样返回()
    {
        var result = AgentLauncher.ResolveMentions(
            "你好",
            Array.Empty<ImMessageReceiveV1EventBodyDto.EventMessage.MentionEvent>());
        Assert.Equal("你好", result);
    }

    [Fact]
    public void 占位符无对应mention_保持原样()
    {
        var mentions = new[]
        {
            Mention("@_user_1", "张三", "ou_zhang", "zhangsan", "on_zhang")
        };

        var result = AgentLauncher.ResolveMentions("@_user_1 你好 @_user_2", mentions);

        Assert.Equal(
            "@张三(open_id:ou_zhang, user_id:zhangsan, union_id:on_zhang) 你好 @_user_2",
            result);
    }

    [Fact]
    public void 名字缺失_回退未知用户()
    {
        var mentions = new[] { Mention("@_user_1", name: null, openId: "ou_x") };

        var result = AgentLauncher.ResolveMentions("@_user_1 hi", mentions);

        Assert.Equal("@未知用户(open_id:ou_x) hi", result);
    }

    [Fact]
    public void 多于十个提及_user_1不误伤user_10()
    {
        // @_user_1 是 @_user_10 的子串,必须先替换长的,否则 @_user_10 被破坏成 <user1 的串>0
        var mentions = new[]
        {
            Mention("@_user_1", "甲", "ou_1"),
            Mention("@_user_10", "乙", "ou_10")
        };

        var result = AgentLauncher.ResolveMentions("at@_user_10 and @_user_1", mentions);

        Assert.Equal("at@乙(open_id:ou_10) and @甲(open_id:ou_1)", result);
    }
}
