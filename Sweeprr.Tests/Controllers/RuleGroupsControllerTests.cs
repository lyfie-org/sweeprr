using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Background;
using Sweeprr.API.Controllers;
using Sweeprr.API.Data;
using Sweeprr.API.Dtos.Rules;
using Sweeprr.API.Integrations;
using Sweeprr.API.Integrations.Bazarr;
using Sweeprr.API.Integrations.Jellyfin;
using Sweeprr.API.Integrations.Jellyfin.Models;
using Sweeprr.API.Integrations.Radarr;
using Sweeprr.API.Integrations.Sonarr;
using Sweeprr.API.Models;
using Sweeprr.API.Services;
using Sweeprr.API.Services.Rules;

namespace Sweeprr.Tests.Controllers;

public class RuleGroupsControllerTests : IDisposable
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

    // ── Export ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_ReturnsEnvelope_WithSchemaVersionAndContentDisposition()
    {
        var (controller, db) = CreateController();
        var group = await SeedGroupAsync(db, "My 4K Movies");

        var result = await controller.Export(group.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        var envelope = Assert.IsType<RuleGroupExportEnvelope>(ok.Value);

        Assert.Equal("1.1", envelope.SchemaVersion);
        Assert.Equal("My 4K Movies", envelope.RuleGroup.Name);
        Assert.Equal(MediaType.Movie, envelope.RuleGroup.MediaType);
        Assert.Equal(SweepAction.DeleteAndUnmonitor, envelope.RuleGroup.Action);
        Assert.Equal(2, envelope.RuleGroup.Rules.Count);

        var disposition = controller.ControllerContext.HttpContext.Response.Headers.ContentDisposition.ToString();
        Assert.Equal("attachment; filename=\"rulegroup-my-4k-movies.json\"", disposition);
    }

    [Fact]
    public async Task Export_ReturnsNotFound_WhenGroupMissing()
    {
        var (controller, _) = CreateController();

        var result = await controller.Export(999);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── Import ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_CreatesDisabledGroupWithNewId_AndStripsConnectionSpecificFields()
    {
        var (controller, db) = CreateController();
        var original = await SeedGroupAsync(db, "Original Group");

        var exportResult = await controller.Export(original.Id);
        var envelope = Assert.IsType<RuleGroupExportEnvelope>(Assert.IsType<OkObjectResult>(exportResult).Value);

        var importResult = await controller.Import(envelope);

        var created = Assert.IsType<CreatedAtActionResult>(importResult);
        var response = Assert.IsType<RuleGroupResponse>(created.Value);

        Assert.NotEqual(original.Id, response.Id);
        Assert.Equal("Original Group", response.Name);
        Assert.False(response.IsEnabled);
        Assert.Null(response.CronOverride);
        Assert.Null(response.TargetQualityProfileId);
        Assert.Null(response.TargetQualityProfileName);
        Assert.Equal(2, response.Conditions.Count);

        var entity = await db.RuleGroups.AsNoTracking().FirstAsync(g => g.Id == response.Id);
        Assert.False(entity.IsEnabled);
    }

    [Fact]
    public async Task ExportImport_RoundTrip_PreservesRuleConditions()
    {
        var (controller, db) = CreateController();
        var original = await SeedGroupAsync(db, "Round Trip Group");

        var exportResult = await controller.Export(original.Id);
        var envelope = Assert.IsType<RuleGroupExportEnvelope>(Assert.IsType<OkObjectResult>(exportResult).Value);

        var importResult = await controller.Import(envelope);
        var response = Assert.IsType<RuleGroupResponse>(Assert.IsType<CreatedAtActionResult>(importResult).Value);

        for (var i = 0; i < envelope.RuleGroup.Rules.Count; i++)
        {
            var expected = envelope.RuleGroup.Rules[i];
            var actual = response.Conditions[i];

            Assert.Equal(expected.Section, actual.Section);
            Assert.Equal(expected.LogicalOperator, actual.LogicalOperator);
            Assert.Equal(expected.Field, actual.Field);
            Assert.Equal(expected.Comparator, actual.Comparator);
            Assert.Equal(expected.Value, actual.Value);
            Assert.Equal(expected.ValueType, actual.ValueType);
        }
    }

    [Fact]
    public async Task Import_RejectsWrongSchemaVersion()
    {
        var (controller, db) = CreateController();
        var envelope = BuildEnvelope("2.0", MediaType.Movie,
            new RuleConditionDto { Section = 0, Field = RuleField.PlayCount, Comparator = RuleComparator.Equals, Value = "0", ValueType = RuleValueType.Number });

        var result = await controller.Import(envelope);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await db.RuleGroups.ToListAsync());
    }

    [Fact]
    public async Task Import_RejectsFieldNotApplicableToMediaType()
    {
        var (controller, db) = CreateController();

        // SeriesEnded is TV-only — invalid for a Movie rule group.
        var envelope = BuildEnvelope("1.1", MediaType.Movie,
            new RuleConditionDto { Section = 0, Field = RuleField.SeriesEnded, Comparator = RuleComparator.Equals, Value = "true", ValueType = RuleValueType.Bool });

        var result = await controller.Import(envelope);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        Assert.Empty(await db.RuleGroups.ToListAsync());
    }

    // ── Test infrastructure ─────────────────────────────────────────────────

    private (RuleGroupsController Controller, SweeprrDbContext Db) CreateController()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sweeprr_rulegroups_{Guid.NewGuid()}.db");
        _dbPaths.Add(dbPath);
        var connectionString = $"Data Source={dbPath}";

        var services = new ServiceCollection();
        services.AddDbContext<SweeprrDbContext>(o => o.UseSqlite(connectionString));
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        var db = new SweeprrDbContext(new DbContextOptionsBuilder<SweeprrDbContext>()
            .UseSqlite(connectionString)
            .Options);
        db.Database.Migrate();

        var scheduler = new SchedulerHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeScanPipeline(),
            new FakeSessionAlertService(),
            NullLogger<SchedulerHostedService>.Instance);

        var controller = new RuleGroupsController(
            db,
            new RuleValidationService(),
            scheduler,
            new FakeIntegrationClientFactory(),
            new FakeMediaPopulationService(),
            new FakeRuleEvaluator())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        return (controller, db);
    }

    private static RuleGroupExportEnvelope BuildEnvelope(
        string schemaVersion, MediaType mediaType, params RuleConditionDto[] rules)
        => new(schemaVersion, DateTimeOffset.UtcNow,
            new ExportedRuleGroupDto("Imported Group", "Imported description", mediaType, SweepAction.DeleteAndUnmonitor, rules));

    private static async Task<RuleGroup> SeedGroupAsync(SweeprrDbContext db, string name)
    {
        var group = new RuleGroup
        {
            Name = name,
            Description = "A test group",
            MediaType = MediaType.Movie,
            IsEnabled = true,
            Action = SweepAction.DeleteAndUnmonitor,
            CronOverride = "0 4 * * *",
            TargetQualityProfileId = 7,
            TargetQualityProfileName = "HD-1080p",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Rules = new List<Rule>
            {
                new() { Section = 0, LogicalOperator = null, Field = RuleField.PlayCount, Comparator = RuleComparator.Equals, Value = "0", ValueType = RuleValueType.Number },
                new() { Section = 0, LogicalOperator = LogicalOperator.And, Field = RuleField.LastWatched, Comparator = RuleComparator.InLastDays, Value = "30", ValueType = RuleValueType.RelativeDays },
            },
        };

        db.RuleGroups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    private sealed class FakeScanPipeline : IScanPipeline
    {
        public Task<ScanResult> ExecuteAsync(int ruleGroupId, CancellationToken ct = default)
            => Task.FromResult(new ScanResult(ruleGroupId, "Test Group", 0, TimeSpan.Zero));
    }

    private sealed class FakeSessionAlertService : IJellyfinSessionAlertService
    {
        public Task ProcessSessionsUpdateAsync(int connectionId, IReadOnlyList<JellyfinSession> sessions, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task BroadcastPreSweepWarningAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeIntegrationClientFactory : IIntegrationClientFactory
    {
        public Task<JellyfinClient?> CreateJellyfinClientAsync(int connectionId, CancellationToken ct = default)
            => Task.FromResult<JellyfinClient?>(null);

        public Task<RadarrClient?> CreateRadarrClientAsync(int connectionId, CancellationToken ct = default)
            => Task.FromResult<RadarrClient?>(null);

        public Task<SonarrClient?> CreateSonarrClientAsync(int connectionId, CancellationToken ct = default)
            => Task.FromResult<SonarrClient?>(null);

        public Task<BazarrClient?> CreateBazarrClientAsync(CancellationToken ct = default)
            => Task.FromResult<BazarrClient?>(null);
    }

    private sealed class FakeMediaPopulationService : IMediaPopulationService
    {
        public Task<IReadOnlyList<MediaContext>> PopulateAsync(RuleGroup group, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MediaContext>>([]);
    }

    private sealed class FakeRuleEvaluator : IRuleEvaluator
    {
        public Task<IReadOnlyList<EvaluationResult>> EvaluateAsync(
            RuleGroup group, IReadOnlyList<MediaContext> items, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EvaluationResult>>([]);

        public Task<IReadOnlyList<RuleGroupTrace>> TraceAsync(
            MediaContext item, IEnumerable<RuleGroup> groups, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RuleGroupTrace>>([]);
    }
}
