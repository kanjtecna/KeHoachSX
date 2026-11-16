using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;

namespace KeHoachSX.Infrastructure.Database;

public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("Server30StockConnection")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? string.Empty;
    }

    public IDbConnection CreateConnection()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException(
                "Không tìm thấy connection string Server30StockConnection hoặc DefaultConnection.");

        return new SqlConnection(_connectionString);
    }
}
