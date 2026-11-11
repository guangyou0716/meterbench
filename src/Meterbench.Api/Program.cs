using Meterbench.Api.Endpoints;
using Meterbench.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMeterbench(builder.Configuration);

var app = builder.Build();

await app.InitialiseMeterbenchAsync();

app.MapMeterbenchEndpoints();

app.Run();

/// <summary>
/// Exposed so WebApplicationFactory in the test project can boot the API in process.
/// </summary>
public partial class Program
{
}
