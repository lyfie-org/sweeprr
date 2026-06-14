using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Background;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Backup;
using Sweeprr.API.Models;
using Sweeprr.API.Services;

namespace Sweeprr.API.Controllers;

/// <summary>Automated SQLite backup settings, manual trigger, and history (Story 11.3). Admin-only.</summary>
[ApiController]
[Route("api/settings/backup")]
[Authorize(Policy = "AdminOnly")]
public sealed class BackupSettingsController : ControllerBase
{
    private readonly SweeprrDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IBackupService _backupService;
    private readonly BackupSchedulerHostedService _scheduler;

    public BackupSettingsController(
        SweeprrDbContext db,
        ISecretProtector protector,
        IBackupService backupService,
        BackupSchedulerHostedService scheduler)
    {
        _db = db;
        _protector = protector;
        _backupService = backupService;
        _scheduler = scheduler;
    }

    [HttpGet]
    [ProducesResponseType(typeof(BackupSettingResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var settings = await GetSettingsAsync(ct);
        return Ok(ToResponse(settings));
    }

    [HttpPut]
    [ProducesResponseType(typeof(BackupSettingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update([FromBody] UpdateBackupSettingRequest request, CancellationToken ct)
    {
        if (request.RetentionCount is < 1 or > 20)
            return BadRequest(new { error = "RetentionCount must be between 1 and 20." });

        if (request.ScheduleCron is not null && !SchedulerHostedService.IsValidCron(request.ScheduleCron))
            return BadRequest(new { error = "ScheduleCron is not a valid cron expression." });

        var entity = await GetSettingsAsync(ct);

        if (request.IsEnabled.HasValue) entity.IsEnabled = request.IsEnabled.Value;
        if (request.DestinationType.HasValue) entity.DestinationType = request.DestinationType.Value;
        if (request.LocalPath is not null) entity.LocalPath = request.LocalPath.Trim();
        if (request.S3Endpoint is not null) entity.S3Endpoint = request.S3Endpoint.Trim();
        if (request.S3Region is not null) entity.S3Region = request.S3Region.Trim();
        if (request.S3Bucket is not null) entity.S3Bucket = request.S3Bucket.Trim();
        if (request.S3AccessKey is not null) entity.S3AccessKey = request.S3AccessKey.Trim();
        if (!string.IsNullOrEmpty(request.S3SecretKey)) entity.S3SecretKeyEncrypted = _protector.Protect(request.S3SecretKey.Trim());
        if (request.RetentionCount.HasValue) entity.RetentionCount = request.RetentionCount.Value;
        if (request.ScheduleCron is not null) entity.ScheduleCron = request.ScheduleCron.Trim();

        await _db.SaveChangesAsync(ct);
        await _scheduler.ForceReloadAsync(ct);

        return Ok(ToResponse(entity));
    }

    /// <summary>Runs a backup immediately using the saved settings.</summary>
    [HttpPost("trigger")]
    [ProducesResponseType(typeof(TriggerBackupResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Trigger(CancellationToken ct)
    {
        var result = await _backupService.RunBackupAsync(ct);

        return Ok(new TriggerBackupResponse(
            result.Success,
            result.Filename,
            result.SizeBytes / 1024,
            result.Error));
    }

    [HttpGet("history")]
    [ProducesResponseType(typeof(IEnumerable<BackupHistoryEntry>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(CancellationToken ct)
        => Ok(await _backupService.ListHistoryAsync(ct));

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<BackupSetting> GetSettingsAsync(CancellationToken ct) =>
        await _db.BackupSettings.FirstOrDefaultAsync(x => x.Id == 1, ct)
            ?? throw new InvalidOperationException("BackupSettings not initialized.");

    private BackupSettingResponse ToResponse(BackupSetting s) => new(
        s.IsEnabled,
        s.DestinationType,
        s.LocalPath,
        s.S3Endpoint,
        s.S3Region,
        s.S3Bucket,
        s.S3AccessKey,
        MaskSecret(s.S3SecretKeyEncrypted is null ? null : _protector.Unprotect(s.S3SecretKeyEncrypted)),
        s.RetentionCount,
        s.ScheduleCron,
        _scheduler.GetNextScheduledRun());

    private static string? MaskSecret(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return null;
        var suffix = secret.Length > 4 ? secret[^4..] : secret;
        return $"···{suffix}";
    }
}
