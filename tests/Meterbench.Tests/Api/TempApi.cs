using Meterbench.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meterbench.Tests.Api;

/// <summary>
/// Boots the API on the in-process test server. Each instance uses its own temporary SQLite file.
/// </summary>
public sealed class MeterbenchApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath;

    public MeterbenchApiFactory(string databasePath)
    {
        _databasePath = databasePath;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<MeterbenchDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<MeterbenchDbContext>();
            services.AddDbContext<MeterbenchDbContext>(options =>
                options.UseSqlite(
                    $"Data Source={_databasePath};Mode=ReadWriteCreate;Cache=Private;Pooling=False"));
        });
    }
}

public sealed class TempApi : IDisposable
{
    private readonly string _databasePath;
    private readonly MeterbenchApiFactory _factory;

    public TempApi()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), "meterbench-" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new MeterbenchApiFactory(_databasePath);
        Client = _factory.CreateClient();
    }

    public HttpClient Client { get; }

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        TryDelete(_databasePath);
        TryDelete(_databasePath + "-wal");
        TryDelete(_databasePath + "-shm");
    }

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string url)
    {
        return client.PostAsync(url, new StringContent(string.Empty));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
