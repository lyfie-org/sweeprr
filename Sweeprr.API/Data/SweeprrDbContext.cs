using Microsoft.EntityFrameworkCore;
using Sweeprr.API.Models;

namespace Sweeprr.API.Data;

public class SweeprrDbContext(DbContextOptions<SweeprrDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<GlobalSettings> GlobalSettings => Set<GlobalSettings>();
    public DbSet<ServerConnection> ServerConnections => Set<ServerConnection>();
    public DbSet<RuleGroup> RuleGroups => Set<RuleGroup>();
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<SweepItem> SweepItems => Set<SweepItem>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<Exclusion> Exclusions => Set<Exclusion>();
    public DbSet<TagExclusion> TagExclusions => Set<TagExclusion>();
    public DbSet<PlaybackActivity> PlaybackActivities => Set<PlaybackActivity>();
    public DbSet<SweeprrApiKey> SweeprrApiKeys => Set<SweeprrApiKey>();
    public DbSet<NotificationSetting> NotificationSettings => Set<NotificationSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Single-row guarantee for GlobalSettings
        modelBuilder.Entity<GlobalSettings>(e =>
        {
            e.ToTable(t => t.HasCheckConstraint("CK_GlobalSettings_SingleRow", "Id = 1"));
        });

        // RuleGroup → Rules cascade
        modelBuilder.Entity<RuleGroup>()
            .HasMany(rg => rg.Rules)
            .WithOne(r => r.RuleGroup)
            .HasForeignKey(r => r.RuleGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // RuleGroup → SweepItems cascade
        modelBuilder.Entity<RuleGroup>()
            .HasMany(rg => rg.SweepItems)
            .WithOne(s => s.RuleGroup)
            .HasForeignKey(s => s.RuleGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // Performance indices
        modelBuilder.Entity<SweepItem>()
            .HasIndex(s => s.Status)
            .HasDatabaseName("IX_SweepItems_Status");

        modelBuilder.Entity<SweepItem>()
            .HasIndex(s => new { s.RuleGroupId, s.MediaServerItemId })
            .HasDatabaseName("IX_SweepItems_GroupItem");

        modelBuilder.Entity<SweepItem>()
            .HasIndex(s => new { s.RuleGroupId, s.Status })
            .HasDatabaseName("IX_SweepItems_GroupStatus");

        modelBuilder.Entity<ActivityLog>()
            .HasIndex(a => a.Timestamp)
            .HasDatabaseName("IX_ActivityLogs_Timestamp");

        modelBuilder.Entity<ActivityLog>()
            .HasIndex(a => a.Category)
            .HasDatabaseName("IX_ActivityLogs_Category");

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique()
            .HasDatabaseName("IX_Users_Username");

        modelBuilder.Entity<Exclusion>(e =>
        {
            e.HasIndex(x => x.MediaServerItemId)
                .HasDatabaseName("IX_Exclusions_MediaServerItemId");

            // Scoped exclusion FK — SET NULL when rule group deleted (keep the exclusion as global)
            e.HasOne(x => x.RuleGroup)
                .WithMany()
                .HasForeignKey(x => x.RuleGroupId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TagExclusion>(e =>
        {
            e.HasIndex(x => x.ServerConnectionId)
                .HasDatabaseName("IX_TagExclusions_ServerConnectionId");

            e.HasOne(x => x.ServerConnection)
                .WithMany()
                .HasForeignKey(x => x.ServerConnectionId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.RuleGroup)
                .WithMany()
                .HasForeignKey(x => x.RuleGroupId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlaybackActivity>(e =>
        {
            e.HasIndex(p => new { p.MediaServerItemId, p.UserId })
                .HasDatabaseName("IX_PlaybackActivities_MediaServerItemId_UserId");
            e.HasIndex(p => p.LastWatched)
                .HasDatabaseName("IX_PlaybackActivities_LastWatched");
        });

        modelBuilder.Entity<SweeprrApiKey>()
            .HasIndex(k => k.HashedKey)
            .IsUnique()
            .HasDatabaseName("IX_SweeprrApiKeys_HashedKey");
    }
}
