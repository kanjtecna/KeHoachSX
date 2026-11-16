using Interfaces;
using KeHoachSX.Infrastructure.Repositories;
using Models;
using System.Text.Json;

namespace Services;

public sealed class AssemblyEmployeeService : IAssemblyEmployeeService
{
    private static readonly HashSet<string> AllowedGroups = new(StringComparer.OrdinalIgnoreCase)
        { "LR1", "LR2", "LR3", "LR4" };
    private static readonly HashSet<string> ExcludedLevels = new(StringComparer.OrdinalIgnoreCase)
        { "CHIEF", "LEADE", "LEADER" };

    private readonly IAssemblyEmployeeRepository _repository;
    private readonly ISkillMatrixService _skillMatrix;

    public AssemblyEmployeeService(IAssemblyEmployeeRepository repository, ISkillMatrixService skillMatrix)
    {
        _repository = repository;
        _skillMatrix = skillMatrix;
    }

    public async Task<List<AssemblyWorker>> GetActiveAssemblyWorkersAsync()
    {
        await _skillMatrix.EnsureLoadedAsync();
        try
        {
            var workers = await _repository.GetActiveAssemblyWorkersAsync();
            if (workers.Count > 0) return AttachSkills(workers);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AssemblyEmployeeService] DB unavailable; JSON fallback: {ex.Message}");
        }
        return await LoadJsonFallbackAsync();
    }

    private List<AssemblyWorker> AttachSkills(IEnumerable<AssemblyWorker> workers)
    {
        return workers.Select(worker =>
        {
            worker.SkillMap = _skillMatrix.GetWorkerSkills(worker.WorkerId);
            return worker;
        }).OrderBy(w => w.GroupName, StringComparer.OrdinalIgnoreCase)
          .ThenBy(w => w.WorkerId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<List<AssemblyWorker>> LoadJsonFallbackAsync()
    {
        try
        {
            var employeePath = FindFile("NHANVIEN.json");
            var efficiencyPath = FindFile("HIEUSUATNHANVIEN.json");
            var efficiency = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            if (efficiencyPath is not null)
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(efficiencyPath));
                foreach (var row in GetArray(doc.RootElement, "HIEUSUATNHANVIEN"))
                {
                    var id = GetString(row, "IDNHANVIEN");
                    var raw = GetString(row, "HIEUSUAT");
                    if (double.TryParse(raw.Replace(',', '.'),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0)
                        efficiency[id] = value;
                }
            }

            if (employeePath is null) return new();
            using var employeeDoc = JsonDocument.Parse(await File.ReadAllTextAsync(employeePath));
            var result = new List<AssemblyWorker>();

            foreach (var row in GetArray(employeeDoc.RootElement, "NHANVIEN"))
            {
                var status = GetString(row, "STATUS");
                var group = GetString(row, "GROUPBOPHAN").Trim().ToUpperInvariant();
                var level = GetString(row, "LevelUser").Trim().ToUpperInvariant();
                var id = GetString(row, "UserName").Trim();

                if (status != "1" || !AllowedGroups.Contains(group) ||
                    ExcludedLevels.Contains(level) || string.IsNullOrWhiteSpace(id)) continue;

                result.Add(new AssemblyWorker
                {
                    WorkerId = id,
                    FullName = GetString(row, "Fullname"),
                    GroupName = group,
                    HieuSuat = efficiency.TryGetValue(id, out var hs) && hs > 0 ? hs : 20.0,
                    SkillMap = _skillMatrix.GetWorkerSkills(id)
                });
            }

            return result.OrderBy(w => w.GroupName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(w => w.WorkerId, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AssemblyEmployeeService] JSON fallback failed: {ex.Message}");
            return new();
        }
    }

    private static string? FindFile(string name)
    {
        var candidates = new[]
        {
            Path.Combine("D:/JTEC-WEB/Documents", name),
            Path.Combine(AppContext.BaseDirectory, name),
            Path.Combine(AppContext.BaseDirectory, "data", name)
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static IEnumerable<JsonElement> GetArray(JsonElement root, string propertyName)
    {
        if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray().ToArray();
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : Array.Empty<JsonElement>();
    }

    private static string GetString(JsonElement row, string property)
    {
        if (!row.TryGetProperty(property, out var value)) return string.Empty;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }
}
