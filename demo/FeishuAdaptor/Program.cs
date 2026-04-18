// Load .env from executable directory

using System.Text;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using FeishuAdaptor;
using FeishuAdaptor.FeishuCard;
using FeishuNetSdk;
using FeishuAdaptor.FeishuCard.Cards;
using FeishuAdaptor.FeishuCard.CardViews;
using ManInBlack.AI;
using ManInBlack.AI.Core;
using ManInBlack.AI.Core.Attributes;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
if (File.Exists(envPath))
    Env.Load(envPath);
else
    Env.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFeishuNetSdk(
    options =>
    {
        options.AppId =
            Environment.GetEnvironmentVariable("FEISHU_APP_ID")
            ?? throw new InvalidOperationException(
                "FEISHU_APP_ID environment variable is not set."
            );
        options.AppSecret =
            Environment.GetEnvironmentVariable("FEISHU_APP_SECRET")
            ?? throw new InvalidOperationException(
                "FEISHU_APP_SECRET environment variable is not set."
            );
        options.VerificationToken = "2coDbVG2uErFAayFpYu5GfBaKTIKVGc3";
        options.EnableLogging = true;
        options.IgnoreStatusException = false;
    },
    opts =>
    {
        opts.HttpHost = new Uri(
            Environment.GetEnvironmentVariable("FEISHU_API_BASE_URL") ?? "https://open.feishu.cn/"
        );
        opts.JsonSerializeOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        opts.KeyValueSerializeOptions.IgnoreNullValues = true;
    }
).AddFeishuWebSocket();

builder.Services.AddSerilog(loggerConfig =>
{
    // Suppress verbose "sending request" / HTTP traffic logs by raising
    // the minimum level for common noisy namespaces to Warning.
    // Add more namespaces here if you still see outgoing-request log lines.
    loggerConfig
        .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Extensions.Http", LogEventLevel.Warning)
        .MinimumLevel.Override("FeishuNetSdk", LogEventLevel.Warning)
        .MinimumLevel.Override("OpenAI", LogEventLevel.Warning)
        .WriteTo.Console(theme: AnsiConsoleTheme.Code);
});

builder.Services.AddManInBlack(opt =>
{
    opt.ModelChoice = new ModelChoice
    {
        Provider = new OpenAIProvider()
        {
            ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "",
            BaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "",
        },
        ModelId = Environment.GetEnvironmentVariable("OPENAI_MODEL_ID") ?? "",
    };
});

builder.Services.AddAutoRegisteredServices();


var app = builder.Build();

app.MapGet("/health", () =>
{
    // returns random health status for demonstration purposes
    string[] healthyTexts = ["feeling great!", "ready to serve!", "fully operational!"];
    var random = new Random();
    var text = healthyTexts[random.Next(healthyTexts.Length)];
    return Results.Ok(new { status = "healthy", message = text });
});

app.UseFeishuEndpoint("/feishu/event/v2");

