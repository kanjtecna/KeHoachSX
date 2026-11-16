using KeHoachSX.Application.Contracts;
using Models;

namespace Abstractions
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

    /// <summary>
    /// Provides the identity of the operator for the current application session.
    /// </summary>
    public interface ICurrentOperatorProvider
    {
        Task<OperatorContext> GetCurrentAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Retrieves persisted operator details when a database record is available.
    /// </summary>
    public interface IOperatorRepository
    {
        Task<OperatorContext?> GetAsync(
            string employeeCode,
            string machineName,
            CancellationToken cancellationToken = default);
    }
}
