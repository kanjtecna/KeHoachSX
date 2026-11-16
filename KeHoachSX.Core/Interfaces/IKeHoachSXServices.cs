using Models;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Interfaces
{
    public interface ISkillMatrixService
    {
        Task EnsureLoadedAsync();
        int GetSkillCount(string workerId, string productCode);
        List<(string WorkerId, string FullName, int Count)> GetTopExperiencedWorkers(string productCode);
        Dictionary<string, int> GetWorkerSkills(string workerId);
        IReadOnlyDictionary<string, string> GetAllWorkersFromMatrix();
    }

    public interface IAssemblyEmployeeService
    {
        Task<List<AssemblyWorker>> GetActiveAssemblyWorkersAsync();
    }

    public interface IStandardTimeService
    {
        Task<double> GetStandardMinutesAsync(string maSanPham);
    }

    public interface IWeeklyPlanParserService
    {
        Task<List<LotTask>> ParseWeeklyPlanAsync(Stream fileStream, string fileName, double defaultMinutes = 60.0);
    }

    public interface IAutoDispatchService
    {
        DispatchResult AutoDispatch(List<LotTask> tasks, List<AssemblyWorker> workers, ShiftConfig shift, DispatchOptions? options = null);
    }
}