app.MapGet("/test-card", async (IServiceProvider sp, ILogger<Program> logger) =>
{
    var scope = sp.CreateScope();
    var view = scope.ServiceProvider.GetRequiredService<MyCardView>();

    var vm = view.ViewModel;

    await view.InitializeAsync();
    await view.SendToUserAsync("open_id", "ou_a1eef73df84a8a400da4010289610e2f");
    
    var markdownContent =
        """
        # Markdown 格式测试

        ## 表格

        | 名称 | 值 | 说明 |
        |------|-----|------|
        | Alpha | 100 | 示例数据 |
        | Beta | 200 | 测试数据 |
        | Gamma | 300 | 演示数据 |

        ### 系统性能监控

        | 指标 | 当前值 | 平均值 | 峰值 | 状态 |
        |------|--------|--------|------|------|
        | CPU 使用率 | 45% | 38% | 92% | 正常 |
        | 内存占用 | 6.2GB | 5.8GB | 15.4GB | 正常 |
        | 磁盘 I/O | 120MB/s | 95MB/s | 450MB/s | 正常 |
        | 网络带宽 | 340Mbps | 280Mbps | 890Mbps | 正常 |
        | GPU 温度 | 68°C | 62°C | 85°C | 警告 |
        | 进程数 | 248 | 230 | 412 | 正常 |

        ### 服务器列表

        | 服务器 | IP 地址 | 区域 | 状态 | 负载 | 延迟 |
        |--------|---------|------|------|------|------|
        | CN-East-01 | 10.0.1.10 | 华东 | 运行中 | 32% | 12ms |
        | CN-East-02 | 10.0.1.11 | 华东 | 运行中 | 67% | 15ms |
        | CN-South-01 | 10.0.2.10 | 华南 | 运行中 | 45% | 18ms |
        | CN-North-01 | 10.0.3.10 | 华北 | 维护中 | 0% | N/A |
        | US-West-01 | 10.1.1.10 | 美西 | 运行中 | 28% | 180ms |
        | EU-Central-01 | 10.2.1.10 | 欧洲 | 运行中 | 51% | 220ms |

        ### 用户增长数据

        | 月份 | 新注册 | 活跃用户 | 付费用户 | 留存率 | 收入(万) |
        |------|--------|----------|----------|--------|----------|
        | 2025-07 | 12,400 | 85,000 | 3,200 | 72% | 48.5 |
        | 2025-08 | 14,100 | 92,300 | 3,800 | 74% | 55.2 |
        | 2025-09 | 15,800 | 98,700 | 4,100 | 71% | 61.8 |
        | 2025-10 | 18,200 | 110,500 | 4,900 | 76% | 72.3 |
        | 2025-11 | 20,500 | 125,000 | 5,600 | 78% | 85.1 |
        | 2025-12 | 23,800 | 142,000 | 6,300 | 80% | 98.7 |

        ### API 接口文档

        | 端点 | 方法 | 描述 | 认证 | 限流 | 版本 |
        |------|------|------|------|------|------|
        | /api/users | GET | 获取用户列表 | Bearer | 100/min | v2 |
        | /api/users | POST | 创建用户 | Bearer | 50/min | v2 |
        | /api/users/:id | GET | 获取用户详情 | Bearer | 200/min | v2 |
        | /api/users/:id | PUT | 更新用户 | Bearer | 50/min | v2 |
        | /api/users/:id | DELETE | 删除用户 | Admin | 20/min | v2 |
        | /api/auth/login | POST | 用户登录 | 无 | 10/min | v2 |
        | /api/auth/refresh | POST | 刷新令牌 | 无 | 30/min | v2 |

        ### 订单统计

        | 订单号 | 客户 | 金额 | 商品数 | 状态 | 下单时间 |
        |--------|------|------|--------|------|----------|
        | ORD-20250701 | 张三 | ¥1,280 | 3 | 已完成 | 07-01 14:30 |
        | ORD-20250702 | 李四 | ¥560 | 1 | 配送中 | 07-02 09:15 |
        | ORD-20250703 | 王五 | ¥3,450 | 7 | 待发货 | 07-03 16:42 |
        | ORD-20250704 | 赵六 | ¥890 | 2 | 已取消 | 07-04 11:08 |
        | ORD-20250705 | 孙七 | ¥2,100 | 5 | 已完成 | 07-05 08:55 |
        | ORD-20250706 | 周八 | ¥780 | 2 | 退款中 | 07-06 20:30 |
        | ORD-20250707 | 吴九 | ¥4,620 | 8 | 待付款 | 07-07 13:20 |

        ### 错误日志汇总

        | 错误码 | 类型 | 模块 | 今日次数 | 昨日次数 | 趋势 |
        |--------|------|------|----------|----------|------|
        | E001 | 网络超时 | Gateway | 142 | 98 | ↑ |
        | E002 | 数据库死锁 | Order | 23 | 45 | ↓ |
        | E003 | 权限拒绝 | Auth | 56 | 52 | → |
        | E004 | 内存溢出 | Worker | 8 | 3 | ↑ |
        | E005 | 磁盘空间不足 | Storage | 0 | 12 | ↓ |
        | E006 | 参数校验失败 | API | 234 | 198 | ↑ |
        | E007 | 第三方服务不可用 | Payment | 17 | 5 | ↑ |
        | E008 | 序列化失败 | Queue | 31 | 28 | → |

        ### 产品功能对比

        | 功能 | 基础版 | 专业版 | 企业版 | 定制版 | 价格差异 |
        |------|--------|--------|--------|--------|----------|
        | 用户数 | 5 | 50 | 无限 | 无限 | — |
        | 存储空间 | 5GB | 100GB | 1TB | 自定义 | — |
        | API 调用 | 1K/天 | 50K/天 | 无限 | 无限 | — |
        | 技术支持 | 社区 | 工单 | 专属客服 | 专属团队 | — |
        | SLA | 99% | 99.9% | 99.99% | 自定义 | — |
        | 自定义 branding | ❌ | ❌ | ✅ | ✅ | — |
        | 私有部署 | ❌ | ❌ | ❌ | ✅ | — |

        ### 测试用例结果

        | 用例ID | 模块 | 用例名称 | 优先级 | 结果 | 耗时 |
        |--------|------|----------|--------|------|------|
        | TC-001 | 登录 | 正常登录 | P0 | ✅ 通过 | 1.2s |
        | TC-002 | 登录 | 密码错误 | P0 | ✅ 通过 | 0.8s |
        | TC-003 | 登录 | 账号锁定 | P1 | ✅ 通过 | 0.9s |
        | TC-004 | 支付 | 微信支付 | P0 | ❌ 失败 | 3.5s |
        | TC-005 | 支付 | 支付宝支付 | P0 | ✅ 通过 | 2.1s |
        | TC-006 | 支付 | 退款流程 | P1 | ✅ 通过 | 4.8s |
        | TC-007 | 搜索 | 关键词搜索 | P1 | ✅ 通过 | 1.5s |

        ### 部署版本记录

        | 版本 | 日期 | 环境 | 变更数 | 回滚 | 负责人 |
        |------|------|------|--------|------|--------|
        | v3.2.1 | 07-01 | Production | 12 | 否 | Alice |
        | v3.2.0 | 06-28 | Staging | 34 | 否 | Bob |
        | v3.1.9 | 06-25 | Production | 8 | 是 | Carol |
        | v3.1.8 | 06-22 | Production | 21 | 否 | Dave |
        | v3.1.7 | 06-18 | Staging | 15 | 否 | Eve |
        | v3.1.6 | 06-15 | Production | 42 | 否 | Frank |

        ### 编程语言排行

        | 排名 | 语言 | 评分 | 变化 | 主要领域 | 年度增长率 | 官网 |
        |------|------|------|------|----------|------------|------| 
        | 1 | Python | 100.0 | — | AI/数据 | +4.2% | [python.org](https://www.python.org) |
        | 2 | JavaScript | 98.5 | — | Web 全栈 | +1.8% | [javascript.com](https://www.javascript.com) |
        | 3 | TypeScript | 85.3 | ↑2 | Web 全栈 | +6.1% | [typescriptlang.org](https://www.typescriptlang.org) |
        | 4 | Java | 82.1 | ↓1 | 企业级 | -0.5% | [java.com](https://www.java.com) |
        | 5 | C# | 78.6 | ↑1 | 游戏/.NET | +2.3% | [dotnet.microsoft.com](https://dotnet.microsoft.com/languages/csharp) |
        | 6 | Go | 72.4 | — | 后端/云原生 | +3.7% | [go.dev](https://go.dev) |
        | 7 | Rust | 65.8 | ↑3 | 系统/底层 | +8.9% | [rust-lang.org](https://www.rust-lang.org) |
        | 8 | C++ | 63.2 | ↓2 | 系统/游戏 | -1.1% | [isocpp.org](https://isocpp.org) |

        ## 任务列表

        - [x] 已完成任务
        - [x] 另一个完成的任务
        - [ ] 未完成任务

        ## 代码块

        ```csharp
        public class Example
        {
            public string Name { get; set; } = "Hello";
        }
        ```

        ## 链接与图片

        [超链接示例](https://example.com)

        ---

        以上是常见的 Markdown 格式演示。
        """;

    // 1 秒后开始 100 次快速更新
    _ = Task.Run(async () =>
    {
        foreach (var c in markdownContent)
        {
            vm.AppendContent(c);
            await Task.Delay(10);
        }
        
        await Task.Delay(100);
    }).ContinueWith(t =>
    {
        if (t.IsFaulted)
        {
            logger.LogError(t.Exception, "Error during card updates");
        }

        scope.Dispose();
    });

    return Results.Ok(new { cardId = view.CardId, message = "Card sent, 100 updates in 1s" });
});

app.Run();


// ──────────── ViewModel ────────────

[ServiceRegister.Transient]
public class MyViewModel : ViewModelBase
{
    private readonly StringBuilder _contentBuilder = new("Initial Content");

    public string Content
    {
        get => _contentBuilder.ToString();
        set
        {
            _contentBuilder.Clear();
            _contentBuilder.Append(value);
            OnPropertyChanged();
        }
    }

    public void AppendContent(char value)
    {
        _contentBuilder.Append(value);
        OnPropertyChanged(nameof(Content));
    }

    public void AppendContent(string value)
    {
        _contentBuilder.Append(value);
        OnPropertyChanged(nameof(Content));
    }
}

// ──────────── CardView ────────────

[ServiceRegister.Transient]
public class MyCardView(MyViewModel viewModel, CardService sc, CardUpdateScheduler scheduler)
    : CardView<MyViewModel>(viewModel, sc, scheduler)
{
    protected override void Define() =>
        AddToBody(
            // BindMarkdown(vm => vm.Name),
            // Hr(),
            BindMarkdown(vm => vm.Content)
        );
}