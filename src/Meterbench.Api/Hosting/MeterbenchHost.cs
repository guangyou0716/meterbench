using System.Text.Json;
using System.Text.Json.Serialization;
using Meterbench.Api.Data;
using Meterbench.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Meterbench.Api.Hosting;

public static class MeterbenchHost
{
    public static void AddMeterbench(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Meterbench") ?? "Data Source=meterbench.db";
        services.AddDbContext<MeterbenchDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<FleetService>();
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        });
    }

    public static async Task InitialiseMeterbenchAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MeterbenchDbContext>();
        await db.Database.EnsureCreatedAsync();
        var fleet = scope.ServiceProvider.GetRequiredService<FleetService>();
        await fleet.EnsureSeededAsync();
    }
}
