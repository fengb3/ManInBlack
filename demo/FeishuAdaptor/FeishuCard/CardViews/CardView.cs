using System.ComponentModel;
using System.Linq.Expressions;
using CommunityToolkit.Mvvm.ComponentModel;
using FeishuAdaptor.FeishuCard.Cards;

namespace FeishuAdaptor.FeishuCard.CardViews;

public abstract class ViewModelBase : ObservableObject
{
    // public string? CardId { get; set; }
}

public abstract class CardView<TViewModel>(TViewModel viewModel, CardService cardService, CardUpdateScheduler scheduler)
    : CardViewBase, IDisposable where TViewModel : ViewModelBase
{
    private readonly List<Action> _tearDownActions = [];
    private int _sequence;
    private string? _cardId;

    public string CardId =>
        _cardId ?? throw new InvalidOperationException($"Card not created yet. Call {nameof(InitializeAsync)} first.");

    public CardService CardService { get; } = cardService;

    public CardUpdateScheduler Scheduler { get; } = scheduler;

    public TViewModel ViewModel { get; } = viewModel;

    protected abstract void Define();

    private void Bind(CardElement element, Expression<Func<TViewModel, string>> expression, Func<TViewModel, string> apply)
    {
        if (expression.Body is not MemberExpression memberExpression)
        {
            throw new ArgumentException("expression should be a member expression");
            return;
        }

        // 赋初始值
        apply(ViewModel);

        var valueGetter = expression.Compile();

        ViewModel.PropertyChanged += HandlePropertyChanged;

        _tearDownActions.Add(() => ViewModel.PropertyChanged -= HandlePropertyChanged);

        return;

        void HandlePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != memberExpression.Member.Name) return;

            var newValue = valueGetter(ViewModel);

            var elementId = apply(ViewModel);

            // 提交到全局调度器，由调度器去重和限流后发送
            Scheduler.Submit(CardId, elementId, newValue, GetNextSequence());
        }
    }

    protected MarkdownElement BindMarkdown(Expression<Func<TViewModel, string>> expression)
    {
        var markdownElement = Markdown();
        Bind(markdownElement, expression, vm =>
        {
             markdownElement.Content = expression.Compile()(vm);
             return markdownElement.ElementId!;
        });
        return markdownElement;
    }

    #region 生命周期

    /// <summary>
    /// 初始化, 这一步会构建卡片内容, 并从飞书获取卡片id
    /// </summary>
    /// <param name="ct"></param>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Define();

        // create a card
        _cardId = await CardService.CreateAsync(Card, ct);
    }

    /// <summary>
    /// 把这个卡片发送给某个用户, 这一步会调用飞书接口发送消息, 消息内容包含卡片ID, 飞书客户端收到消息后会根据卡片ID展示卡片内容
    /// </summary>
    /// <param name="userIdType"></param>
    /// <param name="userId"></param>
    /// <param name="ct"></param>
    public async Task SendToUserAsync(string userIdType, string userId, CancellationToken ct = default)
    {
        await CardService.SendMessageAsync(CardId, userIdType, userId, ct);
    }

    #endregion

    public void Dispose()
    {
        _tearDownActions.ForEach(a => a());
        _tearDownActions.Clear();
    }

    private int GetNextSequence() => Interlocked.Increment(ref _sequence);
}
