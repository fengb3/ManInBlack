using ManInBlack.AI.Services;
using Xunit;

namespace ManInBlack.AI.Tests;

public class EventBusTests
{
    private readonly EventBus _bus = new();

    private record TestEvent(string Message);

    [Fact]
    public async Task Subscribe_Publishes_To_Matching_Key()
    {
        var received = new List<TestEvent>();
        using var sub = _bus.Subscribe<TestEvent>("key1", (e, _) =>
        {
            received.Add(e);
            return Task.CompletedTask;
        });

        await _bus.PublishAsync("key1", new TestEvent("hello"));

        Assert.Single(received);
        Assert.Equal("hello", received[0].Message);
    }

    [Fact]
    public async Task Publish_Does_Not_Reach_Different_Key()
    {
        var received = new List<TestEvent>();
        using var sub = _bus.Subscribe<TestEvent>("key1", (e, _) =>
        {
            received.Add(e);
            return Task.CompletedTask;
        });

        await _bus.PublishAsync("key2", new TestEvent("hello"));

        Assert.Empty(received);
    }

    [Fact]
    public async Task Multiple_Subscribers_Same_Key_All_Receive()
    {
        var received1 = new List<TestEvent>();
        var received2 = new List<TestEvent>();

        using var sub1 = _bus.Subscribe<TestEvent>("key1", (e, _) => { received1.Add(e); return Task.CompletedTask; });
        using var sub2 = _bus.Subscribe<TestEvent>("key1", (e, _) => { received2.Add(e); return Task.CompletedTask; });

        await _bus.PublishAsync("key1", new TestEvent("hello"));

        Assert.Single(received1);
        Assert.Single(received2);
    }

    [Fact]
    public async Task Dispose_Stops_Receiving_Events()
    {
        var received = new List<TestEvent>();
        var sub = _bus.Subscribe<TestEvent>("key1", (e, _) => { received.Add(e); return Task.CompletedTask; });

        await _bus.PublishAsync("key1", new TestEvent("first"));
        sub.Dispose();
        await _bus.PublishAsync("key1", new TestEvent("second"));

        Assert.Single(received);
        Assert.Equal("first", received[0].Message);
    }

    [Fact]
    public async Task Publish_To_Nonexistent_Key_Does_Nothing()
    {
        // 不应抛异常
        await _bus.PublishAsync("no-such-key", new TestEvent("hello"));
    }

    [Fact]
    public async Task Double_Dispose_Does_Not_Throw()
    {
        var sub = _bus.Subscribe<TestEvent>("key1", (e, _) => Task.CompletedTask);
        sub.Dispose();
        sub.Dispose();

        await _bus.PublishAsync("key1", new TestEvent("hello"));
    }

    [Fact]
    public async Task Different_Event_Types_Are_Isolated()
    {
        var stringReceived = new List<string>();
        var intReceived = new List<int>();

        using var s1 = _bus.Subscribe<string>("key1", (e, _) => { stringReceived.Add(e); return Task.CompletedTask; });
        using var s2 = _bus.Subscribe<int>("key1", (e, _) => { intReceived.Add(e); return Task.CompletedTask; });

        await _bus.PublishAsync("key1", "hello");
        await _bus.PublishAsync("key1", 42);

        Assert.Single(stringReceived);
        Assert.Single(intReceived);
        Assert.Equal("hello", stringReceived[0]);
        Assert.Equal(42, intReceived[0]);
    }

    [Fact]
    public async Task Same_Key_Different_Event_Types_Independent()
    {
        // 销毁 string 订阅不影响 int 订阅
        var intReceived = new List<int>();
        var stringSub = _bus.Subscribe<string>("key1", (e, _) => Task.CompletedTask);
        using var intSub = _bus.Subscribe<int>("key1", (e, _) => { intReceived.Add(e); return Task.CompletedTask; });

        stringSub.Dispose();

        await _bus.PublishAsync("key1", 42);
        Assert.Single(intReceived);
    }
}
