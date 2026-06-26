using ManInBlack.Dashboard.Data;
using Microsoft.Extensions.AI;
using Xunit;

namespace Dashboard.Tests;

public class ChatMessageRendererTests
{
    [Fact]
    public void Render_Text_MapsTextBlock()
    {
        var msg = new ChatMessage(ChatRole.User, "hello");
        var view = ChatMessageRenderer.Render(msg);
        Assert.Equal("user", view.Role);
        var b = Assert.Single(view.Blocks);
        Assert.Equal(MessageBlockKind.Text, b.Kind);
        Assert.Equal("hello", b.Text);
    }

    [Fact]
    public void Render_FunctionCall_MapsToolCallBlock()
    {
        var msg = new ChatMessage(ChatRole.Assistant, []);
        msg.Contents.Add(new FunctionCallContent("call_1", "read_file",
            new Dictionary<string, object?> { ["path"] = "/a" }));
        var view = ChatMessageRenderer.Render(msg);
        var b = Assert.Single(view.Blocks);
        Assert.Equal(MessageBlockKind.ToolCall, b.Kind);
        Assert.Equal("read_file", b.ToolName);
        Assert.Contains("path", b.ArgumentsJson!);
    }

    [Fact]
    public void Render_FunctionResult_MapsToolResultBlock()
    {
        var msg = new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_1", "ok-text")]);
        var view = ChatMessageRenderer.Render(msg);
        var b = Assert.Single(view.Blocks);
        Assert.Equal(MessageBlockKind.ToolResult, b.Kind);
        Assert.Equal("ok-text", b.ResultJson);
    }

    [Fact]
    public void Render_UnknownContent_MapsUnknownBlock()
    {
        var msg = new ChatMessage(ChatRole.System, [new OtherContent()]);
        var view = ChatMessageRenderer.Render(msg);
        var b = Assert.Single(view.Blocks);
        Assert.Equal(MessageBlockKind.Unknown, b.Kind);
        Assert.NotNull(b.RawJson);
    }

    sealed class OtherContent : AIContent { }
}
