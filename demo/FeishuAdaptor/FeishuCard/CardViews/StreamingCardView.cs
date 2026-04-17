using System.ComponentModel;
using System.Linq.Expressions;
using FeishuAdaptor.FeishuCard.Cards;

namespace FeishuAdaptor.FeishuCard.CardViews;

/// <summary>
/// 泛型 MVVM 卡片视图基类 — 通过绑定 ViewModel 属性到 CardElement，实现属性变更自动更新飞书卡片。
/// </summary>
/// <typeparam name="TViewModel">ViewModel 类型，需实现 INotifyPropertyChanged。</typeparam>
public abstract class StreamingCardView<TViewModel> : StreamingCardViewBase
    where TViewModel : INotifyPropertyChanged
{
    private readonly StreamingCard _streamingCard;
    private readonly CardUpdateScheduler _scheduler;
    private readonly List<Binder<TViewModel>> _binders = [];
    private readonly Dictionary<string, List<Binder<TViewModel>>> _propertyIndex = new();

    /// <summary>
    /// 关联的 ViewModel 实例。
    /// </summary>
    public TViewModel ViewModel { get; }

    /// <summary>
    /// 卡片在飞书端的唯一标识（InitAsync 之后可用）。
    /// </summary>
    public string CardId => _streamingCard.CardId;

    protected StreamingCardView(TViewModel vm, StreamingCard streamingCard, TimeSpan? updateInterval = null)
    {
        ViewModel = vm ?? throw new ArgumentNullException(nameof(vm));
        _streamingCard = streamingCard;
        _scheduler = new CardUpdateScheduler(streamingCard, updateInterval);

        // 调用子类的 Define() 构建卡片结构
        Define();

        // 订阅属性变更
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// 子类重写此方法，声明卡片元素和 ViewModel 属性的绑定关系。
    /// 基类已自动创建 Card 对象（含 streaming_mode=true 的 Config 和空的 Body），
    /// 子类在此方法中设置 Header、添加元素、调用 Bind 系列方法。
    /// </summary>
    protected abstract void Define();

    // ──────────── 绑定基础设施 ────────────

    /// <summary>
    /// 绑定 ViewModel 属性到 CardElement 的变更逻辑。
    /// </summary>
    /// <param name="propertyExpression">ViewModel 属性表达式，如 <c>vm => vm.Title</c>。</param>
    /// <param name="element">要绑定的卡片元素。</param>
    /// <param name="apply">从 ViewModel 读取值并应用到元素的委托。</param>
    protected Binder<TViewModel> Bind<T>(
        Expression<Func<TViewModel, T>> propertyExpression,
        CardElement element,
        Action<TViewModel, CardElement> apply
    )
    {
        var propertyName = ExtractPropertyName(propertyExpression);

        var binder = new Binder<TViewModel>
        {
            PropertyName = propertyName,
            ElementId = element.ElementId!,
            Element = element,
            Apply = vm => apply(vm, element),
        };

        _binders.Add(binder);

        if (!_propertyIndex.TryGetValue(propertyName, out var list))
        {
            list = new List<Binder<TViewModel>>();
            _propertyIndex[propertyName] = list;
        }
        list.Add(binder);

        // 应用初始值
        binder.Apply(ViewModel);

        return binder;
    }

    // ──────────── 便捷绑定方法 ────────────

    /// <summary>
    /// 创建 Markdown 元素并绑定到 ViewModel 的 string 属性，自动添加到 Card.Body。
    /// </summary>
    protected MarkdownElement BindMarkdown(
        Expression<Func<TViewModel, string>> property,
        string? textSize = null,
        string? margin = null
    )
    {
        var el = Markdown();
        if (textSize != null) el.TextSize = textSize;
        if (margin != null) el.Margin = margin;

        Bind(property, el, (vm, e) => ((MarkdownElement)e).Content = property.Compile()(vm));
        return AddToBody(el);
    }

    /// <summary>
    /// 创建 Div 文本元素并绑定到 ViewModel 的 string 属性，自动添加到 Card.Body。
    /// </summary>
    protected DivElement BindText(
        Expression<Func<TViewModel, string>> property,
        string? tag = null,
        string? margin = null
    )
    {
        var el = Text();
        if (margin != null) el.Margin = margin;
        var textTag = tag ?? "plain_text";

        Bind(property, el, (vm, e) =>
        {
            ((DivElement)e).Text = new TextElement(property.Compile()(vm)) { Tag = textTag };
        });
        return AddToBody(el);
    }

    /// <summary>
    /// 创建 Image 元素并绑定到 ViewModel 的 string 属性（img_key），自动添加到 Card.Body。
    /// </summary>
    protected ImgElement BindImage(
        Expression<Func<TViewModel, string>> property,
        string? margin = null
    )
    {
        var el = Image();
        if (margin != null) el.Margin = margin;

        Bind(property, el, (vm, e) => ((ImgElement)e).ImgKey = property.Compile()(vm));
        return AddToBody(el);
    }

    /// <summary>
    /// 添加静态 Markdown 元素（不绑定属性）到 Card.Body。
    /// </summary>
    protected MarkdownElement AddMarkdown(string content, string? textSize = null, string? margin = null)
    {
        var el = Markdown();
        el.Content = content;
        if (textSize != null) el.TextSize = textSize;
        if (margin != null) el.Margin = margin;
        return AddToBody(el);
    }

    /// <summary>
    /// 添加静态 Div 文本元素到 Card.Body。
    /// </summary>
    protected DivElement AddText(string content, string? tag = null, string? margin = null)
    {
        var el = Text();
        el.Text = new TextElement(content) { Tag = tag ?? "plain_text" };
        if (margin != null) el.Margin = margin;
        return AddToBody(el);
    }

    /// <summary>
    /// 添加分割线到 Card.Body。
    /// </summary>
    protected HrElement AddHr()
    {
        return AddToBody(Hr());
    }

    /// <summary>
    /// 添加容器元素（ColumnSet、Form 等）到 Card.Body。
    /// </summary>
    protected T AddContainer<T>(T element) where T : CardElement
    {
        return AddToBody(element);
    }

    // ──────────── PropertyChanged 处理 ────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == null)
        {
            // null 表示所有属性都变了
            foreach (var binder in _binders)
            {
                binder.Apply(ViewModel);
                _scheduler.MarkDirty(binder.ElementId, binder.Element);
            }
            return;
        }

        if (_propertyIndex.TryGetValue(e.PropertyName, out var affectedBinders))
        {
            foreach (var binder in affectedBinders)
            {
                binder.Apply(ViewModel);
                _scheduler.MarkDirty(binder.ElementId, binder.Element);
            }
        }
    }

    // ──────────── 生命周期 ────────────

    /// <summary>
    /// 在飞书端创建卡片。
    /// </summary>
    public async Task InitAsync(CancellationToken ct = default)
    {
        await _streamingCard.CreateAsync(Card, ct);
    }

    /// <summary>
    /// 将卡片以消息形式发送给指定接收者。
    /// </summary>
    public async Task SendMessageAsync(
        string receiveIdType,
        string receiveId,
        CancellationToken ct = default
    )
    {
        await _streamingCard.SendMessageAsync(receiveIdType, receiveId, ct);
    }

    /// <summary>
    /// 刷新所有待更新元素、关闭调度器、关闭流式模式、取消事件订阅。
    /// </summary>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        await _scheduler.FlushAsync(ct);
        await _scheduler.DisposeAsync();
        await _streamingCard.CloseStreamingAsync(ct);
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    // ──────────── 工具方法 ────────────

    private static string ExtractPropertyName<T>(Expression<Func<TViewModel, T>> expression)
    {
        var body = expression.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert } convert)
            body = convert.Operand;

        if (body is MemberExpression member)
            return member.Member.Name;

        throw new ArgumentException(
            $"Expression '{expression}' must be a simple property access like 'vm => vm.Property'."
        );
    }
}
