using Models;
namespace KeHoachSX.Infrastructure.Repositories;
public interface IAssemblyEmployeeRepository
{
    Task<IReadOnlyList<AssemblyWorker>> GetActiveAssemblyWorkersAsync(CancellationToken cancellationToken = default);
}
