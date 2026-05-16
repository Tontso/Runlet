using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Runlet.Persistence;
using Runlet.Shared.Workflows;
using Runlet.Worker.Claiming;
using Xunit;

namespace Runlet.Worker.Tests;

public sealed class PostgresWorkflowRunClaimerTests
{
    private const string ConnectionStringEnvironmentVariable = "RUNLET_TEST_CONNECTION_STRING";

    [Fact]
    public async Task TryClaimNextRunAsync_WithConcurrentClaimers_ClaimsDifferentRuns()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            return;
        }

        var schemaName = $"runlet_test_{Guid.NewGuid():N}";
        var connectionString = BuildSchemaConnectionString(baseConnectionString, schemaName);

        await CreateSchemaAsync(baseConnectionString, schemaName);

        try
        {
            await using (var setupDbContext = CreateDbContext(connectionString))
            {
                await setupDbContext.Database.MigrateAsync();

                setupDbContext.WorkflowRuns.AddRange(
                    CreatePendingRun("first"),
                    CreatePendingRun("second"));
                await setupDbContext.SaveChangesAsync();
            }

            var claimer = new WorkflowRunClaimer(NullLogger<WorkflowRunClaimer>.Instance);
            await using var workerOneDbContext = CreateDbContext(connectionString);
            await using var workerTwoDbContext = CreateDbContext(connectionString);

            var claimOneTask = claimer.TryClaimNextRunAsync(
                workerOneDbContext,
                "worker-one",
                CancellationToken.None);
            var claimTwoTask = claimer.TryClaimNextRunAsync(
                workerTwoDbContext,
                "worker-two",
                CancellationToken.None);

            var claimedRuns = await Task.WhenAll(claimOneTask, claimTwoTask);

            Assert.All(claimedRuns, Assert.NotNull);
            Assert.NotEqual(claimedRuns[0]!.Id, claimedRuns[1]!.Id);
            Assert.Equal(
                ["worker-one", "worker-two"],
                claimedRuns.Select(run => run!.ClaimedByWorkerId).Order());

            await using var verifyDbContext = CreateDbContext(connectionString);
            var persistedRuns = await verifyDbContext.WorkflowRuns
                .OrderBy(run => run.Name)
                .ToListAsync();

            Assert.All(persistedRuns, run => Assert.Equal(WorkflowRunStatus.Running, run.Status));
            Assert.Equal(2, persistedRuns.Select(run => run.ClaimedByWorkerId).Distinct().Count());
        }
        finally
        {
            await DropSchemaAsync(baseConnectionString, schemaName);
        }
    }

    private static RunletDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<RunletDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new RunletDbContext(options);
    }

    private static WorkflowRun CreatePendingRun(string name)
    {
        var runId = Guid.NewGuid();

        return new WorkflowRun
        {
            Id = runId,
            Name = name,
            Image = "alpine:latest",
            Status = WorkflowRunStatus.Pending,
            Steps =
            [
                new WorkflowStep
                {
                    WorkflowRunId = runId,
                    Order = 1,
                    Command = "echo hello"
                }
            ]
        };
    }

    private static string BuildSchemaConnectionString(
        string baseConnectionString,
        string schemaName)
    {
        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            SearchPath = schemaName
        };

        return builder.ConnectionString;
    }

    private static async Task CreateSchemaAsync(
        string connectionString,
        string schemaName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""CREATE SCHEMA "{schemaName}";""";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropSchemaAsync(
        string connectionString,
        string schemaName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""DROP SCHEMA IF EXISTS "{schemaName}" CASCADE;""";
        await command.ExecuteNonQueryAsync();
    }
}
