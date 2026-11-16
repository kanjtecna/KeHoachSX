using JtecShare.Core.Interfaces;
using System.Globalization;
using Abstractions;

namespace KeHoachSX.Infrastructure.DataSources;

public sealed class OracleStandardTimeProvider : IOracleStandardTimeProvider
{
    private readonly ILayGioCongHeThong _service;

    public OracleStandardTimeProvider(ILayGioCongHeThong service)
    {
        _service = service;
    }

    public async Task<double?> GetStandardMinutesAsync(
        string productCode,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.LayGioCongHeThongAsync(productCode, "1523");
        if (result is null || string.IsNullOrWhiteSpace(result.GioCongHT))
            return null;

        return double.TryParse(
            result.GioCongHT.Replace(',', '.'),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var value) && value > 0
                ? value
                : null;
    }
}

public sealed class NullOracleStandardTimeProvider : IOracleStandardTimeProvider
{
    public Task<double?> GetStandardMinutesAsync(
        string productCode,
        CancellationToken cancellationToken = default)
        => Task.FromResult<double?>(null);
}
