using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Runlet.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddRunletPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Runlet")
            ?? throw new InvalidOperationException("Connection string 'Runlet' is not configured.");

        services.AddDbContext<RunletDbContext>(options =>
            options.UseNpgsql(connectionString));

        return services;
    }
}
