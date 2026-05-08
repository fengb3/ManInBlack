using System.Text.Json;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ManInBlack.AI.Tests.Middlewares;

/// <summary>
/// 手写 FakeOptionsMonitor，用于测试配置注入场景
/// </summary>
public class FakeOptionsMonitor<T> : IOptionsMonitor<T>
{
    public T CurrentValue { get; private set; }
    public FakeOptionsMonitor(T value) => CurrentValue = value;
    public T Get(string? name) => CurrentValue;
    public IDisposable OnChange(Action<T, string?> listener) => EmptyDisposable.Instance;
}

file class EmptyDisposable : IDisposable
{
    public static readonly EmptyDisposable Instance = new();
    public void Dispose() { }
}

public class ToolPromptMiddlewareTests
{
    /// <summary>
    /// 辅助方法：创建一个简单的 JSON Schema，包含指定参数
    /// </summary>
    private static string MakeSchema(params (string name, string type, string description)[] parameters)
    {
        var props = new Dictionary<string, object>();
        var required = new List<string>();
        foreach (var (name, type, desc) in parameters)
        {
            props[name] = new { type, description = desc };
        }
        var schema = new
        {
            type = "object",
            properties = props,
            required = required.ToArray()
        };
        return JsonSerializer.Serialize(schema);
    }

    /// <summary>
    /// 创建一个带有工具声明的 AgentContext
    /// </summary>
    private static AgentContext MakeContext(params (string name, string description, string schema)[] tools)
    {
        var ctx = new AgentContext(TestHelpers.EmptyServiceProvider)
        {
            Options = new ChatOptions { Tools = [] }
        };
        foreach (var (name, desc, schema) in tools)
        {
            ctx.Options.Tools.Add(new ToolFunctionDeclaration(name, desc, schema));
        }
        return ctx;
    }

    /// <summary>
    /// 从 context 中提取唯一的 ToolFunctionDeclaration
    /// </summary>
    private static ToolFunctionDeclaration GetSingleTool(AgentContext ctx)
    {
        var tool = Assert.Single(ctx.Options!.Tools!);
        return Assert.IsType<ToolFunctionDeclaration>(tool);
    }

    private static ToolPromptMiddleware CreateMiddleware(
        IOptionsMonitor<ManInBlackSettings>? optionsMonitor = null)
    {
        return new ToolPromptMiddleware(
            optionsMonitor,
            NullLogger<ToolPromptMiddleware>.Instance);
    }

    // ===== 测试 1：per-request 覆盖工具描述 =====
    [Fact]
    public async Task PerRequest_OverrideDescription_ShouldReplaceToolDescription()
    {
        var schema = MakeSchema(("city", "string", "城市名称"));
        var ctx = MakeContext(("GetWeather", "获取天气", schema));
        ctx.ToolDescriptionOverrides =
        [
            new ToolDescriptionOverride
            {
                ToolName = "GetWeather",
                Description = "获取指定城市的天气预报"
            }
        ];

        var middleware = CreateMiddleware();
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var tool = GetSingleTool(ctx);
        Assert.Equal("获取指定城市的天气预报", tool.Description);
    }

    // ===== 测试 2：per-request 覆盖参数描述 =====
    [Fact]
    public async Task PerRequest_OverrideParameterDescription_ShouldModifySchema()
    {
        var schema = MakeSchema(("city", "string", "城市名称"));
        var ctx = MakeContext(("GetWeather", "获取天气", schema));
        ctx.ToolDescriptionOverrides =
        [
            new ToolDescriptionOverride
            {
                ToolName = "GetWeather",
                ParameterOverrides = new Dictionary<string, string>
                {
                    ["city"] = "城市名称，例如北京、上海"
                }
            }
        ];

        var middleware = CreateMiddleware();
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var tool = GetSingleTool(ctx);
        using var doc = JsonDocument.Parse(tool.JsonSchema.GetRawText());
        var paramDesc = doc.RootElement.GetProperty("properties").GetProperty("city").GetProperty("description").GetString();
        Assert.Equal("城市名称，例如北京、上海", paramDesc);
    }

    // ===== 测试 3：per-request 覆盖返回值描述 =====
    [Fact]
    public async Task PerRequest_OverrideReturnsDescription_ShouldReplaceReturnSchema()
    {
        var schema = MakeSchema(("city", "string", "城市名称"));
        var ctx = MakeContext(("GetWeather", "获取天气", schema));
        ctx.ToolDescriptionOverrides =
        [
            new ToolDescriptionOverride
            {
                ToolName = "GetWeather",
                ReturnsDescription = "天气信息，包含温度、湿度和风力"
            }
        ];

        var middleware = CreateMiddleware();
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var tool = GetSingleTool(ctx);
        Assert.NotNull(tool.ReturnJsonSchema);
        using var doc = JsonDocument.Parse(tool.ReturnJsonSchema!.Value.GetRawText());
        Assert.Equal("天气信息，包含温度、湿度和风力",
            doc.RootElement.GetProperty("description").GetString());
    }

