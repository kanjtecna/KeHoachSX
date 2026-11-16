using Dapper;
using Interfaces;
using KeHoachSX.Infrastructure.Database;

namespace KeHoachSX.Infrastructure.Repositories;

public sealed class SqlStandardTimeRepository : IStandardTimeRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public SqlStandardTimeRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<double?> GetFromSqlServerAsync(
        string productCode,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT TOP (1)
    TRY_CONVERT(FLOAT, NULLIF(LTRIM(RTRIM(GIOCONG)), ''))
FROM STOCKMANAGEMENT.dbo.SANLUONGCAMLR
WHERE IDPRODUCT = @ProductCode
  AND TRY_CONVERT(FLOAT, NULLIF(LTRIM(RTRIM(GIOCONG)), '')) > 0
ORDER BY [TIME] DESC;";

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<double?>(
            new CommandDefinition(
                sql,
                new { ProductCode = productCode },
                cancellationToken: cancellationToken));
    }
}
