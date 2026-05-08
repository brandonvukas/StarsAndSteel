using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StarsAndSteel.Data;

namespace StarsAndSteel.Tests.Integration;

/// <summary>
/// WebApplicationFactory wired against the Testcontainers SQL Server from the
/// fixture. Overrides Jwt:* and the connection string before the host builds,
/// applies migrations, and exposes a fresh database per test class.
/// </summary>
public sealed class StarsAndSteelWebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public StarsAndSteelWebAppFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:StarsAndSteelDb"] = _connectionString,

                // Deterministic 64-byte base64 key — only used in tests.
                ["Jwt:Key"] = "dGVzdC1qd3Qta2V5LXRlc3Qtand0LWtleS10ZXN0LWp3dC1rZXktdGVzdC1qd3Qta2V5LXRlc3RrZXkxMjM0NTY=",
                ["Jwt:Issuer"] = "stars-and-steel-test",
                ["Jwt:Audience"] = "stars-and-steel-test",
                ["Jwt:AccessTokenLifetimeMinutes"] = "15",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Run migrations once when the host starts so every test sees a real schema.
            using var scope = services
                .BuildServiceProvider()
                .CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<StarsAndSteelDbContext>();
            db.Database.Migrate();
        });
    }
}
