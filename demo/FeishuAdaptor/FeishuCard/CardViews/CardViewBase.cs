using FeishuAdaptor.FeishuCard.Cards;

namespace FeishuAdaptor.FeishuCard.CardViews;

/// <summary>
/// 非泛型的卡片视图基类 — 提供元素工厂方法和容器构建器。
/// </summary>
public abstract class CardViewBase : IDisposable
{
    /// <summary>
    /// 关闭卡片流式模式，由子类实现。
    /// </summary>
    public abstract Task CloseStreamingAsync(CancellationToken ct = default);

    /// <inheritdoc />
    public abstract void Dispose();
    private int _elementIdCounter;

    /// <summary>
    /// 自动创建的 Card 对象。可在 Define() 中设置 Header、Config 等顶层属性。
    /// </summary>
    public Card Card { get; } = new()
    {
        Config = new CardConfig
        {
            StreamingMode = true, 
            StreamingConfig = new CardStreamingConfig
            {
                PrintFrequencyMs = new { @default = 10 },
                PrintStep = new { @default = 2 },
                PrintStrategy = "delay",
            },
            // WidthMode = "fill",
            EnableForward = true,
            EnableForwardInteraction = true,
        },
        Body = new CardBody(),
    };

    /// <summary>
    /// 生成唯一的 element_id：字母开头，仅含字母/数字，不超过 20 字符。
    /// </summary>
    protected string NextElementId() =>
        $"e{Interlocked.Increment(ref _elementIdCounter):d8}";

    /// <summary>
    /// 将元素添加到 Card.Body.Elements 列表末尾。
    /// </summary>
    protected void AddToBody(params IEnumerable<CardElement> elements)
    {
        Card.Body!.Elements.AddRange(elements);
    }

    #region 元素工厂

    #region 内容元素工厂

    internal MarkdownElement Markdown(string? elementId = null)
    {
        return new MarkdownElement { ElementId = elementId ?? NextElementId() };
    }

    internal DivElement Text(string? elementId = null)
    {
        return new DivElement { ElementId = elementId ?? NextElementId() };
    }

    internal HrElement Hr(string? elementId = null)
    {
        return new HrElement { ElementId = elementId ?? NextElementId() };
    }

    internal ImgElement Image(string? elementId = null)
    {
        return new ImgElement { ElementId = elementId ?? NextElementId() };
    }

    internal ImgCombinationElement ImgCombination(string? elementId = null)
    {
        return new ImgCombinationElement { ElementId = elementId ?? NextElementId() };
    }

    internal TableElement Table(string? elementId = null)
    {
        return new TableElement { ElementId = elementId ?? NextElementId() };
    }

    internal ChartElement Chart(string? elementId = null)
    {
        return new ChartElement { ElementId = elementId ?? NextElementId() };
    }

    internal PersonElement Person(string? elementId = null)
    {
        return new PersonElement { ElementId = elementId ?? NextElementId() };
    }

    internal PersonListElement PersonList(string? elementId = null)
    {
        return new PersonListElement { ElementId = elementId ?? NextElementId() };
    }

    #endregion

    #region 交互元素工厂

    internal ButtonElement Button(string? elementId = null)
    {
        return new ButtonElement { ElementId = elementId ?? NextElementId() };
    }

    internal InputElement Input(string? elementId = null)
    {
        return new InputElement { ElementId = elementId ?? NextElementId() };
    }

    #endregion

    #region 容器元素工厂

    internal ColumnSetElement ColumnSet(
        Action<ColumnSetBuilder> configure,
        string? elementId = null
    )
    {
        var el = new ColumnSetElement { ElementId = elementId ?? NextElementId() };
        configure(new ColumnSetBuilder(this, el));
        return el;
    }

    internal FormElement Form(
        Action<FormBuilder> configure,
        string? elementId = null
    )
    {
        var el = new FormElement { ElementId = elementId ?? NextElementId() };
        configure(new FormBuilder(this, el));
        return el;
    }

    internal CollapsiblePanelElement CollapsiblePanel(
        Action<CollapsiblePanelBuilder> configure,
        string? elementId = null
    )
    {
        var el = new CollapsiblePanelElement { ElementId = elementId ?? NextElementId() };
        configure(new CollapsiblePanelBuilder(this, el));
        return el;
    }

    internal InteractiveContainerElement InteractiveContainer(
        Action<InteractiveContainerBuilder> configure,
        string? elementId = null
    )
    {
        var el = new InteractiveContainerElement { ElementId = elementId ?? NextElementId() };
        configure(new InteractiveContainerBuilder(this, el));
        return el;
    }

    #endregion

    #endregion
}

#region 容器构建器

public sealed class ColumnSetBuilder
{
    private readonly CardViewBase _view;
    private readonly ColumnSetElement _columnSet;

    internal ColumnSetBuilder(CardViewBase view, ColumnSetElement columnSet)
    {
        _view = view;
        _columnSet = columnSet;
    }

    public ColumnSetBuilder Column(
        Action<ColumnBuilder> configure,
        string? width = null,
        int? weight = null,
        string? verticalAlign = null,
        string? backgroundStyle = null
    )
    {
        var col = new Column
        {
            Width = width,
            Weight = weight,
            VerticalAlign = verticalAlign,
            BackgroundStyle = backgroundStyle,
        };
        _columnSet.Columns.Add(col);
        configure(new ColumnBuilder(_view, col));
        return this;
    }
}

public sealed class ColumnBuilder
{
    private readonly CardViewBase _view;
    private readonly Column _column;

