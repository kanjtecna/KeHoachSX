using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
namespace KeHoachSX.Infrastructure.Database;
public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;
    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Server30StockConnection")
            ?? configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
    }
    public IDbConnection CreateConnection()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("Missing SQL Server connection string.");
        return new SqlConnection(_connectionString);
    }
}
