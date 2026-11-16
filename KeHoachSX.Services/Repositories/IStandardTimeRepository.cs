namespace KeHoachSX.Infrastructure.Repositories;
public interface IStandardTimeRepository
{
    Task<double?> GetFromSqlServerAsync(string productCode, CancellationToken cancellationToken = default);
}
