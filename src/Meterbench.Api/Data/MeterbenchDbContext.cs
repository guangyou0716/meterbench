using Microsoft.EntityFrameworkCore;

namespace Meterbench.Api.Data;

public sealed class MeterbenchDbContext : DbContext
{
    public MeterbenchDbContext(DbContextOptions<MeterbenchDbContext> options)
        : base(options)
    {
    }

    public DbSet<ReadingRow> Readings => Set<ReadingRow>();

    public DbSet<AlarmRow> Alarms => Set<AlarmRow>();

    public DbSet<SimulatorStateRow> SimulatorStates => Set<SimulatorStateRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReadingRow>(entity =>
        {
            entity.ToTable("readings");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.DeviceId).HasMaxLength(32).IsRequired();
            entity.Property(row => row.Quantity).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Unit).HasMaxLength(16).IsRequired();
            entity.Property(row => row.Obis).HasMaxLength(32).IsRequired();
            entity.HasIndex(row => new { row.DeviceId, row.Tick });
        });

        modelBuilder.Entity<AlarmRow>(entity =>
        {
            entity.ToTable("alarms");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.DeviceId).HasMaxLength(32).IsRequired();
            entity.Property(row => row.Quantity).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Direction).HasMaxLength(8).IsRequired();
            entity.HasIndex(row => new { row.DeviceId, row.Quantity, row.IsActive });
        });

        modelBuilder.Entity<SimulatorStateRow>(entity =>
        {
            entity.ToTable("simulator_state");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
        });
    }
}
