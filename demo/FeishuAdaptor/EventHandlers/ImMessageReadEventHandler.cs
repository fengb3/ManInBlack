using FeishuNetSdk.Im.Events;
using FeishuNetSdk.Services;

namespace FeishuAdaptor.EventHandlers;

public class ImMessageReadEventHandler(
// ILogger<EventHandler> logger
)
    : IEventHandler<
        EventV2Dto<ImMessageMessageReadV1EventBodyDto>,
        ImMessageMessageReadV1EventBodyDto
    >
{
    public Task ExecuteAsync(
        EventV2Dto<ImMessageMessageReadV1EventBodyDto> input,
        CancellationToken cancellationToken = default
    )
    {
        // logger.LogInformation(
        //     "{messageId} messages has been read",
        //     input.Event?.MessageIdList?.Length.ToString() ?? "unknown"
        // );

        return Task.CompletedTask;
    }
}
