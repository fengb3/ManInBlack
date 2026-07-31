using FeishuAdaptor.FeishuCard;
using FeishuAdaptor.FeishuCard.Cards;
using FeishuAdaptor.Tools;
using Xunit;

namespace FeishuAdaptor.Tests;

public class AskUserCardBuilderTests
{
    private static readonly AskUserOption[] Opts =
    {
        new("是") { Value = "yes" },
        new("否") { Value = "no" },
    };

    [Fact]
    public void Single_Select_Builds_Buttons_With_RequestId_And_Option()
    {
        var card = AskUserCardBuilder.Build("继续吗？", Opts, multiSelect: false, "rid123");
        var json = card.ToJson();

        Assert.Contains("\"requestId\":\"rid123\"", json);
        Assert.Contains("\"option\":\"yes\"", json);
        Assert.Contains("\"option\":\"no\"", json);
        Assert.Contains("\"tag\":\"button\"", json);
        Assert.DoesNotContain("\"tag\":\"form\"", json);
    }

    [Fact]
    public void Multi_Select_Builds_Form_With_MultiSelect_And_Submit()
    {
        var card = AskUserCardBuilder.Build("选哪些？", Opts, multiSelect: true, "rid456");
        var json = card.ToJson();

        Assert.Contains("\"tag\":\"form\"", json);
        Assert.Contains("\"tag\":\"multi_select_static\"", json);
        Assert.Contains("\"name\":\"opts\"", json);
        Assert.Contains("\"submit\"", json);
        Assert.Contains("\"requestId\":\"rid456\"", json);
        Assert.Contains("\"yes\"", json);
        Assert.Contains("\"no\"", json);
    }

    [Fact]
    public void Body_Contains_Question()
    {
        var card = AskUserCardBuilder.Build("标题问题", Opts, false, "r");
        var json = card.ToJson();
        // 卡片序列化会把非 ASCII 中文转成 \uXXXX 转义，先把转义还原成字符再断言可读文案。
        var decoded = System.Text.RegularExpressions.Regex.Replace(
            json, @"\\u([0-9A-Fa-f]{4})",
            m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        Assert.Contains("标题问题", decoded);
    }
}
