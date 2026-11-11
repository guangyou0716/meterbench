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
        services.AddSingleton<SimulatorWriteGate>();
        services.AddSingleton<SimulatorClock>();
        services.AddOptions<ModbusSimulatorOptions>()
            .Bind(configuration.GetSection(ModbusSimulatorOptions.SectionName))
            .Validate(o => o.Port is >= 0 and <= 65535, "Meterbench:Modbus:Port must be 0..65535.")
            .ValidateOnStart();
        services.AddSingleton<ModbusSimulatorHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<ModbusSimulatorHostedService>());
        services.AddOptions<MqttPublisherOptions>()
            .Bind(configuration.GetSection(MqttPublisherOptions.SectionName))
            .Validate(o => o.Validate() is null, "Meterbench:Mqtt settings are invalid (loopback IP host, port 1..65535, client id 1..23 letters/digits, topic prefix without wildcards, timings >= 10 ms).")
            .ValidateOnStart();
        services.AddSingleton<MqttPublisherHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<MqttPublisherHostedService>());
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
        await fleet.SyncClockAsync();
    }
}
