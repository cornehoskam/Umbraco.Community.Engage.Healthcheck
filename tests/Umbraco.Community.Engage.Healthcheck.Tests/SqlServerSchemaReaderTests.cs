using Umbraco.Community.Engage.Healthcheck.Core.Schema;

namespace Umbraco.Community.Engage.Healthcheck.Tests;

[Collection("sql")]
public class SqlServerSchemaReaderTests(SqlServerFixture sql)
{
    private const string Fixture = """
        CREATE TABLE umbracoEngageParent (
            id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_umbracoEngageParent PRIMARY KEY CLUSTERED,
            [key] uniqueidentifier NOT NULL CONSTRAINT UQ_umbracoEngageParent_key UNIQUE NONCLUSTERED,
            name nvarchar(255) NULL DEFAULT (''),
            amount decimal(18,2) NOT NULL CONSTRAINT DF_umbracoEngageParent_amount DEFAULT (0));
        CREATE TABLE umbracoEngageChild (
            id int NOT NULL,
            parentId int NOT NULL,
            contentTypeId int NULL,
            CONSTRAINT PK_umbracoEngageChild PRIMARY KEY NONCLUSTERED (id),
            CONSTRAINT FK_umbracoEngageChild_umbracoEngageParent FOREIGN KEY (parentId) REFERENCES umbracoEngageParent(id) ON DELETE CASCADE,
            CONSTRAINT CK_umbracoEngageChild_contentTypeId CHECK (contentTypeId > 0));
        CREATE NONCLUSTERED INDEX IX_umbracoEngageChild_parentId ON umbracoEngageChild (parentId DESC) INCLUDE (contentTypeId) WHERE contentTypeId IS NOT NULL;
        CREATE TABLE cmsNotEngage (id int NOT NULL PRIMARY KEY);
        """;

    private async Task<DatabaseSchema> ReadFixtureAsync()
    {
        var cs = await sql.CreateDatabaseAsync("reader_" + Guid.NewGuid().ToString("N"));
        await SqlServerFixture.ExecuteAsync(cs, Fixture);
        return await SqlServerSchemaReader.ReadAsync(cs);
    }

    [Fact]
    public async Task Reads_only_engage_tables_sorted_by_name()
    {
        var db = await ReadFixtureAsync();
        Assert.Equal(["umbracoEngageChild", "umbracoEngageParent"], db.Tables.Select(t => t.Name));
    }

    [Fact]
    public async Task Reads_columns_types_nullability_identity_defaults()
    {
        var parent = (await ReadFixtureAsync()).Tables.Single(t => t.Name == "umbracoEngageParent");
        Assert.Equal(["id", "key", "name", "amount"], parent.Columns.Select(c => c.Name));
        var id = parent.Columns[0];
        Assert.True(id.IsIdentity);
        Assert.Equal("int", id.DataType);
        var name = parent.Columns[2];
        Assert.Equal("nvarchar(255)", name.DataType);
        Assert.True(name.IsNullable);
        Assert.NotNull(name.Collation);
        Assert.Null(name.Default!.Name);                 // system named
        Assert.Equal("('')", name.Default.Definition);
        var amount = parent.Columns[3];
        Assert.Equal("decimal(18,2)", amount.DataType);
        Assert.Equal("DF_umbracoEngageParent_amount", amount.Default!.Name);
        Assert.Equal("((0))", amount.Default.Definition);
    }

    [Fact]
    public async Task Reads_keys_and_excludes_their_backing_indexes()
    {
        var db = await ReadFixtureAsync();
        var parent = db.Tables.Single(t => t.Name == "umbracoEngageParent");
        Assert.Equal("PK_umbracoEngageParent", parent.PrimaryKey!.Name);
        Assert.Equal(["id"], parent.PrimaryKey.Columns);
        Assert.True(parent.PrimaryKey.IsClustered);
        var uq = Assert.Single(parent.UniqueConstraints);
        Assert.Equal("UQ_umbracoEngageParent_key", uq.Name);
        Assert.Equal(["key"], uq.Columns);
        Assert.Empty(parent.Indexes);
        var child = db.Tables.Single(t => t.Name == "umbracoEngageChild");
        Assert.False(child.PrimaryKey!.IsClustered);
    }

