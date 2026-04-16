using System.Text.Json;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Xunit;

namespace ManInBlack.AI.Tests;

public class GeminiCompatibleChatClientTests
{
    private const string TestApiKey = "test-api-key";

    private static GeminiCompatibleChatClient CreateClient(MockHttpMessageHandler handler)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };
        return new GeminiCompatibleChatClient(http, TestApiKey, "gemini-test");
    }

    #region 非流式

    [Fact]
    public async Task GetResponseAsync_文本响应_返回文本内容()
    {
        var json = """
                   {
                       "candidates": [{
                           "content": {
                               "parts": [{"text": "Hello from Gemini!"}],
                               "role": "model"
                           },
                           "finishReason": "STOP"
                       }],
                       "usageMetadata": {
                           "promptTokenCount": 10,
                           "candidatesTokenCount": 5,
                           "totalTokenCount": 15
                       }
                   }
                   """;
        var handler = new MockHttpMessageHandler(json);
        var client = CreateClient(handler);

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hi")
        ]);

        var text = response.Messages[0].Contents.OfType<TextContent>().FirstOrDefault();
        Assert.Equal("Hello from Gemini!", text?.Text);
    }

    [Fact]
    public async Task GetResponseAsync_FunctionCall响应_返回FunctionCallContent()
    {
        var json = """
                   {
                       "candidates": [{
                           "content": {
                               "parts": [{"functionCall": {"name": "get_weather", "args": {"city": "Beijing"}}}],
                               "role": "model"
                           },
                           "finishReason": "STOP"
                       }],
                       "usageMetadata": {
                           "promptTokenCount": 20,
                           "candidatesTokenCount": 10,
                           "totalTokenCount": 30
                       }
                   }
                   """;
        var handler = new MockHttpMessageHandler(json);
        var client = CreateClient(handler);

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "北京天气")
        ]);

        var fcc = response.Messages[0].Contents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(fcc);
        Assert.Equal("get_weather", fcc.Name);
        Assert.Equal("Beijing", fcc.Arguments["city"]?.ToString());
    }

    [Fact]
    public async Task GetResponseAsync_混合文本和FunctionCall()
    {
        var json = """
                   {
                       "candidates": [{
                           "content": {
                               "parts": [
                                   {"text": "让我查一下。"},
                                   {"functionCall": {"name": "search", "args": {"q": "test"}}}
                               ],
                               "role": "model"
                           },
                           "finishReason": "STOP"
                       }],
                       "usageMetadata": {
                           "promptTokenCount": 10,
                           "candidatesTokenCount": 10,
                           "totalTokenCount": 20
                       }
                   }
                   """;
        var handler = new MockHttpMessageHandler(json);
        var client = CreateClient(handler);

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "搜索")
        ]);

        var contents = response.Messages[0].Contents;
        Assert.Contains(contents, c => c is TextContent);
        Assert.Contains(contents, c => c is FunctionCallContent);
    }

    [Fact]
    public async Task GetResponseAsync_Usage信息_正确提取()
    {
        var json = """
                   {
                       "candidates": [{
                           "content": {
                               "parts": [{"text": "ok"}],
                               "role": "model"
                           },
                           "finishReason": "STOP"
                       }],
                       "usageMetadata": {
                           "promptTokenCount": 100,
                           "candidatesTokenCount": 50,
                           "totalTokenCount": 150
                       }
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
                       "candidates": [{
                           "content": {
                               "parts": [{"text": "ok"}],
                               "role": "model"
                           },
                           "finishReason": "STOP"
                       }],
                       "usageMetadata": {
                           "promptTokenCount": 100,
                           "candidatesTokenCount": 50,
                           "totalTokenCount": 150
                       }
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
    public async Task GetResponseAsync_无Candidates_抛InvalidOperationException()
    {
        var json = """{"candidates":[]}""";
        var handler = new MockHttpMessageHandler(json);
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));
    }

    [Fact]
    public async Task GetResponseAsync_空Parts_无内容返回()
    {
        var json = """
                   {
                       "candidates": [{
                           "content": {
                               "parts": [],
                               "role": "model"
                           },
                           "finishReason": "STOP"
                       }],
                       "usageMetadata": {
                           "promptTokenCount": 0,
                           "candidatesTokenCount": 0,
                           "totalTokenCount": 0
                       }
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
    public async Task GetStreamingResponseAsync_文本Chunk_返回TextContent()
    {
        var chunks = new[]
        {
            """{"candidates":[{"content":{"parts":[{"text":"Hello"}],"role":"model"}}]}""",
            """{"candidates":[{"content":{"parts":[{"text":" from"}],"role":"model"}}]}""",
            """{"candidates":[{"content":{"parts":[{"text":" Gemini!"}],"role":"model"}}]}"""
        };
        var stream = SseResponseBuilder.BuildWithDone(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        var text = string.Concat(updates.SelectMany(u => u.Contents.OfType<TextContent>().Select(t => t.Text)));
        Assert.Equal("Hello from Gemini!", text);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_FunctionCallChunk_返回FunctionCallContent()
    {
        // Gemini 的 function call 是完整的，不需要分片累积
        var chunks = new[]
        {
            """{"candidates":[{"content":{"parts":[{"functionCall":{"name":"get_weather","args":{"city":"Beijing"}}}],"role":"model"}}]}"""
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
        Assert.Equal("Beijing", fcc.Arguments["city"]?.ToString());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_空Parts_不产生更新()
    {
        var chunks = new[]
        {
            """{"candidates":[{"content":{"parts":[],"role":"model"}}]}"""
        };
        var stream = SseResponseBuilder.BuildWithDone(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        Assert.Empty(updates);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_单个Chunk多个TextParts_各自发出更新()
    {
        var chunks = new[]
        {
            """{"candidates":[{"content":{"parts":[{"text":"Hello"},{"text":" World"}],"role":"model"}}]}"""
        };
        var stream = SseResponseBuilder.BuildWithDone(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        Assert.Equal(2, updates.Count);
        Assert.Equal("Hello", updates[0].Contents.OfType<TextContent>().First().Text);
        Assert.Equal(" World", updates[1].Contents.OfType<TextContent>().First().Text);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_有UsageMetadata_最后一个为UsageContent()
    {
        var chunks = new[]
        {
            """{"candidates":[{"content":{"parts":[{"text":"Hello"}],"role":"model"}}],"usageMetadata":{"promptTokenCount":10,"candidatesTokenCount":5,"totalTokenCount":15}}"""
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
    public async Task GetStreamingResponseAsync_无UsageMetadata_无UsageContent()
    {
        var chunks = new[]
        {
            """{"candidates":[{"content":{"parts":[{"text":"Hello"}],"role":"model"}}]}"""
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
    public async Task BuildRequest_ApiKey在URL参数中()
    {
        var handler = new MockHttpMessageHandler("""{"candidates":[{"content":{"parts":[{"text":"ok"}],"role":"model"}}],"usageMetadata":{"promptTokenCount":0,"candidatesTokenCount":0,"totalTokenCount":0}}""");
        var client = CreateClient(handler);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var url = handler.LastRequest!.RequestUri!.ToString();
        Assert.Contains($"key={TestApiKey}", url);
    }

    [Fact]
    public async Task BuildRequest_ModelId在URL路径中()
    {
        var handler = new MockHttpMessageHandler("""{"candidates":[{"content":{"parts":[{"text":"ok"}],"role":"model"}}],"usageMetadata":{"promptTokenCount":0,"candidatesTokenCount":0,"totalTokenCount":0}}""");
        var client = CreateClient(handler);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var url = handler.LastRequest!.RequestUri!.ToString();
        Assert.Contains("gemini-test", url);
    }

    [Fact]
    public async Task BuildRequest_System消息_序列化为UserRole()
    {
        var handler = new MockHttpMessageHandler("""{"candidates":[{"content":{"parts":[{"text":"ok"}],"role":"model"}}],"usageMetadata":{"promptTokenCount":0,"candidatesTokenCount":0,"totalTokenCount":0}}""");
        var client = CreateClient(handler);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.System, "你是助手"),
            new ChatMessage(ChatRole.User, "你好")
        ]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var contents = body.RootElement.GetProperty("contents");

        // system 消息在 Gemini 中作为 user 角色发送
        Assert.Equal("user", contents[0].GetProperty("role").GetString());
        Assert.Equal("你是助手", contents[0].GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task BuildRequest_Assistant消息_序列化为Model角色()
    {
        var handler = new MockHttpMessageHandler("""{"candidates":[{"content":{"parts":[{"text":"ok"}],"role":"model"}}],"usageMetadata":{"promptTokenCount":0,"candidatesTokenCount":0,"totalTokenCount":0}}""");
        var client = CreateClient(handler);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "hi"),
            new ChatMessage(ChatRole.Assistant, "你好！"),
            new ChatMessage(ChatRole.User, "再见")
        ]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var contents = body.RootElement.GetProperty("contents");

        // assistant → model
        Assert.Equal("model", contents[1].GetProperty("role").GetString());
    }

    [Fact]
    public async Task BuildRequest_FunctionCallContent_序列化为FunctionCall()
    {
        var handler = new MockHttpMessageHandler("""{"candidates":[{"content":{"parts":[{"text":"ok"}],"role":"model"}}],"usageMetadata":{"promptTokenCount":0,"candidatesTokenCount":0,"totalTokenCount":0}}""");
        var client = CreateClient(handler);

        var assistantMsg = new ChatMessage(ChatRole.Assistant,
        [
            new FunctionCallContent("call_1", "get_weather", new Dictionary<string, object?> { ["city"] = "Beijing" })
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "天气"),
            assistantMsg
        ]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var contents = body.RootElement.GetProperty("contents");

        var modelMsg = contents[1];
        Assert.Equal("model", modelMsg.GetProperty("role").GetString());
        var parts = modelMsg.GetProperty("parts");
        Assert.True(parts[0].TryGetProperty("functionCall", out var fc));
        Assert.Equal("get_weather", fc.GetProperty("name").GetString());
    }

    [Fact]
    public async Task BuildRequest_FunctionResultContent_序列化为FunctionResponse()
    {
        var handler = new MockHttpMessageHandler("""{"candidates":[{"content":{"parts":[{"text":"ok"}],"role":"model"}}],"usageMetadata":{"promptTokenCount":0,"candidatesTokenCount":0,"totalTokenCount":0}}""");
        var client = CreateClient(handler);

        // Gemini 的 FunctionResultContent 需要通过 callId 查找 name，
        // 所以需要先有 FunctionCallContent 在历史中
        var toolCallMsg = new ChatMessage(ChatRole.Assistant,
        [
            new FunctionCallContent("call_1", "get_weather", new Dictionary<string, object?> { ["city"] = "Beijing" })
        ]);
        var toolResultMsg = new ChatMessage(ChatRole.Tool,
        [
            new FunctionResultContent("call_1", "晴天")
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "天气"),
            toolCallMsg,
            toolResultMsg
        ]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var contents = body.RootElement.GetProperty("contents");

        // tool result 在 user 消息中作为 functionResponse
        var resultMsg = contents[2];
        Assert.Equal("user", resultMsg.GetProperty("role").GetString());
        var parts = resultMsg.GetProperty("parts");
        Assert.True(parts[0].TryGetProperty("functionResponse", out var fr));
        // name 应该通过 callId 查找到 "get_weather"
        Assert.Equal("get_weather", fr.GetProperty("name").GetString());
    }

    [Fact]
    public async Task BuildRequest_Tools包裹在FunctionDeclarations中()
    {
        var handler = new MockHttpMessageHandler("""{"candidates":[{"content":{"parts":[{"text":"ok"}],"role":"model"}}],"usageMetadata":{"promptTokenCount":0,"candidatesTokenCount":0,"totalTokenCount":0}}""");
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

        // Gemini: tools = [{ functionDeclarations: [...] }]
        Assert.Equal(1, tools.GetArrayLength());
        var declarations = tools[0].GetProperty("functionDeclarations");
        Assert.Equal(1, declarations.GetArrayLength());
        Assert.Equal("get_weather", declarations[0].GetProperty("name").GetString());
        Assert.Equal("获取天气", declarations[0].GetProperty("description").GetString());
    }

    [Fact]
    public async Task BuildRequest_DefaultGenerationConfig_默认值正确()
    {
        var handler = new MockHttpMessageHandler("""{"candidates":[{"content":{"parts":[{"text":"ok"}],"role":"model"}}],"usageMetadata":{"promptTokenCount":0,"candidatesTokenCount":0,"totalTokenCount":0}}""");
        var client = CreateClient(handler);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var config = body.RootElement.GetProperty("generationConfig");
        Assert.Equal(4096, config.GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal(0.7, config.GetProperty("temperature").GetDouble(), 1);
    }

    [Fact]
    public async Task BuildRequest_CustomGenerationConfig_覆盖默认值()
    {
        var handler = new MockHttpMessageHandler("""{"candidates":[{"content":{"parts":[{"text":"ok"}],"role":"model"}}],"usageMetadata":{"promptTokenCount":0,"candidatesTokenCount":0,"totalTokenCount":0}}""");
        var client = CreateClient(handler);

        var options = new ChatOptions
        {
            MaxOutputTokens = 2048,
            Temperature = 0.5f,
            TopP = 0.8f
        };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var config = body.RootElement.GetProperty("generationConfig");
        Assert.Equal(2048, config.GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal(0.5, config.GetProperty("temperature").GetDouble(), 1);
        Assert.Equal(0.8, config.GetProperty("topP").GetDouble(), 1);
    }

    [Fact]
    public async Task BuildRequest_Tool角色映射为User()
    {
        var handler = new MockHttpMessageHandler("""{"candidates":[{"content":{"parts":[{"text":"ok"}],"role":"model"}}],"usageMetadata":{"promptTokenCount":0,"candidatesTokenCount":0,"totalTokenCount":0}}""");
        var client = CreateClient(handler);

        var toolCallMsg = new ChatMessage(ChatRole.Assistant,
        [
            new FunctionCallContent("call_1", "get_weather", new Dictionary<string, object?> { ["city"] = "Beijing" })
        ]);
        var toolResultMsg = new ChatMessage(ChatRole.Tool,
        [
            new FunctionResultContent("call_1", "晴天")
        ]);

        await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "天气"),
            toolCallMsg,
            toolResultMsg
        ]);

        var body = JsonDocument.Parse(handler.LastRequestBody!);
        var contents = body.RootElement.GetProperty("contents");

        // Tool 角色的消息映射为 "user"，含 functionResponse
        var resultMsg = contents[2];
        Assert.Equal("user", resultMsg.GetProperty("role").GetString());
        var parts = resultMsg.GetProperty("parts");
        Assert.True(parts[0].TryGetProperty("functionResponse", out _));
    }

    #endregion

    #region FunctionCall CallId

    [Fact]
    public async Task GetStreamingResponseAsync_多个FunctionCall_各自有独立CallId()
    {
        var chunks = new[]
        {
            """{"candidates":[{"content":{"parts":[{"functionCall":{"name":"get_weather","args":{"city":"BJ"}}},{"functionCall":{"name":"get_time","args":{"tz":"UTC"}}}],"role":"model"}}]}"""
        };
        var stream = SseResponseBuilder.BuildWithDone(chunks);
        var handler = new MockHttpMessageHandler(stream);
        var client = CreateClient(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            updates.Add(update);

        var toolCalls = updates.SelectMany(u => u.Contents.OfType<FunctionCallContent>()).ToList();
        Assert.Equal(2, toolCalls.Count);

        // 每个 FunctionCallContent 应有非空且不同的 CallId
        Assert.False(string.IsNullOrEmpty(toolCalls[0].CallId));
        Assert.False(string.IsNullOrEmpty(toolCalls[1].CallId));
        Assert.NotEqual(toolCalls[0].CallId, toolCalls[1].CallId);
    }

    [Fact]
    public async Task GetResponseAsync_多个FunctionCall_各自有独立CallId()
    {
        var json = """
                   {
                       "candidates": [{
                           "content": {
                               "parts": [
                                   {"functionCall": {"name": "get_weather", "args": {"city": "BJ"}}},
                                   {"functionCall": {"name": "get_time", "args": {"tz": "UTC"}}}
                               ],
                               "role": "model"
                           },
                           "finishReason": "STOP"
                       }],
                       "usageMetadata": {
                           "promptTokenCount": 10,
                           "candidatesTokenCount": 10,
                           "totalTokenCount": 20
                       }
                   }
                   """;
        var handler = new MockHttpMessageHandler(json);
        var client = CreateClient(handler);

        var response = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "天气和时间")
        ]);

        var toolCalls = response.Messages[0].Contents.OfType<FunctionCallContent>().ToList();
        Assert.Equal(2, toolCalls.Count);

        // 每个 FunctionCallContent 应有非空且不同的 CallId
        Assert.False(string.IsNullOrEmpty(toolCalls[0].CallId));
        Assert.False(string.IsNullOrEmpty(toolCalls[1].CallId));
        Assert.NotEqual(toolCalls[0].CallId, toolCalls[1].CallId);
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
