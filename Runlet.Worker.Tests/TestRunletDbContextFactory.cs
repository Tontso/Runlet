using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Runlet.Persistence;

namespace Runlet.Worker.Tests;

internal static class TestRunletDbContextFactory
{
    public static RunletDbContext Create()
    {
        return Create(
            $"runlet-worker-tests-{Guid.NewGuid()}",
            new InMemoryDatabaseRoot());
    }

    public static RunletDbContext Create(string databaseName, InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<RunletDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;

        var dbContext = new RunletDbContext(options);
        dbContext.Database.EnsureCreated();

        return dbContext;
    }
}
