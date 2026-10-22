using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Umbraco.Engage.HealthCheck.Tests;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public string ConnectionStringFor(string database)
        => new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = database, TrustServerCertificate = true }.ConnectionString;

    public async Task<string> CreateDatabaseAsync(string name)
    {
        await ExecuteAsync(ConnectionStringFor("master"), $"CREATE DATABASE [{name}]");
        return ConnectionStringFor(name);
    }

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition("sql")]
public sealed class SqlCollection : ICollectionFixture<SqlServerFixture>;
