using System.Text.Json;
using ManInBlack.AI.Core;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Xunit;

namespace ManInBlack.AI.Tests;

public class AnthropicCompatibleChatClientTests
{
    private static AnthropicCompatibleChatClient CreateClient(MockHttpMessageHandler handler)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.anthropic.com/")
        };
        return new AnthropicCompatibleChatClient(http, "claude-test");
    }

    #region 非流式

    [Fact]
    public async Task GetResponseAsync_文本响应_返回文本内容()
    {
        var json = """
                   {
                       "content": [{"type": "text", "text": "Hello from Claude!"}],
                       "stop_reason": "end_turn",
                       "usage": { "input_tokens": 10, "output_tokens": 5 }
                   }
                   """;
        var handler = new MockHttpMessageHandler(json);
        var client = CreateClient(handler);

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hi")
        ]);

        var text = response.Messages[0].Contents.OfType<TextContent>().FirstOrDefault();
        Assert.Equal("Hello from Claude!", text?.Text);
    }

    [Fact]
    public async Task GetResponseAsync_ToolUse响应_返回FunctionCallContent()
    {
        var json = """
                   {
                       "content": [
                           {"type": "text", "text": "让我查一下。"},
                           {"type": "tool_use", "id": "toolu_123", "name": "get_weather", "input": {"city": "Beijing"}}
                       ],
                       "stop_reason": "tool_use",
                       "usage": { "input_tokens": 20, "output_tokens": 15 }
                   }
                   """;
        var handler = new MockHttpMessageHandler(json);
        var client = CreateClient(handler);

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "北京天气")
        ]);

        var contents = response.Messages[0].Contents;
        var fcc = contents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(fcc);
        Assert.Equal("get_weather", fcc.Name);
        Assert.Equal("toolu_123", fcc.CallId);
        Assert.Equal("Beijing", fcc.Arguments["city"]?.ToString());
    }

    [Fact]
    public async Task GetResponseAsync_仅ToolUse无文本()
    {
        var json = """
                   {
                       "content": [
                           {"type": "tool_use", "id": "toolu_456", "name": "search", "input": {"query": "test"}}
                       ],
                       "stop_reason": "tool_use",
                       "usage": { "input_tokens": 10, "output_tokens": 8 }
                   }
                   """;
        var handler = new MockHttpMessageHandler(json);
        var client = CreateClient(handler);

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "搜索")
        ]);

        var contents = response.Messages[0].Contents;
        Assert.DoesNotContain(contents, c => c is TextContent);
        Assert.Single(contents.OfType<FunctionCallContent>());
    }

    [Fact]
    public async Task GetResponseAsync_Usage信息_正确提取()
    {
        var json = """
                   {
                       "content": [{"type": "text", "text": "ok"}],
                       "stop_reason": "end_turn",
                       "usage": { "input_tokens": 100, "output_tokens": 50 }
                   }
                   """;
        var handler = new MockHttpMessageHandler(json);
        var client = CreateClient(handler);

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hi")
        ]);

        Assert.NotNull(response.Usage);
        Assert.Equal(100, response.Usage.InputTokenCount);
        Assert.Equal(50, response.Usage.OutputTokenCount);
        Assert.Equal(150, response.Usage.TotalTokenCount);
    }

    [Fact]
    public async Task GetResponseAsync_HTTP错误_抛HttpRequestException()
    {
        var handler = new MockHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));
    }

    [Fact]
    public async Task GetResponseAsync_Usage信息_TokenCount值正确()
    {
        var json = """
                   {
                       "content": [{"type": "text", "text": "ok"}],
                       "stop_reason": "end_turn",
                       "usage": { "input_tokens": 100, "output_tokens": 50 }
                   }
                   """;
        var handler = new MockHttpMessageHandler(json);
        var client = CreateClient(handler);

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hi")
        ]);

        Assert.NotNull(response.Usage);
        Assert.Equal(100, response.Usage.InputTokenCount);
        Assert.Equal(50, response.Usage.OutputTokenCount);
        Assert.Equal(150, response.Usage.TotalTokenCount);
    }

    [Fact]
    public async Task GetResponseAsync_空Content数组_无内容返回()
    {
        var json = """
                   {
                       "content": [],
                       "stop_reason": "end_turn",
                       "usage": { "input_tokens": 0, "output_tokens": 0 }
                   }
                   """;
        var handler = new MockHttpMessageHandler(json);
        var client = CreateClient(handler);

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hi")
        ]);

        Assert.Empty(response.Messages[0].Contents);
    }

    #endregion

    #region 流式

    [Fact]
    public async Task GetStreamingResponseAsync_文本Delta_拼接为完整文本()
    {
        var chunks = new[]
        {
            """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello"}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":" from"}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":" Claude!"}}""",
            """{"type":"content_block_stop","index":0}"""
        };
        var stream = SseResponseBuilder.Build(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        var text = string.Concat(updates.SelectMany(u => u.Contents.OfType<TextContent>().Select(t => t.Text)));
        Assert.Equal("Hello from Claude!", text);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ToolUse三阶段_累积为完整FunctionCall()
    {
        var chunks = new[]
        {
            // 先来一段文本
            """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"查一下"}}""",
            """{"type":"content_block_stop","index":0}""",
            // tool_use 开始
            """{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_abc","name":"get_weather"}}""",
            // tool_use 参数分片
            """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"ci"}}""",
            """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"ty\":\"Shanghai\"}"}}""",
            // tool_use 结束
            """{"type":"content_block_stop","index":1}"""
        };
        var stream = SseResponseBuilder.Build(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        var fcc = updates.SelectMany(u => u.Contents.OfType<FunctionCallContent>()).FirstOrDefault();
        Assert.NotNull(fcc);
        Assert.Equal("get_weather", fcc.Name);
        Assert.Equal("toolu_abc", fcc.CallId);
        Assert.Equal("Shanghai", fcc.Arguments["city"]?.ToString());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_无ContentBlockDelta_不产生更新()
    {
        var chunks = new[]
        {
            """{"type":"message_start","message":{"id":"msg_1","role":"assistant"}}""",
            """{"type":"message_delta","delta":{"stop_reason":"end_turn"}}"""
        };
        var stream = SseResponseBuilder.Build(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        Assert.Empty(updates);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ContentBlockStartText_无Delta不产生更新()
    {
        var chunks = new[]
        {
            """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            """{"type":"content_block_stop","index":0}"""
        };
        var stream = SseResponseBuilder.Build(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        Assert.Empty(updates);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_messageStartAndDelta_产生UsageContent()
    {
        var chunks = new[]
        {
            """{"type":"message_start","message":{"id":"msg_1","role":"assistant","usage":{"input_tokens":25}}}""",
            """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello"}}""",
            """{"type":"content_block_stop","index":0}""",
            """{"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":10}}"""
        };
        var stream = SseResponseBuilder.Build(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        var usageContent = updates.SelectMany(u => u.Contents.OfType<UsageContent>()).FirstOrDefault();
        Assert.NotNull(usageContent);
        Assert.Equal(25, usageContent.Details.InputTokenCount);
        Assert.Equal(10, usageContent.Details.OutputTokenCount);
        Assert.Equal(35, usageContent.Details.TotalTokenCount);
    }

    #endregion

    #region 请求构建

    [Fact]
    public async Task BuildRequest_System消息_提取到顶层字段()
    {
        var handler = new MockHttpMessageHandler("""{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":0,"output_tokens":0}}""");
        var client = CreateClient(handler);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.System, "你是助手"),
            new ChatMessage(ChatRole.User, "你好")
        ]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);

        // system 提取到顶层
        Assert.Equal("你是助手", body.RootElement.GetProperty("system").GetString());

        // messages 数组不包含 system
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task BuildRequest_DefaultMaxTokens_为4096()
    {
        var handler = new MockHttpMessageHandler("""{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":0,"output_tokens":0}}""");
        var client = CreateClient(handler);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal(4096, body.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task BuildRequest_Tools使用InputSchema而非Parameters()
    {
        var handler = new MockHttpMessageHandler("""{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":0,"output_tokens":0}}""");
        var client = CreateClient(handler);

        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create((string city) => $"Weather in {city}", "get_weather", "获取天气")
            ]
        };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "天气")], options);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var tools = body.RootElement.GetProperty("tools");

        Assert.Equal(1, tools.GetArrayLength());
        // Anthropic 用 input_schema 而非 parameters
        Assert.True(tools[0].TryGetProperty("input_schema", out _));
        // 不应该有 parameters 字段
        Assert.False(tools[0].TryGetProperty("parameters", out _));
    }

    [Fact]
    public async Task BuildRequest_FunctionCallContent_序列化为ToolUse()
    {
        var handler = new MockHttpMessageHandler("""{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":0,"output_tokens":0}}""");
        var client = CreateClient(handler);

        var assistantMsg = new ChatMessage(ChatRole.Assistant,
        [
            new FunctionCallContent("toolu_1", "get_weather", new Dictionary<string, object?> { ["city"] = "Beijing" })
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "天气"),
            assistantMsg
        ]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var messages = body.RootElement.GetProperty("messages");

        var toolUseMsg = messages[1];
        Assert.Equal("assistant", toolUseMsg.GetProperty("role").GetString());
        var content = toolUseMsg.GetProperty("content");
        Assert.Equal(1, content.GetArrayLength());
        Assert.Equal("tool_use", content[0].GetProperty("type").GetString());
        Assert.Equal("toolu_1", content[0].GetProperty("id").GetString());
        Assert.Equal("get_weather", content[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task BuildRequest_FunctionResultContent_序列化为ToolResult()
    {
        var handler = new MockHttpMessageHandler("""{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":0,"output_tokens":0}}""");
        var client = CreateClient(handler);

        var toolResultMsg = new ChatMessage(ChatRole.Tool,
        [
            new FunctionResultContent("toolu_1", "晴天")
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "天气"),
            toolResultMsg
        ]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var messages = body.RootElement.GetProperty("messages");

        // Anthropic: tool_result 在 user 消息中
        var resultMsg = messages[1];
        Assert.Equal("user", resultMsg.GetProperty("role").GetString());
        var content = resultMsg.GetProperty("content");
        Assert.Equal("tool_result", content[0].GetProperty("type").GetString());
        Assert.Equal("toolu_1", content[0].GetProperty("tool_use_id").GetString());
        Assert.Equal("晴天", content[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task BuildRequest_未设Temperature_不发送Temperature字段()
    {
        var handler = new MockHttpMessageHandler("""{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":0,"output_tokens":0}}""");
        var client = CreateClient(handler);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        // 未设置 temperature 时不应发送该字段，由模型使用默认值
        Assert.False(body.RootElement.TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task BuildRequest_CustomMaxTokens和Temperature_覆盖默认值()
    {
        var handler = new MockHttpMessageHandler("""{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":0,"output_tokens":0}}""");
        var client = CreateClient(handler);

        var options = new ChatOptions
        {
            MaxOutputTokens = 8192,
            Temperature = 0.3f
        };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal(8192, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.3, body.RootElement.GetProperty("temperature").GetDouble(), 1);
    }

    [Fact]
    public async Task BuildRequest_多个FunctionCallContent_序列化为多个ToolUse()
    {
        var handler = new MockHttpMessageHandler("""{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":0,"output_tokens":0}}""");
        var client = CreateClient(handler);

        var assistantMsg = new ChatMessage(ChatRole.Assistant,
        [
            new FunctionCallContent("toolu_1", "get_weather", new Dictionary<string, object?> { ["city"] = "Beijing" }),
            new FunctionCallContent("toolu_2", "get_time", new Dictionary<string, object?> { ["tz"] = "UTC" })
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "天气和时间"),
            assistantMsg
        ]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var messages = body.RootElement.GetProperty("messages");

        var content = messages[1].GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());
        Assert.Equal("tool_use", content[0].GetProperty("type").GetString());
        Assert.Equal("get_weather", content[0].GetProperty("name").GetString());
        Assert.Equal("tool_use", content[1].GetProperty("type").GetString());
        Assert.Equal("get_time", content[1].GetProperty("name").GetString());
    }

    [Fact]
    public async Task BuildRequest_多个FunctionResultContent_序列化为多个ToolResult()
    {
        var handler = new MockHttpMessageHandler("""{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":0,"output_tokens":0}}""");
        var client = CreateClient(handler);

        var toolResultMsg = new ChatMessage(ChatRole.Tool,
        [
            new FunctionResultContent("toolu_1", "晴天"),
            new FunctionResultContent("toolu_2", "12:00")
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "天气"),
            toolResultMsg
        ]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var messages = body.RootElement.GetProperty("messages");

        // Anthropic: tool_result 在 user 消息中
        var resultMsg = messages[1];
        Assert.Equal("user", resultMsg.GetProperty("role").GetString());
        var content = resultMsg.GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());
        Assert.Equal("tool_result", content[0].GetProperty("type").GetString());
        Assert.Equal("toolu_1", content[0].GetProperty("tool_use_id").GetString());
        Assert.Equal("tool_result", content[1].GetProperty("type").GetString());
        Assert.Equal("toolu_2", content[1].GetProperty("tool_use_id").GetString());
    }

    #endregion

    #region URL 路径

    [Fact]
    public async Task CreateChatClient_AnthropicProviderBaseUrl_无重复V1路径()
    {
        // 模拟 CreateChatClient 中对 AnthropicProvider.BaseUrl 的处理逻辑
        var handler = new MockHttpMessageHandler("""{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":0,"output_tokens":0}}""");
        var provider = new AnthropicProvider { ApiKey = "test-key" };

        // 复现 CreateChatClient 中的 BaseAddress 设置逻辑
        var http = new HttpClient(handler);
        http.BaseAddress = provider.BaseUrl.EndsWith('/')
            ? new Uri(provider.BaseUrl)
            : new Uri(provider.BaseUrl + "/");
        http.DefaultRequestHeaders.Add("x-api-key", provider.ApiKey);
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var client = new AnthropicCompatibleChatClient(http, "test-model");
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var requestUrl = handler.LastRequest!.RequestUri!.ToString();
        Assert.DoesNotContain("/v1/v1/", requestUrl);
        Assert.Equal("https://api.anthropic.com/v1/messages", requestUrl);
    }

    #endregion

    #region 请求序列化补全

    [Fact]
    public async Task BuildRequest_Assistant消息含文本和ToolUse_内容不丢失()
    {
        var handler = new MockHttpMessageHandler("""{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":0,"output_tokens":0}}""");
        var client = CreateClient(handler);

        // assistant 消息同时有文本和 function call
        var assistantMsg = new ChatMessage(ChatRole.Assistant, "让我查一下天气。");
        assistantMsg.Contents.Add(new FunctionCallContent("toolu_1", "get_weather", new Dictionary<string, object?> { ["city"] = "Beijing" }));

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "天气"),
            assistantMsg
        ]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var messages = body.RootElement.GetProperty("messages");

        var toolUseMsg = messages[1];
        Assert.Equal("assistant", toolUseMsg.GetProperty("role").GetString());
        var content = toolUseMsg.GetProperty("content");

        // Anthropic: content 应包含 text 和 tool_use 两个块
        var types = new List<string>();
        for (var i = 0; i < content.GetArrayLength(); i++)
            types.Add(content[i].GetProperty("type").GetString()!);

        Assert.Contains("text", types);
        Assert.Contains("tool_use", types);

        // 验证文本内容
        var textBlock = content.EnumerateArray().First(b => b.GetProperty("type").GetString() == "text");
        Assert.Equal("让我查一下天气。", textBlock.GetProperty("text").GetString());
    }

    [Fact]
    public async Task BuildRequest_多条System消息_内容拼接()
    {
        var handler = new MockHttpMessageHandler("""{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":0,"output_tokens":0}}""");
        var client = CreateClient(handler);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.System, "你是助手"),
            new ChatMessage(ChatRole.System, "请用中文回答"),
            new ChatMessage(ChatRole.User, "你好")
        ]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);

        // 多条 System 消息应拼接为一条
        var system = body.RootElement.GetProperty("system").GetString();
        Assert.Contains("你是助手", system);
        Assert.Contains("请用中文回答", system);

        // messages 数组不含 system
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
    }

    #endregion

    #region 服务与生命周期

    [Fact]
    public void GetService_返回Null()
    {
        var handler = new MockHttpMessageHandler("");
        var client = CreateClient(handler);
        Assert.Null(client.GetService(typeof(object)));
    }

    [Fact]
    public void Dispose_不抛异常()
    {
        var handler = new MockHttpMessageHandler("");
        var client = CreateClient(handler);
        client.Dispose();
    }

    #endregion
}
