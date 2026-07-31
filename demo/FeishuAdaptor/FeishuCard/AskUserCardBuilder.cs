using FeishuAdaptor.FeishuCard.Cards;
using FeishuAdaptor.Tools;

namespace FeishuAdaptor.FeishuCard;

/// <summary>
/// 构造 AskUser 的交互卡片。
/// 单选：问题 markdown + 每个选项一个按钮（CallbackBehavior.Value 带 requestId + option）。
/// 多选：form 容器内含问题 markdown + multi_select_static(name=opts) + 提交按钮（Value 带 requestId）。
/// </summary>
public static class AskUserCardBuilder
{
    public static Card Build(string question, IReadOnlyList<AskUserOption> options, bool multiSelect, string requestId)
        => multiSelect ? BuildMulti(question, options, requestId) : BuildSingle(question, options, requestId);

    private static Card MakeCard(params CardElement[] bodyElements) => new()
    {
        Header = new CardHeader { Title = new TextElement("需要你的选择") },
        Body = new CardBody { Elements = bodyElements.ToList() },
    };

    private static MarkdownElement QuestionMarkdown(string question) => new()
    {
        Content = question,
        TextAlign = "left",
    };

    private static Card BuildSingle(string question, IReadOnlyList<AskUserOption> options, string requestId)
    {
        var elements = new List<CardElement> { QuestionMarkdown(question) };
        for (var i = 0; i < options.Count; i++)
        {
            var opt = options[i];
            elements.Add(new ButtonElement
            {
                Text = new TextElement(opt.Label),
                Type = i == 0 ? "primary" : "default",
                Behaviors =
                {
                    new CallbackBehavior
                    {
                        Value = new Dictionary<string, object>
                        {
                            ["requestId"] = requestId,
                            ["option"] = opt.Value ?? opt.Label,
                        },
                    },
                },
            });
        }
        return MakeCard(elements.ToArray());
    }

    private static Card BuildMulti(string question, IReadOnlyList<AskUserOption> options, string requestId)
    {
        var select = new MultiSelectStaticElement
        {
            Name = "opts",
            Placeholder = new TextElement("请选择"),
            Options = options
                .Select(o => new SelectOption
                {
                    Text = new TextElement(o.Label),
                    Value = o.Value ?? o.Label,
                })
                .ToList(),
        };
        var submit = new ButtonElement
        {
            Text = new TextElement("提交"),
            Type = "primary",
            FormActionType = "submit",
            Behaviors =
            {
                new CallbackBehavior
                {
                    Value = new Dictionary<string, object> { ["requestId"] = requestId },
                },
            },
        };
        var form = new FormElement
        {
            Name = "askUserForm",
            Elements = { QuestionMarkdown(question), select, submit },
        };
        return MakeCard(form);
    }
}
