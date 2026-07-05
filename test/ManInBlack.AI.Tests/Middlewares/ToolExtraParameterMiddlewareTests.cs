using System.Text.Json.Nodes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

public class ToolExtraParameterMiddlewareTests
{
    // 种子 schema:含一个原参数 x,不含 required
    private const string SeedSchema =
        """{"type":"object","properties":{"x":{"type":"string"}}}""";

    private static AgentContext NewContext()
    {
        var tool = new ToolFunctionDeclaration("MyTool", "desc", SeedSchema);
        return new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            Options = new ChatOptions { Tools = [tool] }
        };
    }

    private static JsonObject SchemaOf(AITool tool) =>
        JsonNode.Parse(((AIFunctionDeclaration)tool).JsonSchema.GetRawText())!.AsObject();

    [Fact]
    public async Task Decorate_AppendsConfiguredParam_AndMarksRequired()
    {
        var settings = Options.Create(new ManInBlackSettings
        {
            ToolExtraParameter = new ToolExtraParameterSettings
            { ParamName = "purpose", ParamDescription = "why", Required = true }
        });
        var middleware = new ToolExtraParameterMiddleware(settings);
        var ctx = NewContext();

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var schema = SchemaOf(ctx.Options!.Tools[0]);
        Assert.True(schema["properties"]!.AsObject().ContainsKey("purpose"));
        Assert.Equal("why", schema["properties"]!["purpose"]!["description"]!.GetValue<string>());
        Assert.Contains("purpose",
            schema["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        // 原参数保留
        Assert.True(schema["properties"]!.AsObject().ContainsKey("x"));
    }

    [Fact]
    public async Task Decorate_UsesDefaults_WhenSettingsLeftDefault()
    {
        var middleware = new ToolExtraParameterMiddleware(Options.Create(new ManInBlackSettings()));
        var ctx = NewContext();

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var schema = SchemaOf(ctx.Options!.Tools[0]);
        Assert.True(schema["properties"]!.AsObject().ContainsKey("reason"));
        // 默认 Required=false → 不写 required 数组
        Assert.False(schema.ContainsKey("required"));
    }

    [Fact]
    public async Task Decorate_IsIdempotent_AcrossMultipleRuns()
    {
        var middleware = new ToolExtraParameterMiddleware(Options.Create(new ManInBlackSettings()));
        var ctx = NewContext();

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();
        var firstRaw = ((AIFunctionDeclaration)ctx.Options!.Tools[0]).JsonSchema.GetRawText();

        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();
        var secondRaw = ((AIFunctionDeclaration)ctx.Options!.Tools[0]).JsonSchema.GetRawText();

        Assert.Equal(firstRaw, secondRaw);
    }
}
