using Abstractions;
using Interfaces;
using Models;
using KeHoachSX.Application.Contracts;

namespace Services;

public sealed class AssemblyEmployeeService : IAssemblyEmployeeService
{
    private static readonly HashSet<string> AllowedGroups = new(StringComparer.OrdinalIgnoreCase)
        { "LR1", "LR2", "LR3", "LR4" };
    private static readonly HashSet<string> ExcludedLevels = new(StringComparer.OrdinalIgnoreCase)
        { "CHIEF", "LEADE", "LEADER" };

    private readonly IAssemblyEmployeeRepository _repository;
    private readonly IAssemblyEmployeeJsonSource _jsonSource;
    private readonly ISkillMatrixService _skillMatrix;

    public AssemblyEmployeeService(
        IAssemblyEmployeeRepository repository,
        IAssemblyEmployeeJsonSource jsonSource,
        ISkillMatrixService skillMatrix)
    {
        _repository = repository;
        _jsonSource = jsonSource;
        _skillMatrix = skillMatrix;
    }

    public async Task<List<AssemblyWorker>> GetActiveAssemblyWorkersAsync()
    {
        await _skillMatrix.EnsureLoadedAsync();

        List<AssemblyWorker> workers;
        try
        {
            workers = (await _repository.GetActiveAssemblyWorkersAsync()).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[AssemblyEmployeeService] SQL Server unavailable; trying JSON source. {ex.Message}");
            workers = new List<AssemblyWorker>();
        }

        // Preserve the previous fallback: use JSON when DB is unavailable or returns no rows.
        if (workers.Count == 0)
        {
            try
            {
                var records = await _jsonSource.LoadAsync();
                workers = records
                    .Where(IsEligible)
                    .Select(record => new AssemblyWorker
                    {
                        WorkerId = record.WorkerId.Trim(),
                        FullName = record.FullName,
                        GroupName = record.GroupName.Trim().ToUpperInvariant(),
                        HieuSuat = record.Efficiency > 0 ? record.Efficiency : 20.0
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AssemblyEmployeeService] JSON fallback failed: {ex.Message}");
                workers = new List<AssemblyWorker>();
            }
        }

        foreach (var worker in workers)
            worker.SkillMap = _skillMatrix.GetWorkerSkills(worker.WorkerId);

        return workers
            .Where(w => AllowedGroups.Contains(w.GroupName.Trim()))
            .OrderBy(w => w.GroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(w => w.WorkerId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsEligible(AssemblyEmployeeRecord record)
    {
        return record.Status == "1"
            && AllowedGroups.Contains(record.GroupName.Trim())
            && !ExcludedLevels.Contains(record.LevelUser.Trim())
            && !string.IsNullOrWhiteSpace(record.WorkerId);
    }
}
