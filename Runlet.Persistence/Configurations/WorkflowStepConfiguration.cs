using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runlet.Shared.Workflows;

namespace Runlet.Persistence.Configurations;

public sealed class WorkflowStepConfiguration : IEntityTypeConfiguration<WorkflowStep>
{
    public void Configure(EntityTypeBuilder<WorkflowStep> builder)
    {
        builder.ToTable("workflow_steps");

        builder.HasKey(step => step.Id);

        builder.Property(step => step.Id)
            .HasColumnName("id");

        builder.Property(step => step.WorkflowRunId)
            .HasColumnName("workflow_run_id");

        builder.Property(step => step.Order)
            .HasColumnName("order");

        builder.Property(step => step.Command)
            .HasColumnName("command")
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(step => step.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(step => step.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired();

        builder.Property(step => step.StartedAt)
            .HasColumnName("started_at");

        builder.Property(step => step.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(step => step.ExitCode)
            .HasColumnName("exit_code");

        builder.HasIndex(step => new { step.WorkflowRunId, step.Order })
            .HasDatabaseName("ix_workflow_steps_workflow_run_id_order")
            .IsUnique();
    }
}
