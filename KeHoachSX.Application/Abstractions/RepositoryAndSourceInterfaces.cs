using KeHoachSX.Application.Contracts;
using Models;

namespace Interfaces
{
    public interface IAssemblyEmployeeRepository
    {
        Task<IReadOnlyList<AssemblyWorker>> GetActiveAssemblyWorkersAsync(CancellationToken cancellationToken = default);
    }

    public interface IStandardTimeRepository
    {
        Task<double?> GetFromSqlServerAsync(string productCode, CancellationToken cancellationToken = default);
    }

    public interface IAssemblyEmployeeJsonSource
    {
        Task<IReadOnlyList<AssemblyEmployeeRecord>> LoadAsync(CancellationToken cancellationToken = default);
    }

    public interface ISkillMatrixDataSource
    {
        Task<IReadOnlyList<SkillMatrixRecord>> LoadAsync(CancellationToken cancellationToken = default);
    }

    public interface IWeeklyPlanFileReader
    {
        Task<List<WeeklyPlanItem>> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default);
    }

    public interface IOracleStandardTimeProvider
    {
        Task<double?> GetStandardMinutesAsync(string productCode, CancellationToken cancellationToken = default);
    }
}
