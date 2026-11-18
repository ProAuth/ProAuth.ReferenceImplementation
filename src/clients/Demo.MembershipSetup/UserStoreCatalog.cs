using System.Data.Common;
using Microsoft.Data.SqlClient;

internal static class UserStoreCatalog
{
    public static bool Matches(string? connection, string expected)
    {
        if (string.IsNullOrWhiteSpace(connection))
            return false;
        // ProAuth resolves an installation-owned alias and applies its literal Database override.
        // Require the explicit two-field form so the alias can never select its default catalog.
        const string prefix = "ConnectionStringAlias=";
        if (connection.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var parts = connection[prefix.Length..].TrimEnd(';').Split(';', StringSplitOptions.TrimEntries);
            if (parts is not [var alias, var field] || string.IsNullOrWhiteSpace(alias))
                return false;
            var pair = field.Split('=', 2, StringSplitOptions.TrimEntries);
            return pair is [var key, var database] && key.Equals("Database", StringComparison.OrdinalIgnoreCase) && database == expected;
        }
        try
        {
            var sql = new SqlConnectionStringBuilder(connection);
            var fields = new CatalogCounter { ConnectionString = connection };
            return fields.CountCatalogs == 1 && sql.InitialCatalog == expected && sql.AttachDBFilename.Length == 0;
        }
        catch (ArgumentException)
        {
            return false; // Provider exceptions can contain configuration values.
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // DbConnectionStringBuilder applies each parsed pair through the indexer, preserving
    // duplicate occurrences and handling quotes and embedded semicolons with ADO.NET syntax.
    private sealed class CatalogCounter : DbConnectionStringBuilder
    {
        public int CountCatalogs
        {
            get; private set;
        }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override object this[string keyword]
        {
            get => base[keyword];
            set
            {
                if (keyword.Equals("Database", StringComparison.OrdinalIgnoreCase) ||
                    keyword.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase))
                    CountCatalogs++;
                base[keyword] = value;
            }
        }
    }
}
