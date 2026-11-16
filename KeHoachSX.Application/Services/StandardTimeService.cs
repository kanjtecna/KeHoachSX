using Interfaces;
using System.Collections.Concurrent;

namespace Services;

public sealed class StandardTimeService : IStandardTimeService
{
    private const double DefaultMinutes = 60.0;
    private readonly IOracleStandardTimeProvider _oracleProvider;
    private readonly IStandardTimeRepository _repository;
    private readonly ConcurrentDictionary<string, double> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public StandardTimeService(
        IOracleStandardTimeProvider oracleProvider,
        IStandardTimeRepository repository)
    {
        _oracleProvider = oracleProvider;
        _repository = repository;
    }

    public async Task<double> GetStandardMinutesAsync(string maSanPham)
    {
        if (string.IsNullOrWhiteSpace(maSanPham)) return DefaultMinutes;

        var key = maSanPham.Trim();
        if (_cache.TryGetValue(key, out var cached)) return cached;

        // Preserve the original source priority: Oracle -> SQL Server -> default.
        try
        {
            var oracleValue = await _oracleProvider.GetStandardMinutesAsync(key);
            if (oracleValue is > 0) return _cache[key] = oracleValue.Value;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StandardTimeService] Oracle lookup failed: {ex.Message}");
        }

        try
        {
            var sqlValue = await _repository.GetFromSqlServerAsync(key);
            if (sqlValue is > 0) return _cache[key] = sqlValue.Value;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StandardTimeService] SQL lookup failed: {ex.Message}");
        }

        return DefaultMinutes;
    }
}
