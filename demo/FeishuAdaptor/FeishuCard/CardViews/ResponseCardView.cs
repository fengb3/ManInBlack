using CommunityToolkit.Mvvm.ComponentModel;
using FeishuAdaptor.FeishuCard.Cards;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Tools;

namespace FeishuAdaptor.FeishuCard.CardViews;

[ServiceRegister.Transient]
public partial class LlmReasoningViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Reasoning { get; set; } = "\n";
}

[ServiceRegister.Transient.As<CardView<LlmReasoningViewModel>>]
public class ReasoningCardView(
    LlmReasoningViewModel viewModel,
    CardService cardService,
    CardUpdateScheduler scheduler
) : CardView<LlmReasoningViewModel>(viewModel, cardService, scheduler)
{
    protected override void Define()
    {
        // Card.Config!.WidthMode = "300px";

        var reasoningMarkdown = BindMarkdown(vm => vm.Reasoning);
        var panel = CollapsiblePanel(builder =>
        {
            builder.Element(reasoningMarkdown);
        });
        panel.Expanded = false;
        panel.Header = new CollapsiblePanelHeader
        {
            Title = new TextElement { Content = "🤔 琢磨琢磨" },
            Icon = new CollapsiblePanelIcon { Token = "down-bold_outlined" },
            BackgroundColor = "lime-300",
            IconPosition = "right",
            IconExpandedAngle = -180,
        };
        AddToBody(panel);
    }
}

[ServiceRegister.Transient]
public partial class LlmOutputViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Output { get; set; } = "\n";
}

[ServiceRegister.Transient.As<CardView<LlmOutputViewModel>>]
public class LlmOutputCardView(
    LlmOutputViewModel viewModel,
    CardService cardService,
    CardUpdateScheduler scheduler
) : CardView<LlmOutputViewModel>(viewModel, cardService, scheduler)
{
    protected override void Define()
    {
        Card.Config!.WidthMode = "fill";
        var outputMarkdown = BindMarkdown(vm => vm.Output);
        AddToBody(outputMarkdown);
    }
}

[ServiceRegister.Transient]
public partial class LlmToolExecutionViewModel : ViewModelBase
{
    /// <summary>
    /// 用于存储工具名称，供中间件在 FRC 到达时使用
    /// </summary>
    public string ToolName { get; set; } = "";

    /// <summary>
    /// 用于存储参数，供中间件在 FRC 到达时使用
    /// </summary>
    public string Arguments { get; set; } = "";

    /// <summary>
    /// 用于存储工具执行时的附加描述（例如命令注释），并在卡片标题中显示
    /// </summary>
    public string Description { get; set; } = "";

    public string Result { get; set; } = "";
}

