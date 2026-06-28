using Microsoft.Data.Sqlite;

namespace Alife.Function.DataAgent;

public sealed class DataAgentSchemaIntrospector(DataAgentCatalog catalog, string databasePath)
{
    public DataAgentSchemaSnapshot Inspect()
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        List<DataAgentDatasetSchema> datasets = [];

        foreach (DataAgentDataset dataset in catalog.Datasets)
        {
            IReadOnlyList<string> databaseFields = ReadDatabaseFields(connection, dataset.Name);
            bool exists = databaseFields.Count > 0;
            bool fieldsMatch = exists && dataset.Fields.SetEquals(databaseFields);
            datasets.Add(new DataAgentDatasetSchema(
                dataset.Name,
                dataset.Fields.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                databaseFields,
                exists,
                fieldsMatch));
        }

        return new DataAgentSchemaSnapshot(
            datasets,
            datasets.All(dataset => dataset.ExistsInDatabase && dataset.FieldsMatch));
    }

    static IReadOnlyList<string> ReadDatabaseFields(SqliteConnection connection, string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)});";

        using SqliteDataReader reader = command.ExecuteReader();
        List<string> fields = [];
        while (reader.Read())
            fields.Add(reader.GetString(reader.GetOrdinal("name")));

        return fields.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    static string QuoteIdentifier(string value)
    {
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

public sealed record DataAgentSchemaSnapshot(
    IReadOnlyList<DataAgentDatasetSchema> Datasets,
    bool CatalogMatchesDatabase);

public sealed record DataAgentDatasetSchema(
    string Name,
    IReadOnlyList<string> CatalogFields,
    IReadOnlyList<string> DatabaseFields,
    bool ExistsInDatabase,
    bool FieldsMatch);
