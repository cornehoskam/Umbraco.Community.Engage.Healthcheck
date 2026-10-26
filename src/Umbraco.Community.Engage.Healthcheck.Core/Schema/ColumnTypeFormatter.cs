namespace Umbraco.Community.Engage.Healthcheck.Core.Schema;

public static class ColumnTypeFormatter
{
    public static string Format(string typeName, short maxLength, byte precision, byte scale)
    {
        var type = typeName.ToLowerInvariant();
        return type switch
        {
            "nvarchar" or "nchar" => $"{type}({(maxLength == -1 ? "max" : (maxLength / 2).ToString())})",
            "varchar" or "char" or "varbinary" or "binary" => $"{type}({(maxLength == -1 ? "max" : maxLength.ToString())})",
            "decimal" or "numeric" => $"{type}({precision},{scale})",
            "datetime2" or "datetimeoffset" or "time" => $"{type}({scale})",
            _ => type,
        };
    }
}
