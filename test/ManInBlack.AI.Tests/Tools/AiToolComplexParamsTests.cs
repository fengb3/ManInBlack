using System.Text.Json;
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
}
