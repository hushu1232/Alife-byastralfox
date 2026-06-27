using Microsoft.Data.Sqlite;

namespace Alife.Function.DataAgent;

public sealed class DataAgentQueryExecutor(string databasePath)
{
    public DataAgentQueryResult Execute(DataAgentCompiledSql compiledSql)
    {
        ArgumentNullException.ThrowIfNull(compiledSql);

        using SqliteConnection connection = DataAgentSqlite.Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = compiledSql.Sql;
        command.CommandTimeout = 5;

        foreach (DataAgentSqlParameter parameter in compiledSql.Parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);

        using SqliteDataReader reader = command.ExecuteReader();
        List<IReadOnlyDictionary<string, object?>> rows = [];

        while (reader.Read())
        {
            Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                object? value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                row[reader.GetName(i)] = value;
            }

            rows.Add(row);
        }

        return new DataAgentQueryResult(rows);
    }
}

public sealed record DataAgentQueryResult(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);
