using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Data;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.Tests.Notifications;

/// <summary>
/// Unit tests for <see cref="NotificationDispatchWorker"/> — verifies that queued notifications
/// are delivered only to enabled settings whose provider type and trigger flag match, and that
/// unresolvable providers / undecryptable URLs are skipped without interrupting the dispatch loop.
/// </summary>
public class NotificationDispatchWorkerTests : IDisposable
{
    private readonly List<string> _dbPaths = [];
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        foreach (var provider in _providers)
            provider.Dispose();

        SqliteConnection.ClearAllPools();
        foreach (var path in _dbPaths)
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { var f = path + suffix; if (File.Exists(f)) File.Delete(f); } catch { }
    }

    [Theory]
    [InlineData(NotificationTrigger.SweepComplete)]
    [InlineData(NotificationTrigger.FailsafeTripped)]
    [InlineData(NotificationTrigger.PendingItems)]
    [InlineData(NotificationTrigger.ConnectionError)]
    public async Task Dispatch_DeliversOnlyWhenMatchingTriggerFlagIsEnabled(NotificationTrigger trigger)
    {
        var (worker, channel, db, discord, _) = CreateWorker();

        db.NotificationSettings.Add(new NotificationSetting
        {
            Name = "Discord",
            ProviderType = NotificationProviderType.Discord,
            WebhookUrlEncrypted = "enc:https://discord.test/webhook",
            IsEnabled = true,
            TriggerOnFailsafe = trigger == NotificationTrigger.FailsafeTripped,
            TriggerOnSweepComplete = trigger == NotificationTrigger.SweepComplete,
            TriggerOnPendingItems = trigger == NotificationTrigger.PendingItems,
            TriggerOnConnectionError = trigger == NotificationTrigger.ConnectionError,
        });
        await db.SaveChangesAsync();

        await worker.StartAsync(CancellationToken.None);
        channel.Writer.TryWrite(new NotificationDispatchRequest(trigger, SamplePayload(trigger)));

        await discord.Called.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(discord.Calls);
        Assert.Equal("https://discord.test/webhook", discord.Calls[0].Url);
        Assert.Equal(trigger, discord.Calls[0].Payload.Trigger);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Dispatch_DisabledSetting_NotDelivered()
    {
        var (worker, channel, db, discord, generic) = CreateWorker();

        db.NotificationSettings.AddRange(
            new NotificationSetting
            {
                Name = "Disabled Discord",
                ProviderType = NotificationProviderType.Discord,
                WebhookUrlEncrypted = "enc:https://discord.test/webhook",
                IsEnabled = false,
                TriggerOnSweepComplete = true,
            },
            new NotificationSetting
            {
                Name = "Enabled Generic",
                ProviderType = NotificationProviderType.GenericWebhook,
                WebhookUrlEncrypted = "enc:https://example.test/webhook",
                IsEnabled = true,
                TriggerOnSweepComplete = true,
            });
        await db.SaveChangesAsync();

        await worker.StartAsync(CancellationToken.None);
        channel.Writer.TryWrite(new NotificationDispatchRequest(
            NotificationTrigger.SweepComplete, SamplePayload(NotificationTrigger.SweepComplete)));

        await generic.Called.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(discord.Calls);
        Assert.Single(generic.Calls);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Dispatch_TriggerFlagDisabled_SkipsButContinuesProcessingQueue()
    {
        var (worker, channel, db, discord, _) = CreateWorker();

        db.NotificationSettings.Add(new NotificationSetting
        {
            Name = "Discord",
            ProviderType = NotificationProviderType.Discord,
            WebhookUrlEncrypted = "enc:https://discord.test/webhook",
            IsEnabled = true,
            TriggerOnSweepComplete = true,
            TriggerOnPendingItems = false,
        });
        await db.SaveChangesAsync();

        await worker.StartAsync(CancellationToken.None);

        // PendingItems: flag disabled — must be skipped.
        channel.Writer.TryWrite(new NotificationDispatchRequest(
            NotificationTrigger.PendingItems, SamplePayload(NotificationTrigger.PendingItems)));
        // SweepComplete: flag enabled — must be delivered.
        channel.Writer.TryWrite(new NotificationDispatchRequest(
            NotificationTrigger.SweepComplete, SamplePayload(NotificationTrigger.SweepComplete)));

        await discord.Called.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(discord.Calls);
        Assert.Equal(NotificationTrigger.SweepComplete, discord.Calls[0].Payload.Trigger);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Dispatch_NoProviderRegisteredForType_SkipsGracefullyAndContinues()
    {
        // Only a Discord provider is registered — a GenericWebhook setting has no matching provider.
        var (worker, channel, db, discord, _) = CreateWorker(registerGenericProvider: false);

        db.NotificationSettings.AddRange(
            new NotificationSetting
            {
                Name = "Unsupported Generic",
                ProviderType = NotificationProviderType.GenericWebhook,
                WebhookUrlEncrypted = "enc:https://example.test/webhook",
                IsEnabled = true,
                TriggerOnSweepComplete = true,
            },
            new NotificationSetting
            {
                Name = "Discord",
                ProviderType = NotificationProviderType.Discord,
                WebhookUrlEncrypted = "enc:https://discord.test/webhook",
                IsEnabled = true,
                TriggerOnSweepComplete = true,
            });
        await db.SaveChangesAsync();

        await worker.StartAsync(CancellationToken.None);
        channel.Writer.TryWrite(new NotificationDispatchRequest(
            NotificationTrigger.SweepComplete, SamplePayload(NotificationTrigger.SweepComplete)));

        await discord.Called.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(discord.Calls);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Dispatch_UndecryptableWebhookUrl_SkipsGracefullyAndContinues()
    {
        var (worker, channel, db, discord, _) = CreateWorker();

        db.NotificationSettings.AddRange(
            new NotificationSetting
            {
                Name = "Corrupted Discord",
                ProviderType = NotificationProviderType.Discord,
                WebhookUrlEncrypted = "garbage-not-encrypted-by-us",
                IsEnabled = true,
                TriggerOnSweepComplete = true,
            },
            new NotificationSetting
            {
                Name = "Healthy Discord",
                ProviderType = NotificationProviderType.Discord,
                WebhookUrlEncrypted = "enc:https://discord.test/webhook",
                IsEnabled = true,
                TriggerOnSweepComplete = true,
            });
        await db.SaveChangesAsync();

        await worker.StartAsync(CancellationToken.None);
        channel.Writer.TryWrite(new NotificationDispatchRequest(
            NotificationTrigger.SweepComplete, SamplePayload(NotificationTrigger.SweepComplete)));

        await discord.Called.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(discord.Calls);
        Assert.Equal("https://discord.test/webhook", discord.Calls[0].Url);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Dispatch_MultipleEnabledSettings_AllReceiveDelivery()
    {
        var (worker, channel, db, discord, _) = CreateWorker();

        db.NotificationSettings.AddRange(
            new NotificationSetting
            {
                Name = "Discord A",
                ProviderType = NotificationProviderType.Discord,
                WebhookUrlEncrypted = "enc:https://discord.test/a",
                IsEnabled = true,
                TriggerOnSweepComplete = true,
            },
            new NotificationSetting
            {
                Name = "Discord B",
                ProviderType = NotificationProviderType.Discord,
                WebhookUrlEncrypted = "enc:https://discord.test/b",
                IsEnabled = true,
                TriggerOnSweepComplete = true,
            });
        await db.SaveChangesAsync();

        await worker.StartAsync(CancellationToken.None);
        channel.Writer.TryWrite(new NotificationDispatchRequest(
            NotificationTrigger.SweepComplete, SamplePayload(NotificationTrigger.SweepComplete)));

        await WaitUntilAsync(() => discord.Calls.Count >= 2, TimeSpan.FromSeconds(5));

        var urls = discord.Calls.Select(c => c.Url).OrderBy(u => u).ToList();
        Assert.Equal(["https://discord.test/a", "https://discord.test/b"], urls);

        await worker.StopAsync(CancellationToken.None);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Test infrastructure
    // ═══════════════════════════════════════════════════════════════════════

    private (NotificationDispatchWorker Worker, Channel<NotificationDispatchRequest> Channel, SweeprrDbContext Db,
        FakeNotificationProvider Discord, FakeNotificationProvider Generic) CreateWorker(bool registerGenericProvider = true)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_notif_dispatch_{Guid.NewGuid()}.db");
        _dbPaths.Add(dbPath);
        var connectionString = $"Data Source={dbPath}";

        var services = new ServiceCollection();
        services.AddDbContext<SweeprrDbContext>(o => o.UseSqlite(connectionString));
        services.AddSingleton<ISecretProtector, PrefixSecretProtector>();
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        var db = new SweeprrDbContext(new DbContextOptionsBuilder<SweeprrDbContext>()
            .UseSqlite(connectionString)
            .Options);
        db.Database.Migrate();

        var channel = Channel.CreateUnbounded<NotificationDispatchRequest>();
        var discord = new FakeNotificationProvider(NotificationProviderType.Discord);
        var generic = new FakeNotificationProvider(NotificationProviderType.GenericWebhook);

        var registeredProviders = registerGenericProvider
            ? new[] { discord, generic }
            : [discord];

        var worker = new NotificationDispatchWorker(
            channel,
            provider.GetRequiredService<IServiceScopeFactory>(),
            registeredProviders,
            NullLogger<NotificationDispatchWorker>.Instance);

        return (worker, channel, db, discord, generic);
    }

    private static NotificationPayload SamplePayload(NotificationTrigger trigger) => new(
        trigger,
        "Title",
        [("Key", "Value")],
        new { key = "value" },
        DateTimeOffset.UtcNow);

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met within timeout.");
            await Task.Delay(10);
        }
    }

    /// <summary>Round-trips via an "enc:" prefix; returns null for anything not produced by Protect — mirrors the "unreadable URL" path.</summary>
    private sealed class PrefixSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => $"enc:{plaintext}";
        public string? Unprotect(string ciphertext) => ciphertext.StartsWith("enc:") ? ciphertext[4..] : null;
    }

    private sealed class FakeNotificationProvider : INotificationProvider
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public NotificationProviderType ProviderType { get; }
        public List<(string Url, NotificationPayload Payload)> Calls { get; } = [];
        public Task Called => _tcs.Task;

        public FakeNotificationProvider(NotificationProviderType type) => ProviderType = type;

        public Task<bool> SendAsync(string webhookUrl, NotificationPayload payload, CancellationToken ct = default)
        {
            Calls.Add((webhookUrl, payload));
            _tcs.TrySetResult();
            return Task.FromResult(true);
        }
    }
}
