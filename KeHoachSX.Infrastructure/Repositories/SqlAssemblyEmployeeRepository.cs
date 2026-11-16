using Dapper;
using KeHoachSX.Infrastructure.Database;
using Models;
using Abstractions;

namespace KeHoachSX.Infrastructure.Repositories;

public sealed class SqlAssemblyEmployeeRepository : IAssemblyEmployeeRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public SqlAssemblyEmployeeRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AssemblyWorker>> GetActiveAssemblyWorkersAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT
    LTRIM(RTRIM(nv.UserName)) AS WorkerId,
    LTRIM(RTRIM(nv.Fullname)) AS FullName,
    LTRIM(RTRIM(nv.GROUPBOPHAN)) AS GroupName,
    CASE WHEN hs.HieuSuat > 0 THEN hs.HieuSuat ELSE 20.0 END AS HieuSuat
FROM STOCKMANAGEMENT.dbo.NHANVIEN nv
OUTER APPLY (
    SELECT TOP (1)
        TRY_CONVERT(FLOAT, REPLACE(NULLIF(LTRIM(RTRIM(h.HIEUSUAT)), ''), ',', '.')) AS HieuSuat
    FROM STOCKMANAGEMENT.dbo.HIEUSUATNHANVIEN h
    WHERE LTRIM(RTRIM(h.IDNHANVIEN)) = LTRIM(RTRIM(nv.UserName))
    ORDER BY h.[No] DESC
) hs
WHERE nv.STATUS = '1'
  AND UPPER(LTRIM(RTRIM(nv.GROUPBOPHAN))) IN ('LR1', 'LR2', 'LR3', 'LR4')
  AND UPPER(LTRIM(RTRIM(ISNULL(nv.LevelUser, '')))) NOT IN ('CHIEF', 'LEADE', 'LEADER')
ORDER BY nv.GROUPBOPHAN, nv.UserName ASC;";

        using var connection = _connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<AssemblyWorker>(command);
        return rows.AsList();
    }
}
