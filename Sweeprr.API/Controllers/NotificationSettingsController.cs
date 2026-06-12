using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Notifications;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.API.Controllers;

/// <summary>Discord &amp; generic webhook notification settings (Story 11.1). Admin-only.</summary>
[ApiController]
[Route("api/settings/notifications")]
[Authorize(Policy = "AdminOnly")]
public sealed class NotificationSettingsController : ControllerBase
{
    private readonly SweeprrDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IEnumerable<INotificationProvider> _providers;

    public NotificationSettingsController(
        SweeprrDbContext db, ISecretProtector protector, IEnumerable<INotificationProvider> providers)
    {
        _db = db;
        _protector = protector;
        _providers = providers;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<NotificationSettingResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var settings = await _db.NotificationSettings
            .AsNoTracking()
            .OrderBy(n => n.Name)
            .ToListAsync(ct);

        return Ok(settings.Select(ToResponse));
    }

    [HttpPost]
    [ProducesResponseType(typeof(NotificationSettingResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateNotificationSettingRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required." });

        if (!IsValidWebhookUrl(request.WebhookUrl))
            return BadRequest(new { error = "WebhookUrl must be a valid absolute http(s) URL." });

        var entity = new NotificationSetting
        {
            Name = request.Name.Trim(),
            ProviderType = request.ProviderType,
            WebhookUrlEncrypted = _protector.Protect(request.WebhookUrl.Trim()),
            IsEnabled = request.IsEnabled,
            TriggerOnFailsafe = request.TriggerOnFailsafe,
            TriggerOnSweepComplete = request.TriggerOnSweepComplete,
            TriggerOnPendingItems = request.TriggerOnPendingItems,
            TriggerOnConnectionError = request.TriggerOnConnectionError,
            CreatedAt = DateTime.UtcNow,
        };

        _db.NotificationSettings.Add(entity);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetAll), ToResponse(entity));
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(NotificationSettingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateNotificationSettingRequest request, CancellationToken ct)
    {
        var entity = await _db.NotificationSettings.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (entity is null)
            return NotFound();

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { error = "Name cannot be empty." });
            entity.Name = request.Name.Trim();
        }

        if (request.WebhookUrl is not null)
        {
            if (!IsValidWebhookUrl(request.WebhookUrl))
                return BadRequest(new { error = "WebhookUrl must be a valid absolute http(s) URL." });
            entity.WebhookUrlEncrypted = _protector.Protect(request.WebhookUrl.Trim());
        }

        if (request.IsEnabled.HasValue)
            entity.IsEnabled = request.IsEnabled.Value;

        if (request.TriggerOnFailsafe.HasValue)
            entity.TriggerOnFailsafe = request.TriggerOnFailsafe.Value;

        if (request.TriggerOnSweepComplete.HasValue)
            entity.TriggerOnSweepComplete = request.TriggerOnSweepComplete.Value;

        if (request.TriggerOnPendingItems.HasValue)
            entity.TriggerOnPendingItems = request.TriggerOnPendingItems.Value;

        if (request.TriggerOnConnectionError.HasValue)
            entity.TriggerOnConnectionError = request.TriggerOnConnectionError.Value;

        await _db.SaveChangesAsync(ct);
        return Ok(ToResponse(entity));
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var entity = await _db.NotificationSettings.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (entity is null)
            return NotFound();

        _db.NotificationSettings.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Sends a test payload to an arbitrary webhook URL — used by the create/edit form before saving.</summary>
    [HttpPost("test")]
    [ProducesResponseType(typeof(TestNotificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Test([FromBody] TestNotificationRequest request, CancellationToken ct)
    {
        if (!IsValidWebhookUrl(request.WebhookUrl))
            return BadRequest(new { error = "WebhookUrl must be a valid absolute http(s) URL." });

        return Ok(await SendTestAsync(request.ProviderType, request.WebhookUrl.Trim(), ct));
    }

    /// <summary>Sends a test payload to an existing, saved notification setting's webhook.</summary>
    [HttpPost("{id:int}/test")]
    [ProducesResponseType(typeof(TestNotificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TestExisting(int id, CancellationToken ct)
    {
        var entity = await _db.NotificationSettings.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);
        if (entity is null)
            return NotFound();

        var url = _protector.Unprotect(entity.WebhookUrlEncrypted);
        if (string.IsNullOrEmpty(url))
            return Ok(new TestNotificationResponse(false, "Stored webhook URL could not be decrypted."));

        return Ok(await SendTestAsync(entity.ProviderType, url, ct));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<TestNotificationResponse> SendTestAsync(
        NotificationProviderType providerType, string webhookUrl, CancellationToken ct)
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderType == providerType);
        if (provider is null)
            return new TestNotificationResponse(false, $"No provider registered for {providerType}.");

        var payload = new NotificationPayload(
            NotificationTrigger.SweepComplete,
            "🧪 Sweeprr Test Notification",
            [("Status", "This is a test notification from Sweeprr.")],
            new { message = "This is a test notification from Sweeprr." },
            DateTimeOffset.UtcNow);

        var ok = await provider.SendAsync(webhookUrl, payload, ct);
        return new TestNotificationResponse(ok, ok ? null : "Webhook returned a non-success response.");
    }

    private NotificationSettingResponse ToResponse(NotificationSetting n) => new(
        n.Id,
        n.Name,
        n.ProviderType,
        MaskUrl(_protector.Unprotect(n.WebhookUrlEncrypted)),
        n.IsEnabled,
        n.TriggerOnFailsafe,
        n.TriggerOnSweepComplete,
        n.TriggerOnPendingItems,
        n.TriggerOnConnectionError,
        n.CreatedAt);

    private static bool IsValidWebhookUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string MaskUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "(unavailable)";

        try
        {
            var uri = new Uri(url);
            var suffix = url.Length > 6 ? url[^6..] : url;
            return $"{uri.Scheme}://{uri.Host}/···{suffix}";
        }
        catch (UriFormatException)
        {
            return "···";
        }
    }
}
