using System;
using System.Data;
using System.IO;
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
        EnsureDirectoryExists(connectionString);
    }

    public string ConnectionString => _connectionString;

    public IDbConnection CreateConnection()
    {
        EnsureDirectoryExists(_connectionString);
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void EnsureDirectoryExists(string connectionString)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            var dataSource = builder.DataSource;
            if (!string.IsNullOrWhiteSpace(dataSource) && !dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
            {
                var fullPath = Path.GetFullPath(dataSource);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
        }
        catch
        {
            // Ignore if unable to parse
        }
    }
}

