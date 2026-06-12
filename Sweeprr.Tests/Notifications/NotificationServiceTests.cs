using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.Tests.Notifications;

/// <summary>Unit tests for <see cref="NotificationService"/> — enqueueing onto the dispatch channel.</summary>
public class NotificationServiceTests
{
    [Fact]
    public void Notify_WritesRequestToChannel()
    {
        var channel = Channel.CreateUnbounded<NotificationDispatchRequest>();
        var service = new NotificationService(channel, NullLogger<NotificationService>.Instance);
        var payload = SamplePayload(NotificationTrigger.SweepComplete);

        service.Notify(NotificationTrigger.SweepComplete, payload);

        Assert.True(channel.Reader.TryRead(out var request));
        Assert.Equal(NotificationTrigger.SweepComplete, request!.Trigger);
        Assert.Same(payload, request.Payload);
    }

    [Fact]
    public void Notify_MultipleCalls_AllEnqueuedInOrder()
    {
        var channel = Channel.CreateUnbounded<NotificationDispatchRequest>();
        var service = new NotificationService(channel, NullLogger<NotificationService>.Instance);

        service.Notify(NotificationTrigger.SweepComplete, SamplePayload(NotificationTrigger.SweepComplete));
        service.Notify(NotificationTrigger.PendingItems, SamplePayload(NotificationTrigger.PendingItems));

        Assert.True(channel.Reader.TryRead(out var first));
        Assert.True(channel.Reader.TryRead(out var second));
        Assert.False(channel.Reader.TryRead(out _));

        Assert.Equal(NotificationTrigger.SweepComplete, first!.Trigger);
        Assert.Equal(NotificationTrigger.PendingItems, second!.Trigger);
    }

    [Fact]
    public void Notify_FullBoundedChannel_DoesNotThrow()
    {
        var channel = Channel.CreateBounded<NotificationDispatchRequest>(1);
        var service = new NotificationService(channel, NullLogger<NotificationService>.Instance);

        service.Notify(NotificationTrigger.SweepComplete, SamplePayload(NotificationTrigger.SweepComplete));

        // Channel is now full (capacity 1, no reader draining) — must be logged and swallowed, never thrown.
        service.Notify(NotificationTrigger.FailsafeTripped, SamplePayload(NotificationTrigger.FailsafeTripped));

        Assert.True(channel.Reader.TryRead(out var only));
        Assert.Equal(NotificationTrigger.SweepComplete, only!.Trigger);
        Assert.False(channel.Reader.TryRead(out _));
    }

    private static NotificationPayload SamplePayload(NotificationTrigger trigger) => new(
        trigger,
        "Title",
        [("Key", "Value")],
        new { key = "value" },
        DateTimeOffset.UtcNow);
}
