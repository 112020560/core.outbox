using Microsoft.EntityFrameworkCore;
using SmartCore.Outbox.Models;

namespace SmartCore.Outbox.Infrastructure;

public class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : DbContext(options)
{
    public DbSet<OutboxEvent> Events => Set<OutboxEvent>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxEvent>(entity =>
        {
            entity.ToTable("Events");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnType("uuid");
            entity.Property(e => e.ServiceName).HasMaxLength(128).IsRequired();
            entity.Property(e => e.DeduplicationKey).HasMaxLength(256);
            entity.Property(e => e.AggregateId).HasColumnType("uuid").IsRequired();
            entity.Property(e => e.AggregateType).HasMaxLength(128);
            entity.Property(e => e.EventType).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.OccurredAt).HasColumnType("timestamptz").IsRequired();

            entity.Property<string>("Status")
                .HasMaxLength(32)
                .IsRequired()
                .HasDefaultValue("Pending");

            entity.Property<string?>("ClaimedBy").HasMaxLength(128);
            entity.Property<DateTimeOffset?>("ClaimedAt").HasColumnType("timestamptz");
            entity.Property<DateTimeOffset?>("PublishedAt").HasColumnType("timestamptz");
            entity.Property<int>("RetryCount").IsRequired().HasDefaultValue(0);
            entity.Property<string?>("LastError");

            entity.HasIndex(e => e.DeduplicationKey)
                .IsUnique()
                .HasFilter("\"DeduplicationKey\" IS NOT NULL");
        });

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.ToTable("ProcessedEvents");
            entity.HasKey(e => new { e.EventId, e.ConsumerName });

            entity.Property(e => e.EventId).HasColumnType("uuid").IsRequired();
            entity.Property(e => e.ConsumerName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.ProcessedAt).HasColumnType("timestamptz").IsRequired();
        });
    }
}
