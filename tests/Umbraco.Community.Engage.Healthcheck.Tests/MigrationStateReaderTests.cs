using Umbraco.Community.Engage.Healthcheck.Core.State;

namespace Umbraco.Community.Engage.Healthcheck.Tests;

[Collection("sql")]
public class MigrationStateReaderTests(SqlServerFixture sql)
{
    [Fact]
    public async Task Reads_both_keys()
    {
        var cs = await sql.CreateDatabaseAsync("state_" + Guid.NewGuid().ToString("N"));
        await SqlServerFixture.ExecuteAsync(cs, """
            CREATE TABLE umbracoKeyValue ([key] nvarchar(256) NOT NULL PRIMARY KEY, [value] nvarchar(max) NULL, updated datetime NOT NULL DEFAULT GETDATE());
            INSERT umbracoKeyValue ([key],[value]) VALUES ('Umbraco.Core.Upgrader.State+Umbraco.Engage', '{abc}'), ('Umbraco.Engage+DatabaseSchemaStatus', 'Aligned');
            """);
        var state = await MigrationStateReader.ReadAsync(cs);
        Assert.Equal(new MigrationState(true, "{abc}", "Aligned"), state);
    }

    [Fact]
    public async Task Reports_missing_table()
    {
        var cs = await sql.CreateDatabaseAsync("nostate_" + Guid.NewGuid().ToString("N"));
        Assert.Equal(new MigrationState(false, null, null), await MigrationStateReader.ReadAsync(cs));
    }

    [Fact]
    public async Task ReadKeys_returns_only_requested_keys_case_insensitively()
    {
        var cs = await sql.CreateDatabaseAsync("keys_" + Guid.NewGuid().ToString("N"));
        await SqlServerFixture.ExecuteAsync(cs, """
            CREATE TABLE umbracoKeyValue ([key] nvarchar(256) NOT NULL PRIMARY KEY, [value] nvarchar(max) NULL, updated datetime NOT NULL DEFAULT GETDATE());
            INSERT umbracoKeyValue ([key],[value]) VALUES ('a', '1'), ('b', '2'), ('c', NULL), ('other', 'x');
            """);
        var kv = await MigrationStateReader.ReadKeysAsync(cs, ["A", "c", "missing"]);
        Assert.True(kv.TableFound);
        Assert.Equal("1", kv.Get("a"));
        Assert.Null(kv.Get("c"));
        Assert.Null(kv.Get("other"));
        Assert.Equal(2, kv.Values.Count);
    }

    [Fact]
    public async Task ReadKeys_reports_table_not_found()
    {
        var cs = await sql.CreateDatabaseAsync("nokeys_" + Guid.NewGuid().ToString("N"));
        var kv = await MigrationStateReader.ReadKeysAsync(cs, ["a"]);
        Assert.False(kv.TableFound);
        Assert.Empty(kv.Values);
    }
}
