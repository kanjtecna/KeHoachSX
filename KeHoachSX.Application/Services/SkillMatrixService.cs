using Interfaces;
using System.Collections.Concurrent;
using KeHoachSX.Application.Contracts;

namespace Services;

public sealed class SkillMatrixService : ISkillMatrixService
{
    private readonly ISkillMatrixDataSource _dataSource;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> _workerSkills =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<(string WorkerId, string FullName, int Count, bool FromDetailedSkillCsv)>> _productWorkers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _workerNames =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private volatile bool _isLoaded;

    public SkillMatrixService(ISkillMatrixDataSource dataSource) => _dataSource = dataSource;

    public async Task EnsureLoadedAsync()
    {
        if (_isLoaded) return;

        await _loadLock.WaitAsync();
        try
        {
            if (_isLoaded) return;

            IReadOnlyList<SkillMatrixRecord> records;
            try
            {
                records = await _dataSource.LoadAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SkillMatrixService] Load failed: {ex.Message}");
                records = Array.Empty<SkillMatrixRecord>();
            }

            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.WorkerId)) continue;
                var workerId = record.WorkerId.Trim();
                var fullName = record.FullName?.Trim() ?? string.Empty;
                _workerNames[workerId] = fullName;
                if (string.IsNullOrWhiteSpace(record.ProductCode)) continue;

                var productCode = record.ProductCode.Trim();
                if (record.IncludeInSkillMap)
                {
                    var workerMap = _workerSkills.GetOrAdd(
                        workerId,
                        _ => new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase));
                    workerMap[productCode] = record.Count;
                }

                var productList = _productWorkers.GetOrAdd(
                    productCode,
                    _ => new List<(string WorkerId, string FullName, int Count, bool FromDetailedSkillCsv)>());
                lock (productList)
                {
                    var existing = productList.FindIndex(
                        item => item.WorkerId.Equals(workerId, StringComparison.OrdinalIgnoreCase));
                    var entry = (workerId, fullName, record.Count, record.IncludeInSkillMap);
                    if (existing >= 0) productList[existing] = entry;
                    else productList.Add(entry);
                }
            }

            foreach (var productList in _productWorkers.Values)
            {
                lock (productList)
                {
                    // The old loader sorted detailed skill counts first, then appended
                    // workers supplied only by the product-to-worker CSV in source order.
                    var detailed = productList
                        .Where(item => item.FromDetailedSkillCsv)
                        .OrderByDescending(item => item.Count)
                        .ToList();
                    var supplemental = productList
                        .Where(item => !item.FromDetailedSkillCsv)
                        .ToList();
                    productList.Clear();
                    productList.AddRange(detailed);
                    productList.AddRange(supplemental);
                }
            }

            _isLoaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public int GetSkillCount(string workerId, string productCode)
    {
        if (string.IsNullOrWhiteSpace(workerId) || string.IsNullOrWhiteSpace(productCode)) return 0;
        return _workerSkills.TryGetValue(workerId.Trim(), out var map)
            && map.TryGetValue(productCode.Trim(), out var count) ? count : 0;
    }

    public List<(string WorkerId, string FullName, int Count)> GetTopExperiencedWorkers(string productCode)
    {
        if (string.IsNullOrWhiteSpace(productCode)
            || !_productWorkers.TryGetValue(productCode.Trim(), out var list)) return new();
        lock (list)
            return list.Select(item => (item.WorkerId, item.FullName, item.Count)).ToList();
    }

    public Dictionary<string, int> GetWorkerSkills(string workerId)
    {
        if (string.IsNullOrWhiteSpace(workerId)
            || !_workerSkills.TryGetValue(workerId.Trim(), out var map)) return new();
        return map.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string> GetAllWorkersFromMatrix()
        => new Dictionary<string, string>(_workerNames, StringComparer.OrdinalIgnoreCase);
}
