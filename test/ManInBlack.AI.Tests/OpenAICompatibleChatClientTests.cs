using System.Text.Json;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Xunit;

namespace ManInBlack.AI.Tests;

public class OpenAICompatibleChatClientTests
{
    private static OpenAICompatibleChatClient CreateClient(MockHttpMessageHandler handler)
    {
        return new OpenAICompatibleChatClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/") },
            "test-model");
    }

    #region 非流式

    [Fact]
    public async Task GetResponseAsync_文本响应_返回文本内容()
    {
        var json = """
                   {
                       "choices": [{
                           "message": { "content": "Hello, world!", "role": "assistant" },
                           "finish_reason": "stop"
                       }],
                       "usage": { "prompt_tokens": 10, "completion_tokens": 5 }
                   }
                   """;
        var handler = new MockHttpMessageHandler(json);
        var client = CreateClient(handler);

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hi")
        ]);

        var text = response.Messages[0].Contents.OfType<TextContent>().FirstOrDefault();
        Assert.Equal("Hello, world!", text?.Text);
    }

    [Fact]
    public async Task GetResponseAsync_ToolCall响应_返回FunctionCallContent()
    {
        var json = """
                   {
                       "choices": [{
                           "message": {
                               "role": "assistant",
                               "tool_calls": [{
                                   "id": "call_123",
                                   "type": "function",
                                   "function": {
                                       "name": "get_weather",
                                       "arguments": "{\"city\":\"Beijing\"}"
                                   }
                               }]
                           },
                           "finish_reason": "tool_calls"
                       }],
                       "usage": { "prompt_tokens": 20, "completion_tokens": 10 }
                   }
                   """;
        var handler = new MockHttpMessageHandler(json);
        var client = CreateClient(handler);

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "天气如何")
        ]);

        var fcc = response.Messages[0].Contents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(fcc);
        Assert.Equal("get_weather", fcc.Name);
        Assert.Equal("call_123", fcc.CallId);
        Assert.Equal("Beijing", fcc.Arguments["city"]?.ToString());
    }

    [Fact]
    public async Task GetResponseAsync_混合响应_同时包含文本和ToolCall()
    {
        var json = """
                   {
                       "choices": [{
                           "message": {
                               "role": "assistant",
                               "content": "让我查一下天气。",
                               "tool_calls": [{
                                   "id": "call_456",
                                   "type": "function",
                                   "function": {
                                       "name": "get_weather",
                                       "arguments": "{\"city\":\"Shanghai\"}"
                                   }
                               }]
                           },
                           "finish_reason": "tool_calls"
                       }],
                       "usage": { "prompt_tokens": 15, "completion_tokens": 8 }
                   }
                   """;
        var handler = new MockHttpMessageHandler(json);
        var client = CreateClient(handler);

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "上海天气")
        ]);

        var contents = response.Messages[0].Contents;
        Assert.Contains(contents, c => c is TextContent);
        Assert.Contains(contents, c => c is FunctionCallContent);
        Assert.Equal("让我查一下天气。", contents.OfType<TextContent>().First().Text);
    }

    [Fact]
    public async Task GetResponseAsync_Usage信息_正确提取TokenCount()
    {
        var json = """
                   {
                       "choices": [{
                           "message": { "content": "ok", "role": "assistant" },
                           "finish_reason": "stop"
                       }],
                       "usage": { "prompt_tokens": 100, "completion_tokens": 50 }
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
    public async Task GetResponseAsync_空Choices_抛InvalidOperationException()
    {
        var json = """{"choices":[]}""";
        var handler = new MockHttpMessageHandler(json);
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));
    }

    [Fact]
    public async Task GetResponseAsync_无Usage_返回Null()
    {
        var json = """
                   {
                       "choices": [{
                           "message": { "content": "ok", "role": "assistant" },
                           "finish_reason": "stop"
                       }]
                   }
                   """;
        var handler = new MockHttpMessageHandler(json);
        var client = CreateClient(handler);

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hi")
        ]);

        Assert.Null(response.Usage);
    }

    [Fact]
    public async Task GetResponseAsync_无效Arguments_返回空字典()
    {
        var json = """
                   {
                       "choices": [{
                           "message": {
                               "role": "assistant",
                               "tool_calls": [{
                                   "id": "call_bad",
                                   "type": "function",
                                   "function": {
                                       "name": "bad_tool",
                                       "arguments": "not-valid-json"
                                   }
                               }]
                           },
                           "finish_reason": "tool_calls"
                       }],
                       "usage": { "prompt_tokens": 0, "completion_tokens": 0 }
                   }
                   """;
        var handler = new MockHttpMessageHandler(json);
        var client = CreateClient(handler);

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "test")
        ]);

        var fcc = response.Messages[0].Contents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(fcc);
        Assert.Equal("bad_tool", fcc.Name);
        Assert.Empty(fcc.Arguments);
    }

    #endregion

    #region 流式

    [Fact]
    public async Task GetStreamingResponseAsync_多个文本Chunk_拼接为完整文本()
    {
        var chunks = new[]
        {
            """{"choices":[{"delta":{"content":"Hello"}}]}""",
            """{"choices":[{"delta":{"content":" world"}}]}""",
            """{"choices":[{"delta":{"content":"!"}}]}"""
        };
        var stream = SseResponseBuilder.BuildWithDone(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        Assert.Equal(3, updates.Count);
        var text = string.Concat(updates.SelectMany(u => u.Contents.OfType<TextContent>().Select(t => t.Text)));
        Assert.Equal("Hello world!", text);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ToolCall分片_累积为完整FunctionCall()
    {
        var chunks = new[]
        {
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_abc","function":{"name":"get_weather","arguments":""}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"ci"}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"ty\":\"Beijing\"}"}}]}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}"""
        };
        var stream = SseResponseBuilder.BuildWithDone(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        var fcc = updates.SelectMany(u => u.Contents.OfType<FunctionCallContent>()).FirstOrDefault();
        Assert.NotNull(fcc);
        Assert.Equal("get_weather", fcc.Name);
        Assert.Equal("call_abc", fcc.CallId);
        Assert.Equal("Beijing", fcc.Arguments["city"]?.ToString());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_混合文本和ToolCall()
    {
        var chunks = new[]
        {
            """{"choices":[{"delta":{"content":"查一下"}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"search","arguments":""}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"q\":\"test\"}"}}]}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}"""
        };
        var stream = SseResponseBuilder.BuildWithDone(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        var texts = updates.SelectMany(u => u.Contents.OfType<TextContent>()).ToList();
        var toolCalls = updates.SelectMany(u => u.Contents.OfType<FunctionCallContent>()).ToList();

        Assert.Equal("查一下", string.Concat(texts.Select(t => t.Text)));
        Assert.Single(toolCalls);
        Assert.Equal("search", toolCalls[0].Name);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ReasoningContent_作为TextReasoningContent返回()
    {
        var chunks = new[]
        {
            """{"choices":[{"delta":{"role":"assistant","reasoning_content":"Let me think"}}]}""",
            """{"choices":[{"delta":{"reasoning_content":" about it"}}]}""",
            """{"choices":[{"delta":{"content":"答案是42"}}]}"""
        };
        var stream = SseResponseBuilder.BuildWithDone(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        // reasoning_content 应作为 TextReasoningContent 返回
        var reasoning = string.Concat(updates.SelectMany(u => u.Contents.OfType<TextReasoningContent>().Select(t => t.Text)));
        Assert.Equal("Let me think about it", reasoning);

        // content 应作为 TextContent 返回
        var text = string.Concat(updates.SelectMany(u => u.Contents.OfType<TextContent>().Select(t => t.Text)));
        Assert.Equal("答案是42", text);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_空Delta无Content_不产生Update()
    {
        var chunks = new[]
        {
            """{"choices":[{"delta":{"role":"assistant"}}]}""",
            """{"choices":[{"delta":{}}]}"""
        };
        var stream = SseResponseBuilder.BuildWithDone(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        // delta 没有 content/reasoning_content/tool_calls → 不应该 yield update
        Assert.Empty(updates);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_多个ToolCall_各自独立累积()
    {
        var chunks = new[]
        {
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"get_weather","arguments":""}},{"index":1,"id":"call_2","function":{"name":"get_time","arguments":""}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"city\":\"BJ\"}"}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":1,"function":{"arguments":"{\"tz\":\"UTC\"}"}}]}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}"""
        };
        var stream = SseResponseBuilder.BuildWithDone(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        var toolCalls = updates.SelectMany(u => u.Contents.OfType<FunctionCallContent>()).ToList();
        Assert.Equal(2, toolCalls.Count);
        Assert.Equal("get_weather", toolCalls[0].Name);
        Assert.Equal("get_time", toolCalls[1].Name);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_FinishReasonStop_有累积ToolCall仍发出()
    {
        var chunks = new[]
        {
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"search","arguments":""}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"q\":\"test\"}"}}]}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"stop"}]}"""
        };
        var stream = SseResponseBuilder.BuildWithDone(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        var fcc = updates.SelectMany(u => u.Contents.OfType<FunctionCallContent>()).FirstOrDefault();
        Assert.NotNull(fcc);
        Assert.Equal("search", fcc.Name);
        Assert.Equal("test", fcc.Arguments["q"]?.ToString());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_有UsageChunk_最后一个为UsageContent()
    {
        var chunks = new[]
        {
            """{"choices":[{"delta":{"content":"hi"}}]}""",
            """{"choices":[{"delta":{"content":" there"}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"stop"}]}""",
            """{"choices":[],"usage":{"prompt_tokens":10,"completion_tokens":5}}"""
        };
        var stream = SseResponseBuilder.BuildWithDone(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        var usageContent = updates.SelectMany(u => u.Contents.OfType<UsageContent>()).FirstOrDefault();
        Assert.NotNull(usageContent);
        Assert.Equal(10, usageContent.Details.InputTokenCount);
        Assert.Equal(5, usageContent.Details.OutputTokenCount);
        Assert.Equal(15, usageContent.Details.TotalTokenCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_无UsageChunk_无UsageContent()
    {
        var chunks = new[]
        {
            """{"choices":[{"delta":{"content":"hi"}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"stop"}]}"""
        };
        var stream = SseResponseBuilder.BuildWithDone(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        Assert.Empty(updates.SelectMany(u => u.Contents.OfType<UsageContent>()));
    }

    #endregion

    #region 请求构建

    [Fact]
    public async Task BuildRequest_消息序列化_角色和内容正确()
    {
        var handler = new MockHttpMessageHandler("""{"choices":[{"message":{"content":"ok","role":"assistant"},"finish_reason":"stop"}]}""");
        var client = CreateClient(handler);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.System, "你是助手"),
            new ChatMessage(ChatRole.User, "你好"),
            new ChatMessage(ChatRole.Assistant, "你好！"),
            new ChatMessage(ChatRole.User, "再见")
        ]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var messages = body.RootElement.GetProperty("messages");

        Assert.Equal(4, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("你是助手", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("assistant", messages[2].GetProperty("role").GetString());
        Assert.Equal("再见", messages[3].GetProperty("content").GetString());
    }

    [Fact]
    public async Task BuildRequest_Tool定义_序列化为Tools数组()
    {
        var handler = new MockHttpMessageHandler("""{"choices":[{"message":{"content":"ok","role":"assistant"},"finish_reason":"stop"}]}""");
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
        Assert.Equal("function", tools[0].GetProperty("type").GetString());
        Assert.Equal("get_weather", tools[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("获取天气", tools[0].GetProperty("function").GetProperty("description").GetString());
    }

    [Fact]
    public async Task BuildRequest_流式请求_包含Stream标记()
    {
        var chunks = new[] { """{"choices":[{"delta":{"content":"hi"}}]}""" };
        var stream = SseResponseBuilder.BuildWithDone(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
            break;

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.True(body.RootElement.TryGetProperty("stream", out var streamProp));
        Assert.True(streamProp.GetBoolean());
        Assert.True(body.RootElement.TryGetProperty("stream_options", out var streamOpts));
        Assert.True(streamOpts.GetProperty("include_usage").GetBoolean());
    }

    [Fact]
    public async Task BuildRequest_非流式请求_不包含Stream标记()
    {
        var handler = new MockHttpMessageHandler("""{"choices":[{"message":{"content":"ok","role":"assistant"},"finish_reason":"stop"}]}""");
        var client = CreateClient(handler);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.False(body.RootElement.TryGetProperty("stream", out _));
    }

    [Fact]
    public async Task BuildRequest_FunctionCallContent_序列化为ToolCalls()
    {
        var handler = new MockHttpMessageHandler("""{"choices":[{"message":{"content":"ok","role":"assistant"},"finish_reason":"stop"}]}""");
        var client = CreateClient(handler);

        var assistantMsg = new ChatMessage(ChatRole.Assistant,
        [
            new FunctionCallContent("call_1", "get_weather", new Dictionary<string, object?> { ["city"] = "Beijing" })
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "天气"),
            assistantMsg,
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_1", "晴天")])
        ]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var messages = body.RootElement.GetProperty("messages");

        // assistant 消息应该有 tool_calls
        var assistantMsgJson = messages[1];
        Assert.True(assistantMsgJson.TryGetProperty("tool_calls", out var toolCalls));
        Assert.Equal(1, toolCalls.GetArrayLength());
        Assert.Equal("get_weather", toolCalls[0].GetProperty("function").GetProperty("name").GetString());

        // tool 消息应该有 tool_call_id
        var toolMsgJson = messages[2];
        Assert.True(toolMsgJson.TryGetProperty("tool_call_id", out var callId));
        Assert.Equal("call_1", callId.GetString());
        Assert.Equal("晴天", toolMsgJson.GetProperty("content").GetString());
    }

    [Fact]
    public async Task BuildRequest_ModelId_正确设置()
    {
        var handler = new MockHttpMessageHandler("""{"choices":[{"message":{"content":"ok","role":"assistant"},"finish_reason":"stop"}]}""");
        var client = CreateClient(handler);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("test-model", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task BuildRequest_ChatOptions为Null_不包含可选字段()
    {
        var handler = new MockHttpMessageHandler("""{"choices":[{"message":{"content":"ok","role":"assistant"},"finish_reason":"stop"}]}""");
        var client = CreateClient(handler);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.False(body.RootElement.TryGetProperty("max_tokens", out _));
        Assert.False(body.RootElement.TryGetProperty("temperature", out _));
        Assert.False(body.RootElement.TryGetProperty("top_p", out _));
    }

    [Fact]
    public async Task BuildRequest_ChatOptions设值_序列化正确()
    {
        var handler = new MockHttpMessageHandler("""{"choices":[{"message":{"content":"ok","role":"assistant"},"finish_reason":"stop"}]}""");
        var client = CreateClient(handler);

        var options = new ChatOptions
        {
            MaxOutputTokens = 2048,
            Temperature = 0.5f,
            TopP = 0.9f
        };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal(2048, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.5, body.RootElement.GetProperty("temperature").GetDouble(), 2);
        Assert.Equal(0.9, body.RootElement.GetProperty("top_p").GetDouble(), 2);
    }

    [Fact]
    public async Task BuildRequest_多个FunctionCallContent_序列化为ToolCalls数组()
    {
        var handler = new MockHttpMessageHandler("""{"choices":[{"message":{"content":"ok","role":"assistant"},"finish_reason":"stop"}]}""");
        var client = CreateClient(handler);

        var assistantMsg = new ChatMessage(ChatRole.Assistant,
        [
            new FunctionCallContent("call_1", "get_weather", new Dictionary<string, object?> { ["city"] = "Beijing" }),
            new FunctionCallContent("call_2", "get_time", new Dictionary<string, object?> { ["tz"] = "UTC" })
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "天气和时间"),
            assistantMsg
        ]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var messages = body.RootElement.GetProperty("messages");

        var toolCalls = messages[1].GetProperty("tool_calls");
        Assert.Equal(2, toolCalls.GetArrayLength());
        Assert.Equal("get_weather", toolCalls[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("call_1", toolCalls[0].GetProperty("id").GetString());
        Assert.Equal("get_time", toolCalls[1].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("call_2", toolCalls[1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task BuildRequest_多个FunctionResultContent_各自序列化为独立Tool消息()
    {
        var handler = new MockHttpMessageHandler("""{"choices":[{"message":{"content":"ok","role":"assistant"},"finish_reason":"stop"}]}""");
        var client = CreateClient(handler);

        var assistantMsg = new ChatMessage(ChatRole.Assistant,
        [
            new FunctionCallContent("call_1", "get_weather", new Dictionary<string, object?> { ["city"] = "Beijing" }),
            new FunctionCallContent("call_2", "get_time", new Dictionary<string, object?> { ["tz"] = "UTC" })
        ]);
        var toolResultMsg = new ChatMessage(ChatRole.Tool,
        [
            new FunctionResultContent("call_1", "晴天"),
            new FunctionResultContent("call_2", "12:00")
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "天气和时间"),
            assistantMsg,
            toolResultMsg
        ]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var messages = body.RootElement.GetProperty("messages");

        // assistant 消息（index 1）应含 tool_calls
        var assistantJson = messages[1];
        Assert.True(assistantJson.TryGetProperty("tool_calls", out var toolCalls));
        Assert.Equal(2, toolCalls.GetArrayLength());

        // 多个 FunctionResultContent 应各自生成独立的 tool 消息
        var toolMsg1 = messages[2];
        Assert.Equal("tool", toolMsg1.GetProperty("role").GetString());
        Assert.Equal("call_1", toolMsg1.GetProperty("tool_call_id").GetString());
        Assert.Equal("晴天", toolMsg1.GetProperty("content").GetString());

        var toolMsg2 = messages[3];
        Assert.Equal("tool", toolMsg2.GetProperty("role").GetString());
        Assert.Equal("call_2", toolMsg2.GetProperty("tool_call_id").GetString());
        Assert.Equal("12:00", toolMsg2.GetProperty("content").GetString());
    }

    #endregion

    #region 服务与生命周期

    [Fact]
    public void GetService_任何类型_返回Null()
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
