using System.Text.Json;
using FeishuAdaptor.FeishuCard.Cards;
using FeishuAdaptor.Helper;
using FeishuNetSdk;
using FeishuNetSdk.Cardkit;
using FeishuNetSdk.Im;
using ManInBlack.AI.Core.Attributes;

namespace FeishuAdaptor.FeishuCard;

[ServiceRegister.Transient]
public class StreamingCard
{
    private readonly IFeishuTenantApi _api;
    private int _sequence;
    private string? _cardId;

    public string CardId =>
        _cardId ?? throw new InvalidOperationException($"Card not created yet. Call {nameof(CreateAsync)} first.");

    /// <summary>从 Card 对象创建</summary>
    public StreamingCard(IFeishuTenantApi api)
    {
        _api = api;
        _sequence = 1;
    }

    public async Task CreateAsync(Card card, CancellationToken ct = default)
    {
        var result = await _api.PostCardkitV1CardsAsync(
            new PostCardkitV1CardsBodyDto { Type = "card_json", Data = card.ToJson() },
            ct
        );

        if (result.Code != 0)
            throw new InvalidOperationException(
                $"Failed to create card: code={result.Code}, msg={result.Msg}"
            );

        _cardId = result.Data!.CardId;
    }

    public async Task SendMessageAsync(
        string receiveIdType,
        string receiveId,
        CancellationToken ct = default
    )
    {
        var msgContent = JsonSerializer.Serialize(
            new { type = "card", data = new { card_id = CardId } }
        );

        var result = await _api.PostImV1MessagesAsync(
            receiveIdType,
            new PostImV1MessagesBodyDto
            {
                ReceiveId = receiveId,
                MsgType = "interactive",
                Content = msgContent,
            },
            ct
        );

        if (result.Code != 0)
            throw new InvalidOperationException(
                $"Failed to send message: code={result.Code}, msg={result.Msg}"
            );
    }

    /// <summary>
    /// 部分更新卡片元素 — 只修改传入的字段，未传入的字段保持不变。
    /// </summary>
    public async Task PatchElementAsync(
        string elementId,
        string newContent,
        CancellationToken ct = default
    )
    {
        var response = await _api.PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
            CardId,
            elementId,
            new PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto
            {
                Content = newContent,
                Sequence = GetNextSequence(),
            },
            ct
        );

        response.ThrowIfFeishuResponseNotSuccess();
    }

    /// <summary>
    /// 全量替换卡片元素 — 用新的元素完全替换指定 elementId 的元素。
    /// </summary>
    public async Task ReplaceElementAsync(
        string elementId,
        CardElement element,
        CancellationToken ct = default
    )
    {
        var elementJson = JsonSerializer.Serialize(
            element,
            CardJsonSerializerOptions.Options
        );

        await _api.PutCardkitV1CardsByCardIdElementsByElementIdAsync(
            CardId,
            elementId,
            new PutCardkitV1CardsByCardIdElementsByElementIdBodyDto
            {
                Element = elementJson,
                Sequence = GetNextSequence(),
            },
            ct
        );
    }

    public async Task CloseStreamingAsync(CancellationToken ct = default)
    {
        var settingsJson = """{"streaming_mode": false}""";

        await _api.PatchCardkitV1CardsByCardIdSettingsAsync(
            CardId,
            new PatchCardkitV1CardsByCardIdSettingsBodyDto
            {
                Settings = settingsJson,
                Sequence = _sequence,
            },
            ct
        );
    }

    internal int GetNextSequence()
    {
        return Interlocked.Increment(ref _sequence) - 1;
    }
}