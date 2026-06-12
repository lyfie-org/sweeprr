using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Controllers;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Notifications;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.Tests.Controllers;

public class NotificationSettingsControllerTests : IDisposable
{
    private readonly SweeprrDbContext _db;
    private readonly string _dbPath;
    private readonly FakeNotificationProvider _discord = new(NotificationProviderType.Discord);
    private readonly FakeNotificationProvider _generic = new(NotificationProviderType.GenericWebhook);
    private readonly NotificationSettingsController _controller;

    public NotificationSettingsControllerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_notif_settings_{Guid.NewGuid()}.db");
        var options = new DbContextOptionsBuilder<SweeprrDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _db = new SweeprrDbContext(options);
        _db.Database.Migrate();

        _controller = new NotificationSettingsController(_db, new PrefixSecretProtector(), [_discord, _generic]);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    // ── GetAll ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsEmptyList_WhenNoSettings()
    {
        var result = await _controller.GetAll(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var settings = Assert.IsAssignableFrom<IEnumerable<NotificationSettingResponse>>(ok.Value);
        Assert.Empty(settings);
    }

    [Fact]
    public async Task GetAll_OrdersByNameAscending()
    {
        await _controller.Create(CreateRequest(name: "Zebra"), CancellationToken.None);
        await _controller.Create(CreateRequest(name: "Alpha"), CancellationToken.None);

        var result = await _controller.GetAll(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var settings = Assert.IsAssignableFrom<IEnumerable<NotificationSettingResponse>>(ok.Value).ToList();

        Assert.Equal(2, settings.Count);
        Assert.Equal("Alpha", settings[0].Name);
        Assert.Equal("Zebra", settings[1].Name);
    }

    // ── Create ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidRequest_PersistsAndReturnsMaskedUrl()
    {
        var request = CreateRequest(name: "My Discord", webhookUrl: "https://discord.com/api/webhooks/123456789/abcdefghijklmnop");

        var result = await _controller.Create(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<NotificationSettingResponse>(created.Value);

        Assert.Equal("My Discord", response.Name);
        Assert.Equal(NotificationProviderType.Discord, response.ProviderType);
        Assert.StartsWith("https://discord.com/···", response.MaskedWebhookUrl);
        Assert.EndsWith("klmnop", response.MaskedWebhookUrl);
        Assert.DoesNotContain("123456789", response.MaskedWebhookUrl);

        var entity = await _db.NotificationSettings.AsNoTracking().FirstAsync();
        Assert.Equal("enc:https://discord.com/api/webhooks/123456789/abcdefghijklmnop", entity.WebhookUrlEncrypted);
    }

    [Fact]
    public async Task Create_RejectsEmptyName()
    {
        var request = CreateRequest(name: "   ");

        var result = await _controller.Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await _db.NotificationSettings.ToListAsync());
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/hook")]
    [InlineData("")]
    public async Task Create_RejectsInvalidWebhookUrl(string webhookUrl)
    {
        var request = CreateRequest(webhookUrl: webhookUrl);

        var result = await _controller.Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await _db.NotificationSettings.ToListAsync());
    }

    [Fact]
    public async Task Create_PersistsTriggerFlags()
    {
        var request = new CreateNotificationSettingRequest(
            "Custom Flags",
            NotificationProviderType.GenericWebhook,
            "https://example.com/hook",
            IsEnabled: false,
            TriggerOnFailsafe: false,
            TriggerOnSweepComplete: true,
            TriggerOnPendingItems: true,
            TriggerOnConnectionError: true);

        var result = await _controller.Create(request, CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<NotificationSettingResponse>(created.Value);

        Assert.False(response.IsEnabled);
        Assert.False(response.TriggerOnFailsafe);
        Assert.True(response.TriggerOnSweepComplete);
        Assert.True(response.TriggerOnPendingItems);
        Assert.True(response.TriggerOnConnectionError);
    }

    // ── Update ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ChangesName_PreservesWebhookUrl_WhenNotProvided()
    {
        var id = await CreateAndGetIdAsync(name: "Original", webhookUrl: "https://discord.com/api/webhooks/1/secret123");

        var result = await _controller.Update(id, new UpdateNotificationSettingRequest(
            "Renamed", null, null, null, null, null, null), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<NotificationSettingResponse>(ok.Value);
        Assert.Equal("Renamed", response.Name);

        var entity = await _db.NotificationSettings.AsNoTracking().FirstAsync(n => n.Id == id);
        Assert.Equal("enc:https://discord.com/api/webhooks/1/secret123", entity.WebhookUrlEncrypted);
    }

    [Fact]
    public async Task Update_ChangesWebhookUrl_WhenProvided()
    {
        var id = await CreateAndGetIdAsync(webhookUrl: "https://discord.com/api/webhooks/1/old-secret");

        await _controller.Update(id, new UpdateNotificationSettingRequest(
            null, "https://discord.com/api/webhooks/1/new-secret", null, null, null, null, null), CancellationToken.None);

        var entity = await _db.NotificationSettings.AsNoTracking().FirstAsync(n => n.Id == id);
        Assert.Equal("enc:https://discord.com/api/webhooks/1/new-secret", entity.WebhookUrlEncrypted);
    }

    [Fact]
    public async Task Update_RejectsInvalidWebhookUrl()
    {
        var id = await CreateAndGetIdAsync(webhookUrl: "https://discord.com/api/webhooks/1/secret");

        var result = await _controller.Update(id, new UpdateNotificationSettingRequest(
            null, "not-a-url", null, null, null, null, null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);

        var entity = await _db.NotificationSettings.AsNoTracking().FirstAsync(n => n.Id == id);
        Assert.Equal("enc:https://discord.com/api/webhooks/1/secret", entity.WebhookUrlEncrypted);
    }

    [Fact]
    public async Task Update_RejectsEmptyName()
    {
        var id = await CreateAndGetIdAsync(name: "Keep Me");

        var result = await _controller.Update(id, new UpdateNotificationSettingRequest(
            "   ", null, null, null, null, null, null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);

        var entity = await _db.NotificationSettings.AsNoTracking().FirstAsync(n => n.Id == id);
        Assert.Equal("Keep Me", entity.Name);
    }

    [Fact]
    public async Task Update_NotFound_ReturnsNotFound()
    {
        var result = await _controller.Update(9999, new UpdateNotificationSettingRequest(
            "X", null, null, null, null, null, null), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Update_TogglesEnabledAndTriggerFlags()
    {
        var id = await CreateAndGetIdAsync();

        var result = await _controller.Update(id, new UpdateNotificationSettingRequest(
            null, null, IsEnabled: false, TriggerOnFailsafe: false, TriggerOnSweepComplete: false,
            TriggerOnPendingItems: true, TriggerOnConnectionError: true), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<NotificationSettingResponse>(ok.Value);

        Assert.False(response.IsEnabled);
        Assert.False(response.TriggerOnFailsafe);
        Assert.False(response.TriggerOnSweepComplete);
        Assert.True(response.TriggerOnPendingItems);
        Assert.True(response.TriggerOnConnectionError);
    }

    // ── Delete ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_RemovesSetting()
    {
        var id = await CreateAndGetIdAsync();

        var result = await _controller.Delete(id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await _db.NotificationSettings.ToListAsync());
    }

    [Fact]
    public async Task Delete_NotFound_ReturnsNotFound()
    {
        var result = await _controller.Delete(9999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── Test (ad-hoc) ─────────────────────────────────────────────────────

    [Fact]
    public async Task Test_ValidRequest_ReturnsSuccessAndInvokesMatchingProvider()
    {
        var request = new TestNotificationRequest(NotificationProviderType.Discord, "https://discord.com/api/webhooks/1/2");

        var result = await _controller.Test(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TestNotificationResponse>(ok.Value);

        Assert.True(response.Success);
        Assert.Null(response.Error);
        Assert.Single(_discord.Calls);
        Assert.Equal("https://discord.com/api/webhooks/1/2", _discord.Calls[0].Url);
        Assert.Empty(_generic.Calls);
    }

    [Fact]
    public async Task Test_InvalidWebhookUrl_ReturnsBadRequest()
    {
        var request = new TestNotificationRequest(NotificationProviderType.Discord, "not-a-url");

        var result = await _controller.Test(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(_discord.Calls);
    }

    [Fact]
    public async Task Test_ProviderReturnsFailure_ReturnsUnsuccessfulResponse()
    {
        _discord.Result = false;
        var request = new TestNotificationRequest(NotificationProviderType.Discord, "https://discord.com/api/webhooks/1/2");

        var result = await _controller.Test(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TestNotificationResponse>(ok.Value);

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
    }

    // ── TestExisting ──────────────────────────────────────────────────────

    [Fact]
    public async Task TestExisting_ValidId_InvokesProviderWithDecryptedUrl()
    {
        var id = await CreateAndGetIdAsync(webhookUrl: "https://discord.com/api/webhooks/9/abcdef");

        var result = await _controller.TestExisting(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TestNotificationResponse>(ok.Value);

        Assert.True(response.Success);
        Assert.Single(_discord.Calls);
        Assert.Equal("https://discord.com/api/webhooks/9/abcdef", _discord.Calls[0].Url);
    }

    [Fact]
    public async Task TestExisting_NotFound_ReturnsNotFound()
    {
        var result = await _controller.TestExisting(9999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task TestExisting_UndecryptableUrl_ReturnsFailureWithoutInvokingProvider()
    {
        _db.NotificationSettings.Add(new NotificationSetting
        {
            Name = "Corrupted",
            ProviderType = NotificationProviderType.Discord,
            WebhookUrlEncrypted = "garbage-not-encrypted-by-us",
            IsEnabled = true,
        });
        await _db.SaveChangesAsync();
        var id = await _db.NotificationSettings.Select(n => n.Id).FirstAsync();

        var result = await _controller.TestExisting(id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TestNotificationResponse>(ok.Value);

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.Empty(_discord.Calls);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Test infrastructure
    // ═══════════════════════════════════════════════════════════════════════

    private static CreateNotificationSettingRequest CreateRequest(
        string name = "Test Webhook",
        NotificationProviderType providerType = NotificationProviderType.Discord,
        string webhookUrl = "https://discord.com/api/webhooks/1/abcdef") => new(
        name, providerType, webhookUrl,
        IsEnabled: true,
        TriggerOnFailsafe: true,
        TriggerOnSweepComplete: true,
        TriggerOnPendingItems: false,
        TriggerOnConnectionError: false);

    private async Task<int> CreateAndGetIdAsync(
        string name = "Test Webhook",
        NotificationProviderType providerType = NotificationProviderType.Discord,
        string webhookUrl = "https://discord.com/api/webhooks/1/abcdef")
    {
        var result = await _controller.Create(CreateRequest(name, providerType, webhookUrl), CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<NotificationSettingResponse>(created.Value);
        return response.Id;
    }

    /// <summary>Round-trips via an "enc:" prefix; returns null for anything not produced by Protect — mirrors the "unreadable URL" path.</summary>
    private sealed class PrefixSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => $"enc:{plaintext}";
        public string? Unprotect(string ciphertext) => ciphertext.StartsWith("enc:") ? ciphertext[4..] : null;
    }

    private sealed class FakeNotificationProvider : INotificationProvider
    {
        public NotificationProviderType ProviderType { get; }
        public bool Result { get; set; } = true;
        public List<(string Url, NotificationPayload Payload)> Calls { get; } = [];

        public FakeNotificationProvider(NotificationProviderType type) => ProviderType = type;

        public Task<bool> SendAsync(string webhookUrl, NotificationPayload payload, CancellationToken ct = default)
        {
            Calls.Add((webhookUrl, payload));
            return Task.FromResult(Result);
        }
    }
}
