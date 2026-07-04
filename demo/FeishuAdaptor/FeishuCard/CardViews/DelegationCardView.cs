using CommunityToolkit.Mvvm.ComponentModel;
using FeishuAdaptor.FeishuCard.Cards;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Tools;

namespace FeishuAdaptor.FeishuCard.CardViews;

[ServiceRegister.Transient]
public partial class DelegationViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string AgentName { get; set; } = "";

    [ObservableProperty]
    public partial string Task { get; set; } = "";

    [ObservableProperty]
    public partial string Reasoning { get; set; } = "";

    [ObservableProperty]
    public partial string Output { get; set; } = "";

    [ObservableProperty]
    public partial bool IsCompleted { get; set; }
}

public record ChildToolRecord
{
    public string CallId { get; init; } = "";
    public string ToolName { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Result { get; set; }
    public bool IsError { get; set; }
}

[ServiceRegister.Transient.As<CardView<DelegationViewModel>>]
public partial class DelegationCardView(
    DelegationViewModel viewModel,
    CardService cardService,
    CardUpdateScheduler scheduler
) : CardView<DelegationViewModel>(viewModel, cardService, scheduler)
{
    private int _updateSequence;
    public List<ChildToolRecord> Tools { get; } = [];

    private static readonly Dictionary<string, string> ToolDisplayNameMap = new()
    {
        { nameof(CommandLineTools.RunBash), "💻 执行命令" },
        { nameof(FileTools.Read), "📖 读取文件" },
        { nameof(FileTools.Write), "✍️ 写入文件" },
        { nameof(FileTools.Edit), "📝 更新文件" },
        { nameof(FileTools.Glob), "🔎 搜索文件" },
        { nameof(FileTools.Grep), "🔍 搜索内容" },
    };

    private static string GetToolDisplayName(string? toolName) =>
        toolName is not null && ToolDisplayNameMap.TryGetValue(toolName, out var displayName)
            ? displayName
            : toolName ?? "未知工具";

    protected override void Define()
    {
        Card.Config!.StreamingMode = false;
        Card.Config.StreamingConfig = null;

        var panel = CollapsiblePanel(builder => { });
        panel.Expanded = false;
        panel.Header = new CollapsiblePanelHeader
        {
            Title = new TextElement { Content = "🤖 委托子 Agent 中..." },
            Icon = new CollapsiblePanelIcon { Token = "down-bold_outlined" },
            BackgroundColor = "indigo-100",
            IconPosition = "right",
            IconExpandedAngle = -180,
        };
        AddToBody(panel);
    }

    public async Task UpdateForStartAsync(string agentName, string task, CancellationToken ct = default)
    {
        ViewModel.AgentName = agentName;
        ViewModel.Task = task;
        await FlushAsync(ct);
    }

    public async Task AppendReasoningAsync(string text, CancellationToken ct = default)
    {
        ViewModel.Reasoning += text;
        // 文本流式事件密集，由调用方在结构事件时统一 FlushAsync
        await Task.CompletedTask;
    }

    public async Task AppendOutputAsync(string text, CancellationToken ct = default)
    {
        ViewModel.Output += text;
        await Task.CompletedTask;
    }

    public async Task AddChildToolStartAsync(string callId, string toolName, string args, string description = "", CancellationToken ct = default)
    {
        Tools.Add(new ChildToolRecord { CallId = callId, ToolName = toolName, Arguments = args, Description = description });
        await FlushAsync(ct);
    }

    public async Task UpdateChildToolResultAsync(string callId, string result, bool isError, CancellationToken ct = default)
    {
        var tool = Tools.FirstOrDefault(t => t.CallId == callId);
        if (tool is not null)
        {
            tool.Result = result;
            tool.IsError = isError;
        }
        await FlushAsync(ct);
    }

    public async Task UpdateForCompletedAsync(CancellationToken ct = default)
    {
        ViewModel.IsCompleted = true;
        await FlushAsync(ct);
    }

    public override Task CloseStreamingAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task FlushAsync(CancellationToken ct = default)
    {
        var card = BuildCard();
        var seq = Interlocked.Increment(ref _updateSequence);
        await CardService.FullUpdateAsync(CardId, card, seq, ct);
    }

    private Card BuildCard()
    {
        var vm = ViewModel;
        var isCompleted = vm.IsCompleted;
        var statusText = isCompleted ? "完成" : "中...";

        var panel = CollapsiblePanel(builder =>
        {
            // 任务描述
            if (!string.IsNullOrWhiteSpace(vm.Task))
            {
                var taskMd = Markdown();
                taskMd.Content = $"**📋 任务:** {Truncate(vm.Task, 200)}";
                builder.Element(taskMd);
            }

            // 推理过程（折叠）
            if (!string.IsNullOrWhiteSpace(vm.Reasoning))
            {
                var reasoningMd = Markdown();
                reasoningMd.Content = vm.Reasoning;
                var reasoningPanel = CollapsiblePanel(b => b.Element(reasoningMd));
                reasoningPanel.Expanded = false;
                reasoningPanel.Header = new CollapsiblePanelHeader
                {
                    Title = new TextElement { Content = "🤔 推理过程" },
                    Icon = new CollapsiblePanelIcon { Token = "down-bold_outlined" },
                    BackgroundColor = "lime-300",
                    IconPosition = "right",
                    IconExpandedAngle = -180,
                };
                builder.Element(reasoningPanel);
            }

            // 文本输出
            if (!string.IsNullOrWhiteSpace(vm.Output))
            {
                var outputMd = Markdown();
                outputMd.Content = vm.Output;
                builder.Element(outputMd);
            }

            // 子工具调用
            foreach (var tool in Tools)
            {
                var displayName = GetToolDisplayName(tool.ToolName);
                if (!string.IsNullOrWhiteSpace(tool.Description))
                    displayName = $"{displayName} - {tool.Description}";
                var toolStatus = tool.Result is not null
                    ? (tool.IsError ? "失败" : "完成")
                    : "中...";

                var toolMd = Markdown();
                var content = $"**参数:**\n```\n{Truncate(tool.Arguments, 300)}\n```";
                if (tool.Result is not null)
                {
                    content += $"\n\n**结果:**\n```\n{Truncate(tool.Result, 500)}\n```";
                }
                toolMd.Content = content;

                var toolPanel = CollapsiblePanel(b => b.Element(toolMd));
                toolPanel.Expanded = false;
                var bgColor = tool.Result is null
                    ? "indigo-100"
                    : tool.IsError ? "red-100" : "green-100";
                toolPanel.Header = new CollapsiblePanelHeader
                {
                    Title = new TextElement { Content = $"{displayName} {toolStatus}" },
                    Icon = new CollapsiblePanelIcon { Token = "down-bold_outlined" },
                    BackgroundColor = bgColor,
                    IconPosition = "right",
                    IconExpandedAngle = -180,
                };
                builder.Element(toolPanel);
            }
        });

        panel.Expanded = false;
        var headerBg = isCompleted ? "green-100" : "indigo-100";
        panel.Header = new CollapsiblePanelHeader
        {
            Title = new TextElement
            {
                Content = $"🤖 委托 {vm.AgentName} {statusText}",
            },
            Icon = new CollapsiblePanelIcon { Token = "down-bold_outlined" },
            BackgroundColor = headerBg,
            IconPosition = "right",
            IconExpandedAngle = -180,
        };

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

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength), "\n...");
}