    [Fact]
    public async Task Reads_foreign_keys_with_actions()
    {
        var child = (await ReadFixtureAsync()).Tables.Single(t => t.Name == "umbracoEngageChild");
        var fk = Assert.Single(child.ForeignKeys);
        Assert.Equal("FK_umbracoEngageChild_umbracoEngageParent", fk.Name);
        Assert.Equal(["parentId"], fk.Columns);
        Assert.Equal("umbracoEngageParent", fk.ReferencedTable);
        Assert.Equal(["id"], fk.ReferencedColumns);
        Assert.Equal("CASCADE", fk.DeleteAction);
        Assert.Equal("NO_ACTION", fk.UpdateAction);
    }

    [Fact]
    public async Task Reads_indexes_with_order_includes_and_filter()
    {
        var child = (await ReadFixtureAsync()).Tables.Single(t => t.Name == "umbracoEngageChild");
        var ix = Assert.Single(child.Indexes);
        Assert.Equal("IX_umbracoEngageChild_parentId", ix.Name);
        Assert.False(ix.IsClustered);
        Assert.False(ix.IsUnique);
        Assert.Equal("parentId", Assert.Single(ix.KeyColumns).Name);
        Assert.True(ix.KeyColumns[0].Descending);
        Assert.Equal(["contentTypeId"], ix.IncludedColumns);
        Assert.Equal("([contentTypeId] IS NOT NULL)", ix.Filter);
    }

    [Fact]
    public async Task Reads_check_constraints()
    {
        var child = (await ReadFixtureAsync()).Tables.Single(t => t.Name == "umbracoEngageChild");
        var ck = Assert.Single(child.CheckConstraints);
        Assert.Equal("CK_umbracoEngageChild_contentTypeId", ck.Name);
        Assert.Equal("([contentTypeId]>(0))", ck.Definition);
    }

    [Fact]
    public async Task Works_with_read_only_permissions()
    {
        var name = "ro_" + Guid.NewGuid().ToString("N");
        var cs = await sql.CreateDatabaseAsync(name);
        await SqlServerFixture.ExecuteAsync(cs, Fixture);
        var password = "Ro!" + Guid.NewGuid().ToString("N");
        await SqlServerFixture.ExecuteAsync(sql.ConnectionStringFor("master"), $"CREATE LOGIN [{name}] WITH PASSWORD = '{password}', CHECK_POLICY = OFF");
        await SqlServerFixture.ExecuteAsync(cs, $"CREATE USER [{name}] FOR LOGIN [{name}]; ALTER ROLE db_datareader ADD MEMBER [{name}]; GRANT VIEW DEFINITION TO [{name}];");
        var roCs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(cs) { UserID = name, Password = password }.ConnectionString;

        var db = await SqlServerSchemaReader.ReadAsync(roCs);

        Assert.Equal(2, db.Tables.Count);
        Assert.Single(db.Tables.Single(t => t.Name == "umbracoEngageChild").Indexes);
    }

    private const string Fixture2 = """
        CREATE TABLE umbracoEngageRef (
            a int NOT NULL, b int NOT NULL, c int NOT NULL, d int NOT NULL,
            PRIMARY KEY NONCLUSTERED (b, a),
            UNIQUE (c, d),
            UNIQUE (d),
            CHECK (c > 1),
            CHECK (d > 2));
        CREATE CLUSTERED INDEX IX_umbracoEngageRef_cluster ON umbracoEngageRef (c);
        CREATE UNIQUE NONCLUSTERED INDEX UX_umbracoEngageRef_d ON umbracoEngageRef (d, c DESC) INCLUDE (b, a);
        CREATE TABLE umbracoEngageRefChild (
            id int NOT NULL PRIMARY KEY,
            x int NOT NULL, y int NOT NULL,
            FOREIGN KEY (y, x) REFERENCES umbracoEngageRef (b, a));
        """;