[ServiceRegister.Transient.As<CardView<LlmToolExecutionViewModel>>]
public partial class ToolExecutionCardView(
    LlmToolExecutionViewModel viewModel,
    CardService cardService,
    CardUpdateScheduler scheduler
) : CardView<LlmToolExecutionViewModel>(viewModel, cardService, scheduler)
{
    private int _updateSequence;
    private const string PanelElementId = "toolPanel";
    private const string ArgsMarkdownElementId = "toolArgs";
    private const string ResultMarkdownElementId = "toolResult";

    /// <summary>
    /// 工具方法名 → 中文显示名映射
    /// </summary>
    private static readonly Dictionary<string, string> ToolDisplayNameMap = new()
    {
        // CommandLineTools
        { nameof(CommandLineTools.RunBash), "💻 执行命令" },
        { nameof(CommandLineTools.GetBackgroundTaskResult), "📥 获取后台任务结果" },
        { nameof(CommandLineTools.KillBackgroundTask), "🛑 终止后台任务" },
        // FileTools
        { nameof(FileTools.Read), "📖 读取文件" },
        { nameof(FileTools.Write), "✍️ 写入文件" },
        { nameof(FileTools.Edit), "📝 更新文件" },
        { nameof(FileTools.Glob), "🔎 搜索文件" },
        { nameof(FileTools.Grep), "🔍 搜索内容" },
        // SkillTools
        { nameof(SkillTools.LoadSkill), "🧠 加载技能" },
    };

    /// <summary>
    /// 根据工具方法名获取中文显示名，未映射时返回原始名称
    /// </summary>
    private static string GetToolDisplayName(string? toolName) =>
        toolName is not null && ToolDisplayNameMap.TryGetValue(toolName, out var displayName)
            ? displayName
            : toolName ?? "未知工具";

    protected override void Define()
    {
        Card.Config!.StreamingMode = false;
        Card.Config.StreamingConfig = null;

        var argsMarkdown = Markdown(ArgsMarkdownElementId);
        argsMarkdown.Content = "";

        var panel = CollapsiblePanel(
            builder =>
            {
                builder.Element(argsMarkdown);
            },
            PanelElementId
        );
        panel.Expanded = false;
        panel.Header = new CollapsiblePanelHeader
        {
            Title = new TextElement { Content = "🔧 正在调用..." },
            Icon = new CollapsiblePanelIcon { Token = "down-bold_outlined" },
            BackgroundColor = "indigo-100",
            IconPosition = "right",
            IconExpandedAngle = -180,
        };
        AddToBody(panel);
    }

    /// <summary>
    /// 更新卡片为"工具调用中"状态 — 更新标题为工具名，更新参数显示
    /// </summary>
    public async Task UpdateForToolStartAsync(
        string toolName,
        string arguments,
        string description = "",
        CancellationToken ct = default
    )
    {
        ViewModel.ToolName = toolName;
        ViewModel.Arguments = arguments;
        ViewModel.Description = description;

        var displayName = GetToolDisplayName(toolName);
        if (!string.IsNullOrWhiteSpace(description))
        {
            displayName = $"{displayName} - {description}";
        }

        var argsMarkdown = Markdown(ArgsMarkdownElementId);
        argsMarkdown.Content = string.IsNullOrWhiteSpace(arguments) ? "无参数" : arguments;

        var panel = CollapsiblePanel(
            builder =>
            {
                builder.Element(argsMarkdown);
            },
            PanelElementId
        );
        panel.Expanded = false;
        panel.Header = new CollapsiblePanelHeader
        {
            Title = new TextElement { Content = $"{displayName} 中..." },
            Icon = new CollapsiblePanelIcon { Token = "down-bold_outlined" },
            BackgroundColor = "indigo-100",
            IconPosition = "right",
            IconExpandedAngle = -180,
        };

        var card = BuildCard(panel);
        var seq = Interlocked.Increment(ref _updateSequence);
        await CardService.FullUpdateAsync(CardId, card, seq, ct);
    }

    /// <summary>
    /// 更新卡片为"工具完成"状态 — 折叠面板默认折叠，添加结果
    /// </summary>
    /// <param name="result">工具返回结果</param>
    /// <param name="isError">工具执行是否失败</param>
    /// <param name="ct">取消令牌</param>
    public async Task UpdateForToolResultAsync(
        string result,
        bool isError = false,
        CancellationToken ct = default
    )
    {
        var displayName = GetToolDisplayName(ViewModel.ToolName);
        if (!string.IsNullOrWhiteSpace(ViewModel.Description))
        {
            displayName = $"{displayName} - {ViewModel.Description}";
        }

        var argsMarkdown = Markdown(ArgsMarkdownElementId);
        argsMarkdown.Content = string.IsNullOrWhiteSpace(ViewModel.Arguments)
            ? "无参数"
            : ViewModel.Arguments;

        var resultMarkdown = Markdown(ResultMarkdownElementId);
        resultMarkdown.Content = string.IsNullOrWhiteSpace(result) ? "无返回结果" : result;

        var panel = CollapsiblePanel(
            builder =>
            {
                builder.Element(argsMarkdown);
                builder.Hr();
                builder.Element(resultMarkdown);
            },
            PanelElementId
        );
        panel.Expanded = false;
        panel.Header = new CollapsiblePanelHeader
        {
            Title = new TextElement
            {
                Content = isError ? $"{displayName} 失败" : $"{displayName} 完成",
            },
            Icon = new CollapsiblePanelIcon { Token = "down-bold_outlined" },
            BackgroundColor = isError ? "red-100" : "green-100",
            IconPosition = "right",
            IconExpandedAngle = -180,
        };

        var card = BuildCard(panel);
        var seq = Interlocked.Increment(ref _updateSequence);
        await CardService.FullUpdateAsync(CardId, card, seq, ct);
    }

    /// <summary>
    /// 非流式卡片无需关闭流式模式，空操作
    /// </summary>
    public override Task CloseStreamingAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// 构建全量更新用的 Card 对象
    /// </summary>
    private static Card BuildCard(CollapsiblePanelElement panel)
    {
        var card = new Card
        {
            Config = new CardConfig
            {
                StreamingMode = false,
                EnableForward = true,
                EnableForwardInteraction = true,
            },
            Body = new CardBody(),
        };
        card.Body!.Elements.Add(panel);
        return card;
    }
}