    internal ColumnBuilder(CardViewBase view, Column column)
    {
        _view = view;
        _column = column;
    }

    /// <summary>
    /// 向列中添加一个元素。
    /// </summary>
    public ColumnBuilder Element(CardElement element)
    {
        _column.Elements.Add(element);
        return this;
    }

    /// <summary>
    /// 向列中添加一个文本元素。
    /// </summary>
    public ColumnBuilder Text(string? elementId = null)
    {
        _column.Elements.Add(_view.Text(elementId));
        return this;
    }

    /// <summary>
    /// 向列中添加一个 Markdown 元素。
    /// </summary>
    public ColumnBuilder Markdown(string? elementId = null)
    {
        _column.Elements.Add(_view.Markdown(elementId));
        return this;
    }

    /// <summary>
    /// 向列中添加一个分割线。
    /// </summary>
    public ColumnBuilder Hr()
    {
        _column.Elements.Add(_view.Hr());
        return this;
    }

    /// <summary>
    /// 向列中添加一个图片元素。
    /// </summary>
    public ColumnBuilder Image(string? elementId = null)
    {
        _column.Elements.Add(_view.Image(elementId));
        return this;
    }

    /// <summary>
    /// 向列中添加一个按钮元素。
    /// </summary>
    public ColumnBuilder Button(string? elementId = null)
    {
        _column.Elements.Add(_view.Button(elementId));
        return this;
    }

    /// <summary>
    /// 向列中嵌套一个分栏容器。
    /// </summary>
    public ColumnBuilder ColumnSet(Action<ColumnSetBuilder> configure, string? elementId = null)
    {
        _column.Elements.Add(_view.ColumnSet(configure, elementId));
        return this;
    }
}

public sealed class FormBuilder
{
    private readonly CardViewBase _view;
    private readonly FormElement _form;

    internal FormBuilder(CardViewBase view, FormElement form)
    {
        _view = view;
        _form = form;
    }

    public FormBuilder Element(CardElement element)
    {
        _form.Elements.Add(element);
        return this;
    }

    public FormBuilder Input(string? elementId = null)
    {
        _form.Elements.Add(_view.Input(elementId));
        return this;
    }

    public FormBuilder Button(string? elementId = null)
    {
        _form.Elements.Add(_view.Button(elementId));
        return this;
    }

    public FormBuilder Markdown(string? elementId = null)
    {
        _form.Elements.Add(_view.Markdown(elementId));
        return this;
    }

    public FormBuilder Text(string? elementId = null)
    {
        _form.Elements.Add(_view.Text(elementId));
        return this;
    }

    public FormBuilder Hr()
    {
        _form.Elements.Add(_view.Hr());
        return this;
    }
}

public sealed class InteractiveContainerBuilder
{
    private readonly CardViewBase _view;
    private readonly InteractiveContainerElement _container;

    internal InteractiveContainerBuilder(CardViewBase view, InteractiveContainerElement container)
    {
        _view = view;
        _container = container;
    }

    public InteractiveContainerBuilder Element(CardElement element)
    {
        _container.Elements.Add(element);
        return this;
    }

    public InteractiveContainerBuilder Text(string? elementId = null)
    {
        _container.Elements.Add(_view.Text(elementId));
        return this;
    }

    public InteractiveContainerBuilder Markdown(string? elementId = null)
    {
        _container.Elements.Add(_view.Markdown(elementId));
        return this;
    }

    public InteractiveContainerBuilder Hr()
    {
        _container.Elements.Add(_view.Hr());
        return this;
    }

    public InteractiveContainerBuilder Image(string? elementId = null)
    {
        _container.Elements.Add(_view.Image(elementId));
        return this;
    }

    public InteractiveContainerBuilder Button(string? elementId = null)
    {
        _container.Elements.Add(_view.Button(elementId));
        return this;
    }

    public InteractiveContainerBuilder Input(string? elementId = null)
    {
        _container.Elements.Add(_view.Input(elementId));
        return this;
    }

    public InteractiveContainerBuilder ColumnSet(Action<ColumnSetBuilder> configure, string? elementId = null)
    {
        _container.Elements.Add(_view.ColumnSet(configure, elementId));
        return this;
    }
}

public sealed class CollapsiblePanelBuilder
{
    private readonly CardViewBase _view;
    private readonly CollapsiblePanelElement _panel;

    internal CollapsiblePanelBuilder(CardViewBase view, CollapsiblePanelElement panel)
    {
        _view = view;
        _panel = panel;
    }

    public CollapsiblePanelBuilder Element(CardElement element)
    {
        _panel.Elements.Add(element);
        return this;
    }

    public CollapsiblePanelBuilder Text(string? elementId = null)
    {
        _panel.Elements.Add(_view.Text(elementId));
        return this;
    }

    public CollapsiblePanelBuilder Markdown(string? elementId = null)
    {
        _panel.Elements.Add(_view.Markdown(elementId));
        return this;
    }

    public CollapsiblePanelBuilder Hr()
    {
        _panel.Elements.Add(_view.Hr());
        return this;
    }

    public CollapsiblePanelBuilder Image(string? elementId = null)
    {
        _panel.Elements.Add(_view.Image(elementId));
        return this;
    }

    public CollapsiblePanelBuilder Button(string? elementId = null)
    {
        _panel.Elements.Add(_view.Button(elementId));
        return this;
    }

    public CollapsiblePanelBuilder ColumnSet(Action<ColumnSetBuilder> configure, string? elementId = null)
    {
        _panel.Elements.Add(_view.ColumnSet(configure, elementId));
        return this;
    }
}

#endregion