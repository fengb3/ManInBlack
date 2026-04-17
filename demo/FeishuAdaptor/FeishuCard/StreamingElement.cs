using FeishuNetSdk;
using FeishuNetSdk.Cardkit;

namespace FeishuAdaptor.FeishuCard;

public class StreamingElement
{
    private readonly StreamingCard _card;
    private readonly string _elementId;
    private readonly IFeishuTenantApi _api;
    private string _accumulated = "";

    internal StreamingElement(StreamingCard card, string elementId, IFeishuTenantApi api)
    {
        _card = card;
        _elementId = elementId;
        _api = api;
    }

    public async Task AppendAsync(string content, CancellationToken ct = default)
    {
        _accumulated += content;

        await _api.PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
            _card.CardId,
            _elementId,
            new PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto
            {
                Content = _accumulated,
                Sequence = _card.GetNextSequence(),
            },
            ct
        );
    }
}
