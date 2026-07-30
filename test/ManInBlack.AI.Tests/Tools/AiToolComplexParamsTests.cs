using System.Text.Json;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Xunit;

namespace ManInBlack.AI.Tests.Tools;

public class AiToolComplexParamsTests
{
    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddToolHandlers();                 // 测试项目生成的 internal 扩展（同程序集可见）
        services.AddScoped<ComplexParamsTestTools>();
        return services.BuildServiceProvider();
    }

    private static AIFunctionDeclaration GetDecl(string toolName)
    {
        using var sp = BuildSp();
        var registry = sp.GetRequiredService<ToolRegistry>();
        return registry.GetAll().First(d => d.Name == toolName);
    }

    [Fact]
    public void Schema_对象参数_生成object与公共属性()
    {
        var decl = GetDecl("PickOne");
        var option = decl.JsonSchema.GetProperty("properties").GetProperty("option");

        Assert.Equal("object", option.GetProperty("type").GetString());
        var props = option.GetProperty("properties");
        Assert.Contains("label", props.EnumerateObject().Select(p => p.Name));
        Assert.Contains("description", props.EnumerateObject().Select(p => p.Name));
        var required = option.GetProperty("required").EnumerateArray().Select(t => t.GetString()).ToArray();
        Assert.Contains("label", required);
        Assert.DoesNotContain("description", required);
    }

    [Theory]
    [InlineData("PickMany")]
    [InlineData("PickFromArray")]
    public void Schema_集合参数_生成array与items(string toolName)
    {
        var decl = GetDecl(toolName);
        var options = decl.JsonSchema.GetProperty("properties").GetProperty("options");

        Assert.Equal("array", options.GetProperty("type").GetString());
        Assert.Equal("object", options.GetProperty("items").GetProperty("type").GetString());
        Assert.Equal("string", options.GetProperty("items").GetProperty("properties").GetProperty("label").GetProperty("type").GetString());
    }

    [Fact]
    public void Schema_enum参数_生成string与枚举值()
    {
        var decl = GetDecl("SetColor");
        var color = decl.JsonSchema.GetProperty("properties").GetProperty("color");

        Assert.Equal("string", color.GetProperty("type").GetString());
        var values = color.GetProperty("enum").EnumerateArray().Select(t => t.GetString()).ToArray();
        Assert.Equal(new[] { "Red", "Green", "Blue" }, values);
    }

    private static async Task<(object? Result, Exception? Error)> ExecuteAsync(
        string toolName, IDictionary<string, object?> arguments)
    {
        var sp = BuildSp();
        using var scope = sp.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IToolExecutor>();
        var ctx = new ToolExecuteContext(scope.ServiceProvider)
        {
            ToolName = toolName,
            CallId = "c1",
            Arguments = arguments,
        };
        await executor.ExecuteAsync(ctx, default);
        return (ctx.Result, ctx.Error);
    }

    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task 运行时_对象参数_反序列化PascalCase()
    {
        var (result, error) = await ExecuteAsync("PickOne",
            new Dictionary<string, object?> { ["option"] = El("""{"Label":"A","Description":"x"}""") });
        Assert.Null(error);
        Assert.Equal("A", result);
    }

    [Fact]
    public async Task 运行时_对象参数_反序列化camelCase()
    {
        var (result, error) = await ExecuteAsync("PickOne",
            new Dictionary<string, object?> { ["option"] = El("""{"label":"B","description":"y"}""") });
        Assert.Null(error);
        Assert.Equal("B", result);
    }

    [Fact]
    public async Task 运行时_集合参数_反序列化()
    {
        var (result, error) = await ExecuteAsync("PickMany",
            new Dictionary<string, object?> { ["options"] = El("""[{"label":"A"},{"label":"B"}]""") });
        Assert.Null(error);
        Assert.Equal("2", result);
    }

    [Theory]
    [InlineData("\"Green\"", "Green")]
    [InlineData("1", "Green")]   // 数字也兼容
    public async Task 运行时_enum参数_反序列化(string jsonValue, string expected)
    {
        var (result, error) = await ExecuteAsync("SetColor",
            new Dictionary<string, object?> { ["color"] = El(jsonValue) });
        Assert.Null(error);
        Assert.Equal(expected, result);
    }
}