    private async Task<DatabaseSchema> ReadFixture2Async()
    {
        var cs = await sql.CreateDatabaseAsync("reader2_" + Guid.NewGuid().ToString("N"));
        await SqlServerFixture.ExecuteAsync(cs, Fixture2);
        return await SqlServerSchemaReader.ReadAsync(cs);
    }

    [Fact]
    public async Task Composite_primary_key_keeps_key_order()
    {
        var table = (await ReadFixture2Async()).Tables.Single(t => t.Name == "umbracoEngageRef");
        Assert.Equal(["b", "a"], table.PrimaryKey!.Columns);
        Assert.False(table.PrimaryKey.IsClustered);
    }

    [Fact]
    public async Task Composite_foreign_key_keeps_column_order()
    {
        var fk = Assert.Single((await ReadFixture2Async()).Tables.Single(t => t.Name == "umbracoEngageRefChild").ForeignKeys);
        Assert.Equal(["y", "x"], fk.Columns);
        Assert.Equal(["b", "a"], fk.ReferencedColumns);
    }

    [Fact]
    public async Task Reads_multi_column_index_unique_index_and_clustered_non_pk_index()
    {
        var table = (await ReadFixture2Async()).Tables.Single(t => t.Name == "umbracoEngageRef");
        Assert.Equal(["IX_umbracoEngageRef_cluster", "UX_umbracoEngageRef_d"], table.Indexes.Select(i => i.Name));
        var cluster = table.Indexes[0];
        Assert.True(cluster.IsClustered);
        Assert.False(cluster.IsUnique);
        var ux = table.Indexes[1];
        Assert.True(ux.IsUnique);
        Assert.False(ux.IsClustered);
        Assert.Equal(["d", "c"], ux.KeyColumns.Select(k => k.Name));
        Assert.Equal([false, true], ux.KeyColumns.Select(k => k.Descending));
        Assert.Equal(["a", "b"], ux.IncludedColumns);
    }

    [Fact]
    public async Task System_named_constraints_have_null_names()
    {
        var db = await ReadFixture2Async();
        var rf = db.Tables.Single(t => t.Name == "umbracoEngageRef");
        Assert.Null(rf.PrimaryKey!.Name);
        Assert.All(rf.UniqueConstraints, u => Assert.Null(u.Name));
        Assert.All(rf.CheckConstraints, c => Assert.Null(c.Name));
        Assert.Null(Assert.Single(db.Tables.Single(t => t.Name == "umbracoEngageRefChild").ForeignKeys).Name);
    }

    [Fact]
    public async Task Output_order_is_identical_across_databases()
    {
        static string Project(DatabaseSchema db) => string.Join("|", db.Tables.Select(t =>
            t.Name
            + ";uq=" + string.Join("/", t.UniqueConstraints.Select(u => string.Join(",", u.Columns)))
            + ";ck=" + string.Join("/", t.CheckConstraints.Select(c => c.Definition))
            + ";fk=" + string.Join("/", t.ForeignKeys.Select(f => string.Join(",", f.Columns) + ">" + f.ReferencedTable))
            + ";ix=" + string.Join("/", t.Indexes.Select(i => i.Name))));

        var first = Project(await ReadFixture2Async());
        var second = Project(await ReadFixture2Async());
        Assert.Equal(first, second);
        Assert.Contains("uq=c,d/d", first);
        Assert.Contains("ck=([c]>(1))/([d]>(2))", first);
    }

