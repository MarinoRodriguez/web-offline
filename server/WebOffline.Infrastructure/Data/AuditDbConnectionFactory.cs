using System.Data;
using Microsoft.Data.Sqlite;

namespace WebOffline.Infrastructure.Data;

public interface IAuditDbConnectionFactory
{
    IDbConnection CreateConnection();
    string ConnectionString { get; }
}

public class AuditDbConnectionFactory : IAuditDbConnectionFactory
{
    private readonly string _connectionString;

    public AuditDbConnectionFactory(string connectionString)
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
