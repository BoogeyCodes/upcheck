using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Upcheck.Api.Domain;

namespace Upcheck.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Monitor> Monitors => Set<Monitor>();
    public DbSet<Check> Checks => Set<Check>();
    public DbSet<Incident> Incidents => Set<Incident>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Monitor>(e =>
        {
            e.Property(m => m.Name).HasMaxLength(120).IsRequired();
            e.Property(m => m.Url).HasMaxLength(2048).IsRequired();
            e.Property(m => m.Status).HasConversion<string>().HasMaxLength(10);
        });

        b.Entity<Check>(e =>
        {
            e.Property(c => c.Error).HasMaxLength(500);
            e.HasIndex(c => new { c.MonitorId, c.CheckedAt });
            e.HasOne(c => c.Monitor).WithMany(m => m.Checks)
                .HasForeignKey(c => c.MonitorId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Incident>(e =>
        {
            e.Property(i => i.Cause).HasMaxLength(500).IsRequired();
            e.HasIndex(i => new { i.MonitorId, i.ResolvedAt });
            e.HasOne(i => i.Monitor).WithMany(m => m.Incidents)
                .HasForeignKey(i => i.MonitorId).OnDelete(DeleteBehavior.Cascade);
        });

        ApplyDateTimeOffsetConverter(b);
    }

    // SQLite can't ORDER BY or compare DateTimeOffset, so store UTC ticks (a sortable INTEGER).
    private static void ApplyDateTimeOffsetConverter(ModelBuilder modelBuilder)
    {
        var converter = new ValueConverter<DateTimeOffset, long>(
            v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        var nullable = new ValueConverter<DateTimeOffset?, long?>(
            v => v.HasValue ? v.Value.UtcTicks : null,
            v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(converter);
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(nullable);
                }
            }
        }
    }
}
