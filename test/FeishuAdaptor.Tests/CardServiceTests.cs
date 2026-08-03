using FeishuAdaptor.FeishuCard;
using FeishuAdaptor.FeishuCard.Cards;
using FeishuAdaptor.Helper;
using FeishuNetSdk;
using FeishuNetSdk.Cardkit;
using FeishuNetSdk.Im;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FeishuAdaptor.Tests;

/// <summary>
/// CardService 单元测试 — 验证每个方法正确调用限流器 + API。
/// </summary>
public class CardServiceTests
{
    private readonly IFeishuTenantApi _api;
    private readonly CardApiLimiter _limiter;
    private readonly CardService _sut;

    public CardServiceTests()
    {
        _api = Substitute.For<IFeishuTenantApi>();
        _limiter = new CardApiLimiter();
        _sut = new CardService(_api, _limiter, NullLogger<CardService>.Instance);
    }

    /// <summary>
    /// 构造一个成功的 FeishuResponse（非泛型），用于不返回 Data 的 API 方法。
    /// </summary>
    private static FeishuResponse CreateSuccessResponse()
    {
        return new FeishuResponse { Code = 0, Msg = "ok" };
    }

    /// <summary>
    /// 构造一个成功的 FeishuResponse&lt;T&gt;，用于返回 Data 的 API 方法。
    /// </summary>
    private static FeishuResponse<T> CreateSuccessResponse<T>(T? data = default)
    {
        return new FeishuResponse<T> { Code = 0, Msg = "ok", Data = data };
    }

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_应调用限流器并返回CardId()
    {
        // Arrange
        var card = new Card();
        var expectedCardId = "test-card-id-123";
        _api.PostCardkitV1CardsAsync(
                Arg.Any<PostCardkitV1CardsBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse(new PostCardkitV1CardsResponseDto { CardId = expectedCardId }));

        // Act
        var result = await _sut.CreateAsync(card);

        // Assert
        Assert.Equal(expectedCardId, result);
        await _api.Received(1).PostCardkitV1CardsAsync(
            Arg.Any<PostCardkitV1CardsBodyDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_应传递正确的CardJson()
    {
        // Arrange
        var card = new Card { Schema = "2.0" };
        var expectedJson = card.ToJson();
        PostCardkitV1CardsBodyDto? capturedDto = null;

        _api.PostCardkitV1CardsAsync(
                Arg.Do<PostCardkitV1CardsBodyDto>(dto => capturedDto = dto),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse(new PostCardkitV1CardsResponseDto { CardId = "card-id" }));

        // Act
        await _sut.CreateAsync(card);

        // Assert
        Assert.NotNull(capturedDto);
        Assert.Equal("card_json", capturedDto.Type);
        Assert.Equal(expectedJson, capturedDto.Data);
    }

    [Fact]
    public async Task CreateAsync_API返回失败时应抛出异常()
    {
        // Arrange
        var card = new Card();
        _api.PostCardkitV1CardsAsync(
                Arg.Any<PostCardkitV1CardsBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(new FeishuResponse<PostCardkitV1CardsResponseDto>
                { Code = 99999, Msg = "error" });

        // Act & Assert
        await Assert.ThrowsAsync<FeishuRequestException>(() => _sut.CreateAsync(card));
    }

    #endregion

    #region SendMessageAsync

    [Fact]
    public async Task SendMessageAsync_应调用限流器和API()
    {
        // Arrange
        var cardId = "card-001";
        var receiveIdType = "open_id";
        var receiveId = "ou_xxx";

        _api.PostImV1MessagesAsync(
                Arg.Any<string>(),
                Arg.Any<PostImV1MessagesBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse(new PostImV1MessagesResponseDto()));

        // Act
        await _sut.SendMessageAsync(cardId, receiveIdType, receiveId);

        // Assert
        await _api.Received(1).PostImV1MessagesAsync(
            receiveIdType,
            Arg.Any<PostImV1MessagesBodyDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendMessageAsync_应传递正确的消息内容()
    {
        // Arrange
        var cardId = "card-002";
        PostImV1MessagesBodyDto? capturedDto = null;

        _api.PostImV1MessagesAsync(
                Arg.Do<string>(_ => { }),
                Arg.Do<PostImV1MessagesBodyDto>(dto => capturedDto = dto),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse(new PostImV1MessagesResponseDto()));

        // Act
        await _sut.SendMessageAsync(cardId, "chat_id", "oc_xxx");

        // Assert
        Assert.NotNull(capturedDto);
        Assert.Equal("oc_xxx", capturedDto.ReceiveId);
        Assert.Equal("interactive", capturedDto.MsgType);
        Assert.Contains(cardId, capturedDto.Content);
    }

    [Fact]
    public async Task SendMessageAsync_API返回失败时应抛出异常()
    {
        // Arrange
        _api.PostImV1MessagesAsync(
                Arg.Any<string>(),
                Arg.Any<PostImV1MessagesBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(new FeishuResponse<PostImV1MessagesResponseDto>
                { Code = 99999, Msg = "error" });

        // Act & Assert
        await Assert.ThrowsAsync<FeishuRequestException>(
            () => _sut.SendMessageAsync("card-001", "open_id", "ou_xxx"));
    }

    #endregion

    #region UpdateElementStreamAsync

    [Fact]
    public async Task UpdateElementStreamAsync_应调用限流器和API()
    {
        // Arrange
        var cardId = "card-stream";
        var elementId = "elem-001";
        var content = "Hello World";
        var sequence = 1;

        _api.PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());

        // Act
        await _sut.UpdateElementStreamAsync(cardId, elementId, content, sequence);

        // Assert
        await _api.Received(1).PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
            cardId,
            elementId,
            Arg.Any<PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateElementStreamAsync_应传递正确的内容和Sequence()
    {
        // Arrange
        PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto? capturedDto = null;

        _api.PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Do<PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto>(dto => capturedDto = dto),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());

        // Act
        await _sut.UpdateElementStreamAsync("card-1", "elem-1", "新内容", 42);

        // Assert
        Assert.NotNull(capturedDto);
        Assert.Equal("新内容", capturedDto.Content);
        Assert.Equal(42, capturedDto.Sequence);
    }

    #endregion

    #region ReplaceElementAsync

    [Fact]
    public async Task ReplaceElementAsync_应调用限流器和API()
    {
        // Arrange
        var cardId = "card-replace";
        var elementId = "elem-replace";
        var element = new MarkdownElement();
        var sequence = 5;

        _api.PutCardkitV1CardsByCardIdElementsByElementIdAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<PutCardkitV1CardsByCardIdElementsByElementIdBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());

        // Act
        await _sut.ReplaceElementAsync(cardId, elementId, element, sequence);

        // Assert
        await _api.Received(1).PutCardkitV1CardsByCardIdElementsByElementIdAsync(
            cardId,
            elementId,
            Arg.Any<PutCardkitV1CardsByCardIdElementsByElementIdBodyDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplaceElementAsync_应传递正确的ElementJson和Sequence()
    {
        // Arrange
        var element = new MarkdownElement();
        PutCardkitV1CardsByCardIdElementsByElementIdBodyDto? capturedDto = null;

        _api.PutCardkitV1CardsByCardIdElementsByElementIdAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Do<PutCardkitV1CardsByCardIdElementsByElementIdBodyDto>(dto => capturedDto = dto),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());

        // Act
        await _sut.ReplaceElementAsync("card-1", "elem-1", element, 3);

        // Assert — 验证序列化后的 JSON 包含 tag=markdown（snake_case 命名策略）
        Assert.NotNull(capturedDto);
        Assert.Contains("markdown", capturedDto.Element);
        Assert.Equal(3, capturedDto.Sequence);
    }

    #endregion

    #region FullUpdateAsync

    [Fact]
    public async Task FullUpdateAsync_应调用限流器和API()
    {
        // Arrange
        var cardId = "card-full";
        var card = new Card();
        var sequence = 10;

        _api.PutCardkitV1CardsByCardIdAsync(
                Arg.Any<string>(),
                Arg.Any<PutCardkitV1CardsByCardIdBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());

        // Act
        await _sut.FullUpdateAsync(cardId, card, sequence);

        // Assert
        await _api.Received(1).PutCardkitV1CardsByCardIdAsync(
            cardId,
            Arg.Any<PutCardkitV1CardsByCardIdBodyDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FullUpdateAsync_应传递正确的卡片内容和Sequence()
    {
        // Arrange
        var card = new Card();
        var expectedJson = card.ToJson();
        PutCardkitV1CardsByCardIdBodyDto? capturedDto = null;

        _api.PutCardkitV1CardsByCardIdAsync(
                Arg.Any<string>(),
                Arg.Do<PutCardkitV1CardsByCardIdBodyDto>(dto => capturedDto = dto),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());

        // Act
        await _sut.FullUpdateAsync("card-1", card, 7);

        // Assert
        Assert.NotNull(capturedDto);
        Assert.Equal("card_json", capturedDto.Card.Type);
        Assert.Equal(expectedJson, capturedDto.Card.Data);
        Assert.Equal(7, capturedDto.Sequence);
    }

    #endregion

    #region BatchUpdateAsync

    [Fact]
    public async Task BatchUpdateAsync_应调用限流器和API()
    {
        // Arrange
        var cardId = "card-batch";
        var actions = """{"partial_update_element":[]}""";
        var sequence = 2;

        _api.PostCardkitV1CardsByCardIdBatchUpdateAsync(
                Arg.Any<string>(),
                Arg.Any<PostCardkitV1CardsByCardIdBatchUpdateBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());

        // Act
        await _sut.BatchUpdateAsync(cardId, actions, sequence);

        // Assert
        await _api.Received(1).PostCardkitV1CardsByCardIdBatchUpdateAsync(
            cardId,
            Arg.Any<PostCardkitV1CardsByCardIdBatchUpdateBodyDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BatchUpdateAsync_应传递正确的Actions和Sequence()
    {
        // Arrange
        var actions = """{"add_elements":[]}""";
        PostCardkitV1CardsByCardIdBatchUpdateBodyDto? capturedDto = null;

        _api.PostCardkitV1CardsByCardIdBatchUpdateAsync(
                Arg.Any<string>(),
                Arg.Do<PostCardkitV1CardsByCardIdBatchUpdateBodyDto>(dto => capturedDto = dto),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());

        // Act
        await _sut.BatchUpdateAsync("card-1", actions, 5);

        // Assert
        Assert.NotNull(capturedDto);
        Assert.Equal(actions, capturedDto.Actions);
        Assert.Equal(5, capturedDto.Sequence);
    }

    #endregion

    #region AddElementsAsync

    [Fact]
    public async Task AddElementsAsync_应调用限流器和API()
    {
        // Arrange
        var cardId = "card-add";
        var type = "append";
        var targetElementId = "elem-target";
        var elements = """[{"tag":"div"}]""";
        var sequence = 3;

        _api.PostCardkitV1CardsByCardIdElementsAsync(
                Arg.Any<string>(),
                Arg.Any<PostCardkitV1CardsByCardIdElementsBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());

        // Act
        await _sut.AddElementsAsync(cardId, type, targetElementId, elements, sequence);

        // Assert
        await _api.Received(1).PostCardkitV1CardsByCardIdElementsAsync(
            cardId,
            Arg.Any<PostCardkitV1CardsByCardIdElementsBodyDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddElementsAsync_targetElementId为null时应传空字符串()
    {
        // Arrange
        PostCardkitV1CardsByCardIdElementsBodyDto? capturedDto = null;

        _api.PostCardkitV1CardsByCardIdElementsAsync(
                Arg.Any<string>(),
                Arg.Do<PostCardkitV1CardsByCardIdElementsBodyDto>(dto => capturedDto = dto),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());

        // Act
        await _sut.AddElementsAsync("card-1", "append", null, "[]", 1);

        // Assert
        Assert.NotNull(capturedDto);
        Assert.Equal(string.Empty, capturedDto.TargetElementId);
    }

    [Fact]
    public async Task AddElementsAsync_应传递正确的参数()
    {
        // Arrange
        PostCardkitV1CardsByCardIdElementsBodyDto? capturedDto = null;

        _api.PostCardkitV1CardsByCardIdElementsAsync(
                Arg.Any<string>(),
                Arg.Do<PostCardkitV1CardsByCardIdElementsBodyDto>(dto => capturedDto = dto),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());

        // Act
        await _sut.AddElementsAsync("card-1", "insert_before", "elem-1", """[{"tag":"div"}]""", 4);

        // Assert
        Assert.NotNull(capturedDto);
        Assert.Equal("insert_before", capturedDto.Type);
        Assert.Equal("elem-1", capturedDto.TargetElementId);
        Assert.Equal("""[{"tag":"div"}]""", capturedDto.Elements);
        Assert.Equal(4, capturedDto.Sequence);
    }

    #endregion

    #region PartialUpdateElementAsync

    [Fact]
    public async Task PartialUpdateElementAsync_应调用限流器和API()
    {
        // Arrange
        var cardId = "card-partial";
        var elementId = "elem-partial";
        var partialElement = """{"text":{"tag":"plain_text","content":"更新后"}}""";
        var sequence = 6;

        _api.PatchCardkitV1CardsByCardIdElementsByElementIdAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<PatchCardkitV1CardsByCardIdElementsByElementIdBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());

        // Act
        await _sut.PartialUpdateElementAsync(cardId, elementId, partialElement, sequence);

        // Assert
        await _api.Received(1).PatchCardkitV1CardsByCardIdElementsByElementIdAsync(
            cardId,
            elementId,
            Arg.Any<PatchCardkitV1CardsByCardIdElementsByElementIdBodyDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PartialUpdateElementAsync_应传递正确的PartialElement和Sequence()
    {
        // Arrange
        var partialElement = """{"text":{"tag":"plain_text","content":"更新后"}}""";
        PatchCardkitV1CardsByCardIdElementsByElementIdBodyDto? capturedDto = null;

        _api.PatchCardkitV1CardsByCardIdElementsByElementIdAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Do<PatchCardkitV1CardsByCardIdElementsByElementIdBodyDto>(dto => capturedDto = dto),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());

        // Act
        await _sut.PartialUpdateElementAsync("card-1", "elem-1", partialElement, 8);

        // Assert
        Assert.NotNull(capturedDto);
        Assert.Equal(partialElement, capturedDto.PartialElement);
        Assert.Equal(8, capturedDto.Sequence);
    }

    #endregion

    #region DeleteElementAsync

    [Fact]
    public async Task DeleteElementAsync_应调用限流器和API()
    {
        // Arrange
        var cardId = "card-delete";
        var elementId = "elem-delete";
        var sequence = 9;

        _api.DeleteCardkitV1CardsByCardIdElementsByElementIdAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<DeleteCardkitV1CardsByCardIdElementsByElementIdBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());

        // Act
        await _sut.DeleteElementAsync(cardId, elementId, sequence);

        // Assert
        await _api.Received(1).DeleteCardkitV1CardsByCardIdElementsByElementIdAsync(
            cardId,
            elementId,
            Arg.Any<DeleteCardkitV1CardsByCardIdElementsByElementIdBodyDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteElementAsync_应传递正确的Sequence()
    {
        // Arrange
        DeleteCardkitV1CardsByCardIdElementsByElementIdBodyDto? capturedDto = null;

        _api.DeleteCardkitV1CardsByCardIdElementsByElementIdAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Do<DeleteCardkitV1CardsByCardIdElementsByElementIdBodyDto>(dto => capturedDto = dto),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());

        // Act
        await _sut.DeleteElementAsync("card-1", "elem-1", 11);

        // Assert
        Assert.NotNull(capturedDto);
        Assert.Equal(11, capturedDto.Sequence);
    }

    #endregion

    #region CloseStreamingAsync

    [Fact]
    public async Task CloseStreamingAsync_应调用限流器和API()
    {
        // Arrange
        var cardId = "card-close";
        var sequence = 100;

        _api.PatchCardkitV1CardsByCardIdSettingsAsync(
                Arg.Any<string>(),
                Arg.Any<PatchCardkitV1CardsByCardIdSettingsBodyDto>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());

        // Act
        await _sut.CloseStreamingAsync(cardId, sequence);

        // Assert
        await _api.Received(1).PatchCardkitV1CardsByCardIdSettingsAsync(
            cardId,
            Arg.Any<PatchCardkitV1CardsByCardIdSettingsBodyDto>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CloseStreamingAsync_应传递正确的Settings和Sequence()
    {
        // Arrange
        PatchCardkitV1CardsByCardIdSettingsBodyDto? capturedDto = null;

        _api.PatchCardkitV1CardsByCardIdSettingsAsync(
                Arg.Any<string>(),
                Arg.Do<PatchCardkitV1CardsByCardIdSettingsBodyDto>(dto => capturedDto = dto),
                Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());

        // Act
        await _sut.CloseStreamingAsync("card-1", 99);

        // Assert
        Assert.NotNull(capturedDto);
        Assert.Contains("streaming_mode", capturedDto.Settings);
        Assert.Contains("false", capturedDto.Settings);
        Assert.Equal(99, capturedDto.Sequence);
    }

    #endregion

    #region 限流器调用验证

    [Fact]
    public async Task 各方法应使用对应的限流器_全部调用不抛异常()
    {
        // 通过验证所有方法可正常调用来间接验证限流器集成
        // 使用真实的 CardApiLimiter，低频调用不会被限流

        _api.PostCardkitV1CardsAsync(Arg.Any<PostCardkitV1CardsBodyDto>(), Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse(new PostCardkitV1CardsResponseDto { CardId = "c1" }));
        _api.PostImV1MessagesAsync(Arg.Any<string>(), Arg.Any<PostImV1MessagesBodyDto>(), Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse(new PostImV1MessagesResponseDto()));
        _api.PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto>(), Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());
        _api.PutCardkitV1CardsByCardIdElementsByElementIdAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<PutCardkitV1CardsByCardIdElementsByElementIdBodyDto>(), Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());
        _api.PutCardkitV1CardsByCardIdAsync(Arg.Any<string>(),
                Arg.Any<PutCardkitV1CardsByCardIdBodyDto>(), Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());
        _api.PostCardkitV1CardsByCardIdBatchUpdateAsync(Arg.Any<string>(),
                Arg.Any<PostCardkitV1CardsByCardIdBatchUpdateBodyDto>(), Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());
        _api.PostCardkitV1CardsByCardIdElementsAsync(Arg.Any<string>(),
                Arg.Any<PostCardkitV1CardsByCardIdElementsBodyDto>(), Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());
        _api.PatchCardkitV1CardsByCardIdElementsByElementIdAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<PatchCardkitV1CardsByCardIdElementsByElementIdBodyDto>(), Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());
        _api.DeleteCardkitV1CardsByCardIdElementsByElementIdAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DeleteCardkitV1CardsByCardIdElementsByElementIdBodyDto>(), Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());
        _api.PatchCardkitV1CardsByCardIdSettingsAsync(Arg.Any<string>(),
                Arg.Any<PatchCardkitV1CardsByCardIdSettingsBodyDto>(), Arg.Any<CancellationToken>())
            .Returns(CreateSuccessResponse());

        // 所有调用应在超时内完成（限流器不会阻塞低频调用）
        using var cts = new CancellationTokenSource(5000);

        await _sut.CreateAsync(new Card(), cts.Token);
        await _sut.SendMessageAsync("c1", "open_id", "ou_1", cts.Token);
        await _sut.UpdateElementStreamAsync("c1", "e1", "text", 1, cts.Token);
        await _sut.ReplaceElementAsync("c1", "e1", new MarkdownElement(), 2, cts.Token);
        await _sut.FullUpdateAsync("c1", new Card(), 3, cts.Token);
        await _sut.BatchUpdateAsync("c1", "{}", 4, cts.Token);
        await _sut.AddElementsAsync("c1", "append", null, "[]", 5, cts.Token);
        await _sut.PartialUpdateElementAsync("c1", "e1", "{}", 6, cts.Token);
        await _sut.DeleteElementAsync("c1", "e1", 7, cts.Token);
        await _sut.CloseStreamingAsync("c1", 8, cts.Token);

        // 验证所有 API 都恰好被调用一次
        await _api.Received(1).PostCardkitV1CardsAsync(
            Arg.Any<PostCardkitV1CardsBodyDto>(), Arg.Any<CancellationToken>());
        await _api.Received(1).PostImV1MessagesAsync(
            Arg.Any<string>(), Arg.Any<PostImV1MessagesBodyDto>(), Arg.Any<CancellationToken>());
        await _api.Received(1).PutCardkitV1CardsByCardIdElementsByElementIdContentAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<PutCardkitV1CardsByCardIdElementsByElementIdContentBodyDto>(), Arg.Any<CancellationToken>());
        await _api.Received(1).PutCardkitV1CardsByCardIdElementsByElementIdAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<PutCardkitV1CardsByCardIdElementsByElementIdBodyDto>(), Arg.Any<CancellationToken>());
        await _api.Received(1).PutCardkitV1CardsByCardIdAsync(
            Arg.Any<string>(), Arg.Any<PutCardkitV1CardsByCardIdBodyDto>(), Arg.Any<CancellationToken>());
        await _api.Received(1).PostCardkitV1CardsByCardIdBatchUpdateAsync(
            Arg.Any<string>(), Arg.Any<PostCardkitV1CardsByCardIdBatchUpdateBodyDto>(), Arg.Any<CancellationToken>());
        await _api.Received(1).PostCardkitV1CardsByCardIdElementsAsync(
            Arg.Any<string>(), Arg.Any<PostCardkitV1CardsByCardIdElementsBodyDto>(), Arg.Any<CancellationToken>());
        await _api.Received(1).PatchCardkitV1CardsByCardIdElementsByElementIdAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<PatchCardkitV1CardsByCardIdElementsByElementIdBodyDto>(), Arg.Any<CancellationToken>());
        await _api.Received(1).DeleteCardkitV1CardsByCardIdElementsByElementIdAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<DeleteCardkitV1CardsByCardIdElementsByElementIdBodyDto>(), Arg.Any<CancellationToken>());
        await _api.Received(1).PatchCardkitV1CardsByCardIdSettingsAsync(
            Arg.Any<string>(), Arg.Any<PatchCardkitV1CardsByCardIdSettingsBodyDto>(), Arg.Any<CancellationToken>());
    }

    #endregion
}
