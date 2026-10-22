using Umbraco.Engage.HealthCheck.Core.Schema;

namespace Umbraco.Engage.HealthCheck.Tests;

public class ColumnTypeFormatterTests
{
    [Theory]
    [InlineData("int", 4, 10, 0, "int")]
    [InlineData("nvarchar", 510, 0, 0, "nvarchar(255)")]
    [InlineData("nvarchar", -1, 0, 0, "nvarchar(max)")]
    [InlineData("nchar", 20, 0, 0, "nchar(10)")]
    [InlineData("varchar", 50, 0, 0, "varchar(50)")]
    [InlineData("varbinary", -1, 0, 0, "varbinary(max)")]
    [InlineData("decimal", 9, 18, 2, "decimal(18,2)")]
    [InlineData("datetime2", 8, 27, 7, "datetime2(7)")]
    [InlineData("uniqueidentifier", 16, 0, 0, "uniqueidentifier")]
    [InlineData("bit", 1, 1, 0, "bit")]
    public void Formats_sql_server_types(string type, short maxLength, byte precision, byte scale, string expected)
        => Assert.Equal(expected, ColumnTypeFormatter.Format(type, maxLength, precision, scale));
}
