using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Runlet.Shared.Executions;

namespace Runlet.Persistence.Configurations;

public sealed class WorkflowLogEntryConfiguration : IEntityTypeConfiguration<WorkflowLogEntry>
{
    public void Configure(EntityTypeBuilder<WorkflowLogEntry> builder)
    {
        builder.ToTable("workflow_log_entries");

        builder.HasKey(log => log.Id);

        builder.Property(log => log.Id)
            .HasColumnName("id");

        builder.Property(log => log.WorkflowRunId)
            .HasColumnName("workflow_run_id");

        builder.Property(log => log.WorkflowStepId)
            .HasColumnName("workflow_step_id");

        builder.Property(log => log.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(log => log.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(log => log.Message)
            .HasColumnName("message")
            .IsRequired();

        builder.HasIndex(log => new { log.WorkflowRunId, log.CreatedAt })
            .HasDatabaseName("ix_workflow_log_entries_workflow_run_id_created_at");
    }
}
