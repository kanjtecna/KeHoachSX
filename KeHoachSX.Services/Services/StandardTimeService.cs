using Interfaces;
using JtecShare.Core.Interfaces;
using KeHoachSX.Infrastructure.Repositories;
using System.Collections.Concurrent;
using System.Globalization;

namespace Services;

public sealed class StandardTimeService : IStandardTimeService
{
    private readonly IStandardTimeRepository _repository;
    private readonly ILayGioCongHeThong? _oracle;
    private readonly ConcurrentDictionary<string, double> _cache = new(StringComparer.OrdinalIgnoreCase);

    public StandardTimeService(IStandardTimeRepository repository, IServiceProvider services)
    {
        _repository = repository;
        _oracle = services.GetService(typeof(ILayGioCongHeThong)) as ILayGioCongHeThong;
    }

    public async Task<double> GetStandardMinutesAsync(string maSanPham)
    {
        if (string.IsNullOrWhiteSpace(maSanPham)) return 60.0;
        var key = maSanPham.Trim();
        if (_cache.TryGetValue(key, out var cached)) return cached;

        if (_oracle is not null)
        {
            try
            {
                var row = await _oracle.LayGioCongHeThongAsync(key, "1523");
                if (row is not null &&
                    double.TryParse(row.GioCongHT?.Replace(',', '.'),
                        NumberStyles.Any, CultureInfo.InvariantCulture, out var oracleValue) &&
                    oracleValue > 0)
                    return _cache[key] = oracleValue;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StandardTimeService] Oracle lookup failed: {ex.Message}");
            }
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

        return 60.0;
    }
}
