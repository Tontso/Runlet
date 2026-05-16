using Microsoft.EntityFrameworkCore;
using Runlet.Shared.Executions;
using Runlet.Shared.Workers;
using Runlet.Shared.Workflows;

namespace Runlet.Persistence;

public sealed class RunletDbContext(DbContextOptions<RunletDbContext> options) : DbContext(options)
{
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();

    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();

    public DbSet<WorkflowLogEntry> WorkflowLogEntries => Set<WorkflowLogEntry>();

    public DbSet<WorkerRegistration> WorkerRegistrations => Set<WorkerRegistration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RunletDbContext).Assembly);
    }
}
