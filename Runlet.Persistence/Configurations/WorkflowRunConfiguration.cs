using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runlet.Shared.Workflows;

namespace Runlet.Persistence.Configurations;

public sealed class WorkflowRunConfiguration : IEntityTypeConfiguration<WorkflowRun>
{
    public void Configure(EntityTypeBuilder<WorkflowRun> builder)
    {
        builder.ToTable("workflow_runs");

        builder.HasKey(run => run.Id);

        builder.Property(run => run.Id)
            .HasColumnName("id");

        builder.Property(run => run.Image)
            .HasColumnName("image")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(run => run.ExecutionMode)
            .HasColumnName("execution_mode")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(run => run.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(run => run.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(run => run.StartedAt)
            .HasColumnName("started_at");

        builder.Property(run => run.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(run => run.ClaimedByWorkerId)
            .HasColumnName("claimed_by_worker_id")
            .HasMaxLength(200);

        builder.Property(run => run.ClaimedAt)
            .HasColumnName("claimed_at");

        builder.HasMany(run => run.Steps)
            .WithOne()
            .HasForeignKey(step => step.WorkflowRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(run => new { run.Status, run.CreatedAt })
            .HasDatabaseName("ix_workflow_runs_status_created_at");
    }
}
