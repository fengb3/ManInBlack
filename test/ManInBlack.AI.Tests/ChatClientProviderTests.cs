using System;
using System.Net.Http;
using Microsoft.Extensions.AI;
using Xunit;

namespace ManInBlack.AI.Tests;

public class ChatClientProviderTests
{
    // Mscc.GenerativeAI 对 key 做格式/长度校验，测试里需要符合 Google AI Studio key 形状。
    private static readonly string FakeGeminiApiKey = "AIzaSy" + new string('x', 33);

    private static IHttpClientFactory CreateFactory() => new FakeHttpClientFactory();

    [Fact]
    public void CreateChatClient_OpenAI_ReturnsIChatClient()
    {
        var choice = new ModelChoice
        {
            Schema = "OpenAI",
            ApiKey = "sk-test",
            BaseUrl = "https://api.openai.com",
            ModelId = "gpt-4o"
        };

        var client = ChatClientProviderExtensions.CreateChatClient(CreateFactory(), choice);

        Assert.NotNull(client);
        Assert.IsAssignableFrom<IChatClient>(client);
    }

    [Fact]
    public void CreateChatClient_OpenAI_CustomBaseUrl_NoThrow()
    {
        var choice = new ModelChoice
        {
            Schema = "OpenAI",
            ApiKey = "sk-test",
            BaseUrl = "https://api.deepseek.com",
            ModelId = "deepseek-chat"
        };

        var client = ChatClientProviderExtensions.CreateChatClient(CreateFactory(), choice);

        Assert.NotNull(client);
    }

    [Fact]
    public void CreateChatClient_Anthropic_ReturnsIChatClient()
    {
        var choice = new ModelChoice
        {
            Schema = "Anthropic",
            ApiKey = "sk-ant-test",
            BaseUrl = "https://api.anthropic.com",
            ModelId = "claude-sonnet-4-20250514"
        };

        var client = ChatClientProviderExtensions.CreateChatClient(CreateFactory(), choice);

        Assert.NotNull(client);
        Assert.IsAssignableFrom<IChatClient>(client);
    }

    [Fact]
    public void CreateChatClient_Gemini_ReturnsIChatClient()
    {
        var choice = new ModelChoice
        {
            Schema = "Gemini",
            ApiKey = FakeGeminiApiKey,
            BaseUrl = "https://generativelanguage.googleapis.com",
            ModelId = "gemini-2.5-flash"
        };

        var client = ChatClientProviderExtensions.CreateChatClient(CreateFactory(), choice);

        Assert.NotNull(client);
        Assert.IsAssignableFrom<IChatClient>(client);
    }

    [Fact]
    public void CreateChatClient_Gemini_CustomBaseUrl_NoThrow()
    {
        var choice = new ModelChoice
        {
            Schema = "Gemini",
            ApiKey = FakeGeminiApiKey,
            BaseUrl = "https://vertex.example.com",
            ModelId = "gemini-2.5-flash"
        };

        var client = ChatClientProviderExtensions.CreateChatClient(CreateFactory(), choice);

        Assert.NotNull(client);
    }

    [Fact]
    public void CreateChatClient_UnsupportedSchema_ThrowsNotSupportedException()
    {
        var choice = new ModelChoice
        {
            Schema = "Unknown",
            ApiKey = "x",
            ModelId = "x"
        };

        Assert.Throws<NotSupportedException>(() =>
            ChatClientProviderExtensions.CreateChatClient(CreateFactory(), choice));
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
