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

        modelBuilder.Entity<Exclusion>()
            .HasIndex(e => e.MediaServerItemId)
            .HasDatabaseName("IX_Exclusions_MediaServerItemId");
    }
}