    // ===== 测试 4：per-request 动态增加参数 =====
    [Fact]
    public async Task PerRequest_AdditionalParameter_ShouldExtendSchema()
    {
        var schema = MakeSchema(("city", "string", "城市名称"));
        var ctx = MakeContext(("GetWeather", "获取天气", schema));
        ctx.ToolDescriptionOverrides =
        [
            new ToolDescriptionOverride
            {
                ToolName = "GetWeather",
                AdditionalParameters =
                [
                    new ToolParameterOverride
                    {
                        Name = "unit",
                        Type = "string",
                        Description = "温度单位，celsius 或 fahrenheit",
                        Required = true
                    }
                ]
            }
        ];

        var middleware = CreateMiddleware();
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var tool = GetSingleTool(ctx);
        using var doc = JsonDocument.Parse(tool.JsonSchema.GetRawText());
        var props = doc.RootElement.GetProperty("properties");
        Assert.True(props.TryGetProperty("unit", out var unitProp));
        Assert.Equal("温度单位，celsius 或 fahrenheit", unitProp.GetProperty("description").GetString());
        Assert.Equal("string", unitProp.GetProperty("type").GetString());

        var required = doc.RootElement.GetProperty("required");
        Assert.Contains("unit", required.EnumerateArray().Select(e => e.GetString()));
    }

    // ===== 测试 5：配置覆盖 =====
    [Fact]
    public async Task Config_OverrideDescription_ShouldApplyFromSettings()
    {
        var schema = MakeSchema(("city", "string", "城市名称"));
        var ctx = MakeContext(("GetWeather", "获取天气", schema));

        var settings = new ManInBlackSettings
        {
            ToolDescriptions =
            [
                new ToolDescriptionSetting
                {
                    ToolName = "GetWeather",
                    Description = "配置中的天气描述"
                }
            ]
        };
        var middleware = CreateMiddleware(new FakeOptionsMonitor<ManInBlackSettings>(settings));
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var tool = GetSingleTool(ctx);
        Assert.Equal("配置中的天气描述", tool.Description);
    }

    // ===== 测试 6：per-request 优先于 config =====
    [Fact]
    public async Task PerRequest_WinsOverConfig_WhenBothPresent()
    {
        var schema = MakeSchema(("city", "string", "城市名称"));
        var ctx = MakeContext(("GetWeather", "获取天气", schema));

        var settings = new ManInBlackSettings
        {
            ToolDescriptions =
            [
                new ToolDescriptionSetting
                {
                    ToolName = "GetWeather",
                    Description = "配置描述"
                }
            ]
        };
        ctx.ToolDescriptionOverrides =
        [
            new ToolDescriptionOverride
            {
                ToolName = "GetWeather",
                Description = "请求级描述"
            }
        ];

        var middleware = CreateMiddleware(new FakeOptionsMonitor<ManInBlackSettings>(settings));
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var tool = GetSingleTool(ctx);
        Assert.Equal("请求级描述", tool.Description);
    }

    // ===== 测试 7：无 override 时不修改 =====
    [Fact]
    public async Task NoOverrides_ShouldNotModifyTools()
    {
        var schema = MakeSchema(("city", "string", "城市名称"));
        var ctx = MakeContext(("GetWeather", "获取天气", schema));

        var middleware = CreateMiddleware();
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var tool = GetSingleTool(ctx);
        Assert.Equal("获取天气", tool.Description);
    }

    // ===== 测试 8：工具名不匹配时跳过 =====
    [Fact]
    public async Task ToolNotFound_ShouldSkip()
    {
        var schema = MakeSchema(("city", "string", "城市名称"));
        var ctx = MakeContext(("GetWeather", "获取天气", schema));
        ctx.ToolDescriptionOverrides =
        [
            new ToolDescriptionOverride
            {
                ToolName = "NonExistentTool",
                Description = "不存在的工具"
            }
        ];

        var middleware = CreateMiddleware();
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var tool = GetSingleTool(ctx);
        Assert.Equal("获取天气", tool.Description);
    }

    // ===== 测试 9：幂等性 =====
    [Fact]
    public async Task Idempotent_ShouldNotDoubleOverride()
    {
        var schema = MakeSchema(("city", "string", "城市名称"));
        var ctx = MakeContext(("GetWeather", "获取天气", schema));
        ctx.ToolDescriptionOverrides =
        [
            new ToolDescriptionOverride
            {
                ToolName = "GetWeather",
                Description = "第一次覆盖"
            }
        ];

        var middleware = CreateMiddleware();
        // 第一次执行
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();
        // 修改为第二次的覆盖
        ctx.ToolDescriptionOverrides![0].Description = "第二次覆盖";
        // 第二次执行（应跳过，因为幂等性标记已存在）
        await middleware.HandleAsync(ctx, () => TestHelpers.EmptyStream).ToListAsync();

        var tool = GetSingleTool(ctx);
        Assert.Equal("第一次覆盖", tool.Description);
    }
}
