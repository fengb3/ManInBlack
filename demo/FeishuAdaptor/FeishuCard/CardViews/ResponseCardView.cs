using CommunityToolkit.Mvvm.ComponentModel;
using FeishuAdaptor.FeishuCard.Cards;
using ManInBlack.AI.Abstraction.Attributes;

namespace FeishuAdaptor.FeishuCard.CardViews;

[ServiceRegister.Transient]
public partial class LlmReasoningViewModel : ViewModelBase
{
    [ObservableProperty] public partial string Reasoning { get; set; } = "\n";
}

[ServiceRegister.Transient.As<CardView<LlmReasoningViewModel>>]
public class ReasoningCardView(LlmReasoningViewModel viewModel, CardService cardService, CardUpdateScheduler scheduler)
    : CardView<LlmReasoningViewModel>(viewModel, cardService, scheduler)
{
    protected override void Define()
    {
        // Card.Config!.WidthMode = "300px";
        
        var reasoningMarkdown = BindMarkdown(vm => vm.Reasoning);
        var panel = CollapsiblePanel(builder => { builder.Element(reasoningMarkdown); });
        panel.Expanded = false;
        panel.Header = new CollapsiblePanelHeader
        {
            Title = new TextElement { Content = "🤔琢磨琢磨" },
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
    [ObservableProperty] public partial string Output { get; set; } = "\n";
}

[ServiceRegister.Transient.As<CardView<LlmOutputViewModel>>]
public class LlmOutputCardView(LlmOutputViewModel viewModel, CardService cardService, CardUpdateScheduler scheduler)
    : CardView<LlmOutputViewModel>(viewModel, cardService, scheduler)
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
    [ObservableProperty] public partial string ToolName { get; set; } = "\n";
    [ObservableProperty] public partial string Arguments { get; set; } = "\n";
    [ObservableProperty] public partial string Result { get; set; } = "\n";
}

[ServiceRegister.Transient.As<CardView<LlmToolExecutionViewModel>>]
public partial class ToolExecutionCardView(
    LlmToolExecutionViewModel viewModel,
    CardService cardService,
    CardUpdateScheduler scheduler)
    : CardView<LlmToolExecutionViewModel>(viewModel, cardService, scheduler)
{
    protected override void Define()
    {
        // Card.Config!.StreamingConfig!.PrintFrequencyMs = 100;
        // Card.Config!.StreamingConfig!.PrintStep = 100;

        // Card.Config!.WidthMode = "300px";
        
        var toolNameText = BindMarkdown(vm => vm.ToolName);

        var argumentsText = BindMarkdown(vm => vm.Arguments);

        var resultText = BindMarkdown(vm => vm.Result);

        var panel = CollapsiblePanel(builder =>
        {
            builder.Element(toolNameText);
            builder.Element(argumentsText);
            builder.Element(resultText);
        });
        panel.Expanded = false;
        panel.Header = new CollapsiblePanelHeader
        {
            Title = new TextElement { Content = "🔧工具执行", TextColor = "orange-700"},
            Icon = new CollapsiblePanelIcon { Token = "down-bold_outlined" },
            BackgroundColor = "indigo-100",
            IconPosition = "right",
            IconExpandedAngle = -180,
        };
        AddToBody(panel);
    }
}