using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ManInBlack.Dashboard.Data;

/// <summary>把 ChatMessage 内容块映射成前端友好的 MessageView(纯函数,无 DB)。</summary>
public static class ChatMessageRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static MessageView Render(ChatMessage message)
    {
        var blocks = new List<MessageBlock>(message.Contents.Count);
        foreach (var content in message.Contents)
        {
            blocks.Add(content switch
            {
                TextContent t => new MessageBlock { Kind = MessageBlockKind.Text, Text = t.Text },
                FunctionCallContent fc => new MessageBlock
                {
                    Kind = MessageBlockKind.ToolCall,
                    ToolName = fc.Name,
                    ArgumentsJson = JsonSerializer.Serialize(fc.Arguments, JsonOptions),
                },
                FunctionResultContent fr => new MessageBlock
                {
                    Kind = MessageBlockKind.ToolResult,
                    ResultJson = fr.Result switch
                    {
                        null => "null",
                        string s => s,
                        _ => SafeSerialize(fr.Result),
                    },
                },
                TextReasoningContent r => new MessageBlock { Kind = MessageBlockKind.Reasoning, Text = r.Text },
                _ => new MessageBlock { Kind = MessageBlockKind.Unknown, RawJson = SafeSerialize(content) },
            });
        }
        return new MessageView { Role = message.Role.Value, Blocks = blocks };
    }

    /// <summary>序列化任意对象;若类型不受 JSON 多态契约支持(如未注册的 AIContent 子类),回退为类型名 JSON,保证不抛。</summary>
    private static string SafeSerialize(object obj)
    {
        try { return JsonSerializer.Serialize(obj, JsonOptions); }
        catch (NotSupportedException) { return $"{{\"$type\":\"{obj.GetType().Name}\"}}"; }
    }
}
