using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runlet.Shared.Workers;

namespace Runlet.Persistence.Configurations;

public sealed class WorkerRegistrationConfiguration : IEntityTypeConfiguration<WorkerRegistration>
{
    public void Configure(EntityTypeBuilder<WorkerRegistration> builder)
    {
        builder.ToTable("worker_registrations");

        builder.HasKey(worker => worker.WorkerId);

        builder.Property(worker => worker.WorkerId)
            .HasColumnName("worker_id")
            .HasMaxLength(200);

        builder.Property(worker => worker.MachineName)
            .HasColumnName("machine_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(worker => worker.MaxConcurrentRuns)
            .HasColumnName("max_concurrent_runs")
            .IsRequired();

        builder.Property(worker => worker.StartedAt)
            .HasColumnName("started_at");

        builder.Property(worker => worker.LastHeartbeatAt)
            .HasColumnName("last_heartbeat_at");

        builder.Property(worker => worker.StoppedAt)
            .HasColumnName("stopped_at");

        builder.HasIndex(worker => worker.LastHeartbeatAt)
            .HasDatabaseName("ix_worker_registrations_last_heartbeat_at");
    }
}
