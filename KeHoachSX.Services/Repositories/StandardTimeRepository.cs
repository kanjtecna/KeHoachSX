using Dapper;
using KeHoachSX.Infrastructure.Database;
namespace KeHoachSX.Infrastructure.Repositories;
public sealed class StandardTimeRepository : IStandardTimeRepository
{
    private readonly ISqlConnectionFactory _factory;
    public StandardTimeRepository(ISqlConnectionFactory factory) => _factory = factory;
    public async Task<double?> GetFromSqlServerAsync(string productCode, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT TOP (1) TRY_CONVERT(FLOAT, NULLIF(LTRIM(RTRIM(GIOCONG)), ''))
FROM STOCKMANAGEMENT.dbo.SANLUONGCAMLR
WHERE IDPRODUCT = @ProductCode
  AND TRY_CONVERT(FLOAT, NULLIF(LTRIM(RTRIM(GIOCONG)), '')) > 0
ORDER BY [TIME] DESC;";
        using var connection = _factory.CreateConnection();
        return await connection.ExecuteScalarAsync<double?>(
            new CommandDefinition(sql, new { ProductCode = productCode }, cancellationToken: cancellationToken));
    }
}
