using Microsoft.Data.Sqlite;

namespace Alife.Function.DataAgent;

static class DataAgentSqlite
{
    public static SqliteConnection Open(string databasePath)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databasePath
        };

        SqliteConnection connection = new(builder.ToString());
        connection.Open();
        return connection;
    }
}
