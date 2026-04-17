using System.ComponentModel;
using System.Linq.Expressions;
using CommunityToolkit.Mvvm.ComponentModel;
using FeishuAdaptor.FeishuCard.Cards;
using FeishuNetSdk;

namespace FeishuAdaptor.FeishuCard.CardViews;

public abstract class ViewModelBase : ObservableObject
{
    // public string? CardId { get; set; }
}

public abstract class CardView<TViewModel>(TViewModel viewModel, StreamingCard streamingCard)
    : CardViewBase, IDisposable where TViewModel : ViewModelBase
{
    private readonly List<Action> _tearDownActions = [];

    public StreamingCard StreamingCard { get; } = streamingCard;

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

            StreamingCard.PatchElementAsync(elementId, newValue).Wait();

            // TODO
            Console.WriteLine(
                $"should update Card id: {StreamingCard.CardId}, element id: {element.ElementId} 's content to {newValue} ,,, because {e.PropertyName} changed");
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

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Define();

        // create a card 
        await StreamingCard.CreateAsync(Card, ct);
    }

    public async Task SendToUserAsync(string userIdType, string userId, CancellationToken ct = default)
    {
        await StreamingCard.SendMessageAsync(userIdType, userId, ct);
    }

    #endregion

    public void Dispose()
    {
        _tearDownActions.ForEach(a => a());
        _tearDownActions.Clear();
    }
}