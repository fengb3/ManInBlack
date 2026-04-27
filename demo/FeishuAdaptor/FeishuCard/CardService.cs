using System.Text.Json;
using FeishuAdaptor.FeishuCard.Cards;
using FeishuAdaptor.Helper;
using FeishuNetSdk;
using FeishuNetSdk.Cardkit;
using FeishuNetSdk.Im;
using ManInBlack.AI.Abstraction.Attributes;

namespace FeishuAdaptor.FeishuCard;

[ServiceRegister.Scoped]
public class CardService(IFeishuTenantApi api)
{
    /// <summary>
    /// 创建卡片实体并获取卡片ID, 卡片ID 用于后续更新卡片
    /// </summary>
    public async Task<string> CreateAsync(Card card, CancellationToken ct = default)
    {
        var result = await api.PostCardkitV1CardsAsync(
            new PostCardkitV1CardsBodyDto { Type = "card_json", Data = card.ToJson() },
            ct
        );

        result.ThrowIfFeishuResponseNotSuccess();

        return result.Data!.CardId;
    }

    public async Task SendMessageAsync(
        string cardId,
        string receiveIdType,
        string receiveId,
        CancellationToken ct = default
    )
    {
        var msgContent = JsonSerializer.Serialize(
            new { type = "card", data = new { card_id = cardId } }
        );

        var result = await api.PostImV1MessagesAsync(
            receiveIdType,
            new PostImV1MessagesBodyDto
            {
                ReceiveId = receiveId,
                MsgType = "interactive",
                Content = msgContent,
            },
            ct
        );

        result.ThrowIfFeishuResponseNotSuccess();
    }

    /// <summary>
    /// 流式更新卡片文本, 传入卡片中指定元素的 elementId 和新的文本内容，API 将使用新的文本内容更新该元素，并保留其他属性不变。
    /// </summary>
    public async Task UpdateElementStreamAsync(
        string cardId,
        string elementId,
        string newContent,
        int sequence,
        CancellationToken ct = default
    )
    {
        var response = await api.PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
            cardId,
            elementId,
            new PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto
            {
                Content = newContent,
                Sequence = sequence,
            },
            ct
        );

        response.ThrowIfFeishuResponseNotSuccess();
    }

    /// <summary>
    /// 全量替换卡片元素 — 用新的元素完全替换指定 elementId 的元素。
    /// </summary>
    public async Task ReplaceElementAsync(
        string cardId,
        string elementId,
        CardElement element,
        int sequence,
        CancellationToken ct = default
    )
    {
        var elementJson = JsonSerializer.Serialize(
            element,
            CardJsonSerializerOptions.Options
        );

        await api.PutCardkitV1CardsByCardIdElementsByElementIdAsync(
            cardId,
            elementId,
            new PutCardkitV1CardsByCardIdElementsByElementIdBodyDto
            {
                Element = elementJson,
                Sequence = sequence,
            },
            ct
        );
    }

    public async Task CloseStreamingAsync(string cardId, int sequence, CancellationToken ct = default)
    {
        var settingsJson = """{"streaming_mode": false}""";

        await api.PatchCardkitV1CardsByCardIdSettingsAsync(
            cardId,
            new PatchCardkitV1CardsByCardIdSettingsBodyDto
            {
                Settings = settingsJson,
                Sequence = sequence,
            },
            ct
        );
    }
}