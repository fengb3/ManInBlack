using System.Text.Json;
using FeishuAdaptor.FeishuCard.Cards;
using FeishuAdaptor.Helper;
using FeishuNetSdk;
using FeishuNetSdk.Cardkit;
using FeishuNetSdk.Im;
using ManInBlack.AI.Abstraction.Attributes;

namespace FeishuAdaptor.FeishuCard;

[ServiceRegister.Scoped]
public class CardService(IFeishuTenantApi api, CardApiLimiter limiter)
{
    /// <summary>
    /// 创建卡片实体并获取卡片ID, 卡片ID 用于后续更新卡片
    /// </summary>
    public async Task<string> CreateAsync(Card card, CancellationToken ct = default)
    {
        await limiter.CreateCard.WaitForSlotAsync(ct);

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
        await limiter.SendMessage.WaitForSlotAsync(ct);

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
        await limiter.StreamingUpdateText.WaitForSlotAsync(ct);

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
        await limiter.ReplaceElement.WaitForSlotAsync(ct);

        var elementJson = JsonSerializer.Serialize(
            element,
            CardJsonSerializerOptions.Options
        );

        var response = await api.PutCardkitV1CardsByCardIdElementsByElementIdAsync(
            cardId,
            elementId,
            new PutCardkitV1CardsByCardIdElementsByElementIdBodyDto
            {
                Element = elementJson,
                Sequence = sequence,
            },
            ct
        );

        response.ThrowIfFeishuResponseNotSuccess();
    }

    /// <summary>
    /// 全量更新卡片实体 — 传入新的卡片 JSON 代码，覆盖更新指定卡片实体的所有内容。
    /// </summary>
    public async Task FullUpdateAsync(
        string cardId,
        Card card,
        int sequence,
        CancellationToken ct = default
    )
    {
        await limiter.FullUpdate.WaitForSlotAsync(ct);

        var response = await api.PutCardkitV1CardsByCardIdAsync(
            cardId,
            new PutCardkitV1CardsByCardIdBodyDto
            {
                Card = new PutCardkitV1CardsByCardIdBodyDto.PutCardkitV1CardsByCardIdBodyDtoCard
                {
                    Type = "card_json",
                    Data = card.ToJson(),
                },
                Sequence = sequence,
            },
            ct
        );

        response.ThrowIfFeishuResponseNotSuccess();
    }

    /// <summary>
    /// 局部更新卡片实体 — 支持同时对多个组件进行增删改等不同操作。
    /// </summary>
    /// <param name="actions">操作列表 JSON 字符串，包含 partial_update_setting、add_elements、delete_elements、partial_update_element、update_element 等操作。</param>
    public async Task BatchUpdateAsync(
        string cardId,
        string actions,
        int sequence,
        CancellationToken ct = default
    )
    {
        await limiter.BatchUpdate.WaitForSlotAsync(ct);

        var response = await api.PostCardkitV1CardsByCardIdBatchUpdateAsync(
            cardId,
            new PostCardkitV1CardsByCardIdBatchUpdateBodyDto
            {
                Actions = actions,
                Sequence = sequence,
            },
            ct
        );

        response.ThrowIfFeishuResponseNotSuccess();
    }

    /// <summary>
    /// 新增组件 — 为指定卡片实体新增组件，支持在目标组件前后插入或在末尾追加。
    /// </summary>
    /// <param name="type">添加方式：insert_before、insert_after、append</param>
    /// <param name="targetElementId">目标组件 ID（insert_before/insert_after 时必填，append 时为容器组件 ID）</param>
    /// <param name="elements">新增组件列表 JSON 字符串</param>
    public async Task AddElementsAsync(
        string cardId,
        string type,
        string? targetElementId,
        string elements,
        int sequence,
        CancellationToken ct = default
    )
    {
        await limiter.AddElements.WaitForSlotAsync(ct);

        var response = await api.PostCardkitV1CardsByCardIdElementsAsync(
            cardId,
            new PostCardkitV1CardsByCardIdElementsBodyDto
            {
                Type = type,
                TargetElementId = targetElementId ?? string.Empty,
                Elements = elements,
                Sequence = sequence,
            },
            ct
        );

        response.ThrowIfFeishuResponseNotSuccess();
    }

    /// <summary>
    /// 新增组件(重载)— 接受强类型 <see cref="CardElement"/> 列表，内部按卡片序列化约定转为 JSON。
    /// </summary>
    public Task AddElementsAsync(
        string cardId,
        string type,
        string? targetElementId,
        IEnumerable<CardElement> elements,
        int sequence,
        CancellationToken ct = default
    )
    {
        var json = JsonSerializer.Serialize(elements, CardJsonSerializerOptions.Options);
        return AddElementsAsync(cardId, type, targetElementId, json, sequence, ct);
    }

    /// <summary>
    /// 更新组件属性 — 局部更新指定组件的部分属性，不支持修改 tag 属性。
    /// </summary>
    /// <param name="partialElement">要更新的属性 JSON 字符串</param>
    public async Task PartialUpdateElementAsync(
        string cardId,
        string elementId,
        string partialElement,
        int sequence,
        CancellationToken ct = default
    )
    {
        await limiter.PartialUpdateElement.WaitForSlotAsync(ct);

        var response = await api.PatchCardkitV1CardsByCardIdElementsByElementIdAsync(
            cardId,
            elementId,
            new PatchCardkitV1CardsByCardIdElementsByElementIdBodyDto
            {
                PartialElement = partialElement,
                Sequence = sequence,
            },
            ct
        );

        response.ThrowIfFeishuResponseNotSuccess();
    }

    /// <summary>
    /// 删除组件 — 删除指定卡片实体中的组件。删除容器类组件时，容器中内嵌的组件将一并被删除。
    /// </summary>
    public async Task DeleteElementAsync(
        string cardId,
        string elementId,
        int sequence,
        CancellationToken ct = default
    )
    {
        await limiter.DeleteElement.WaitForSlotAsync(ct);

        var response = await api.DeleteCardkitV1CardsByCardIdElementsByElementIdAsync(
            cardId,
            elementId,
            new DeleteCardkitV1CardsByCardIdElementsByElementIdBodyDto
            {
                Sequence = sequence,
            },
            ct
        );

        response.ThrowIfFeishuResponseNotSuccess();
    }

    /// <summary>
    /// 关闭流式更新模式。
    /// </summary>
    public async Task CloseStreamingAsync(string cardId, int sequence, CancellationToken ct = default)
    {
        await limiter.UpdateSettings.WaitForSlotAsync(ct);

        var settingsJson = """{"config":{"streaming_mode": false}}""";

        var response = await api.PatchCardkitV1CardsByCardIdSettingsAsync(
            cardId,
            new PatchCardkitV1CardsByCardIdSettingsBodyDto
            {
                Settings = settingsJson,
                Sequence = sequence,
            },
            ct
        );

        response.ThrowIfFeishuResponseNotSuccess();
    }
}