    [Fact]
    public async Task Ignores_engage_tables_outside_dbo()
    {
        var cs = await sql.CreateDatabaseAsync("schema_" + Guid.NewGuid().ToString("N"));
        await SqlServerFixture.ExecuteAsync(cs, Fixture);
        await SqlServerFixture.ExecuteAsync(cs, "CREATE SCHEMA [backup]");
        await SqlServerFixture.ExecuteAsync(cs, """
            CREATE TABLE [backup].umbracoEngageParent (id int NOT NULL CONSTRAINT PK_backup_umbracoEngageParent PRIMARY KEY, extra int NULL);
            CREATE TABLE [backup].umbracoEngageBackupOnly (id int NOT NULL);
            CREATE TABLE umbracoEngageStray (
                id int NOT NULL,
                parentId int NOT NULL CONSTRAINT FK_umbracoEngageStray_backup FOREIGN KEY REFERENCES [backup].umbracoEngageParent(id));
            """);

        var db = await SqlServerSchemaReader.ReadAsync(cs);

        Assert.Equal(["umbracoEngageChild", "umbracoEngageParent", "umbracoEngageStray"], db.Tables.Select(t => t.Name));
        Assert.Equal(["id", "key", "name", "amount"], db.Tables.Single(t => t.Name == "umbracoEngageParent").Columns.Select(c => c.Name));
        Assert.Equal("backup.umbracoEngageParent", Assert.Single(db.Tables.Single(t => t.Name == "umbracoEngageStray").ForeignKeys).ReferencedTable);
        Assert.Empty(Core.Comparison.SchemaComparer.Compare(db, db));
    }

    [Fact]
    public async Task Reads_disabled_index_and_disabled_untrusted_foreign_key()
    {
        var cs = await sql.CreateDatabaseAsync("disabled_" + Guid.NewGuid().ToString("N"));
        await SqlServerFixture.ExecuteAsync(cs, Fixture);
        var before = (await SqlServerSchemaReader.ReadAsync(cs)).Tables.Single(t => t.Name == "umbracoEngageChild");
        Assert.False(before.Indexes.Single().IsDisabled);
        Assert.False(before.ForeignKeys.Single().IsDisabled);
        Assert.False(before.ForeignKeys.Single().IsNotTrusted);

        await SqlServerFixture.ExecuteAsync(cs, """
            ALTER INDEX IX_umbracoEngageChild_parentId ON umbracoEngageChild DISABLE;
            ALTER TABLE umbracoEngageChild NOCHECK CONSTRAINT FK_umbracoEngageChild_umbracoEngageParent;
            """);
        var child = (await SqlServerSchemaReader.ReadAsync(cs)).Tables.Single(t => t.Name == "umbracoEngageChild");

        var ix = Assert.Single(child.Indexes);
        Assert.True(ix.IsDisabled);
        Assert.Equal(["contentTypeId"], ix.IncludedColumns);
        var fk = Assert.Single(child.ForeignKeys);
        Assert.True(fk.IsDisabled);
        Assert.True(fk.IsNotTrusted);
    }

    [Fact]
    public async Task Reads_untrusted_foreign_key_that_is_enabled()
    {
        var cs = await sql.CreateDatabaseAsync("untrusted_" + Guid.NewGuid().ToString("N"));
        await SqlServerFixture.ExecuteAsync(cs, Fixture);
        await SqlServerFixture.ExecuteAsync(cs, """
            ALTER TABLE umbracoEngageChild NOCHECK CONSTRAINT FK_umbracoEngageChild_umbracoEngageParent;
            ALTER TABLE umbracoEngageChild CHECK CONSTRAINT FK_umbracoEngageChild_umbracoEngageParent;
            """);
        var fk = Assert.Single((await SqlServerSchemaReader.ReadAsync(cs)).Tables.Single(t => t.Name == "umbracoEngageChild").ForeignKeys);
        Assert.False(fk.IsDisabled);
        Assert.True(fk.IsNotTrusted);
    }
}
