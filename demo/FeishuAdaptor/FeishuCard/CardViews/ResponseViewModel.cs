using System.Text;
using ManInBlack.AI.Core.Attributes;

namespace FeishuAdaptor.FeishuCard.CardViews;

[ServiceRegister.Transient]
public class ResponseViewModel : ViewModelBase
{
    private readonly StringBuilder _reasoningBuilder = new();
    private readonly StringBuilder _outputBuilder = new();

    public string Reasoning => _reasoningBuilder.ToString();

    public string Output => _outputBuilder.ToString();

    public void AppendReasoning(string text)
    {
        _reasoningBuilder.Append(text);
        OnPropertyChanged(nameof(Reasoning));
    }

    public void AppendOutput(string text)
    {
        _outputBuilder.Append(text);
        OnPropertyChanged(nameof(Output));
    }
}
