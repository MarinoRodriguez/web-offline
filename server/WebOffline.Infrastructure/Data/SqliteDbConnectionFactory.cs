using System.Data;
using Microsoft.Data.Sqlite;

namespace WebOffline.Infrastructure.Data;

public interface ISqliteDbConnectionFactory
{
    IDbConnection CreateConnection();
    string ConnectionString { get; }
}

public class SqliteDbConnectionFactory : ISqliteDbConnectionFactory
{
    private readonly string _connectionString;

    public SqliteDbConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public string ConnectionString => _connectionString;

    public IDbConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
