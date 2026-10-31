using GuardaCat.Server.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GuardaCat.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string databaseName = $"guardacat_auth_tests_{Guid.NewGuid():N}";
    private string adminConnection = string.Empty;
    private string testConnection = string.Empty;
    private bool created;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("GUARDACAT_TEST_POSTGRES")
            ?? throw new InvalidOperationException(
                "Define GUARDACAT_TEST_POSTGRES con una conexión PostgreSQL local que permita crear bases temporales.");
        var builder = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" };
        adminConnection = builder.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
        created = true;
        builder.Database = databaseName;
        testConnection = builder.ConnectionString;

        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(testConnection).Options);
        await db.Database.MigrateAsync();
    }

    public WebApplicationFactory<Program> CreateHost()
    {
        var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Postgres", testConnection);
            builder.ConfigureLogging(logging => logging.ClearProviders());
        });
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (db.Database.GetDbConnection().Database != databaseName)
        {
            host.Dispose();
            throw new InvalidOperationException("El host de pruebas debe usar exclusivamente la base temporal.");
        }
        return host;
    }

    public async Task DisposeAsync()
    {
        if (!created) return;
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        // The identifier is generated here, never supplied by configuration or by a test.
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
