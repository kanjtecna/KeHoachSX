using Dapper;
using Interfaces;
using JtecShare.Core.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Services
{
    public class StandardTimeService : IStandardTimeService
    {
        private readonly string _sqlServerConnection;
        private readonly ILayGioCongHeThong? _layGioCongOracle;
        private readonly ConcurrentDictionary<string, double> _cache = new(StringComparer.OrdinalIgnoreCase);

        public StandardTimeService(IConfiguration configuration, IServiceProvider serviceProvider)
        {
            _sqlServerConnection = configuration.GetConnectionString("Server30StockConnection")
                                   ?? configuration.GetConnectionString("DefaultConnection")
                                   ?? "";

            // Thử lấy ILayGioCongHeThong nếu có đăng ký
            try
            {
                _layGioCongOracle = (ILayGioCongHeThong?)serviceProvider.GetService(typeof(ILayGioCongHeThong));
            }
            catch
            {
                _layGioCongOracle = null;
            }
        }

        public async Task<double> GetStandardMinutesAsync(string maSanPham)
        {
            if (string.IsNullOrWhiteSpace(maSanPham)) return 60.0;
            string key = maSanPham.Trim();

            if (_cache.TryGetValue(key, out double cachedTime))
            {
                return cachedTime;
            }

            // 1. Thử lấy từ Oracle BOM
            if (_layGioCongOracle != null)
            {
                try
                {
                    var result = await _layGioCongOracle.LayGioCongHeThongAsync(key, "1523");
                    if (result != null && !string.IsNullOrWhiteSpace(result.GioCongHT) &&
                        double.TryParse(result.GioCongHT.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedGc) &&
                        parsedGc > 0)
                    {
                        _cache[key] = parsedGc;
                        return parsedGc;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[StandardTimeService] Lỗi Oracle BOM: {ex.Message}");
                }
            }

            // 2. Thử lấy từ SQL Server SANLUONGCAMLR
            if (!string.IsNullOrWhiteSpace(_sqlServerConnection))
            {
                try
                {
                    using var conn = new SqlConnection(_sqlServerConnection);
                    const string sql = @"
                        SELECT TOP 1 CAST(ISNULL(NULLIF(GIOCONG, ''), '0') AS FLOAT) 
                        FROM STOCKMANAGEMENT.dbo.SANLUONGCAMLR 
                        WHERE IDPRODUCT = @ProductCode 
                          AND GIOCONG IS NOT NULL 
                          AND GIOCONG <> '' 
                          AND CAST(ISNULL(NULLIF(GIOCONG, ''), '0') AS FLOAT) > 0
                        ORDER BY [TIME] DESC";

                    var gc = await conn.ExecuteScalarAsync<double?>(sql, new { ProductCode = key });
                    if (gc.HasValue && gc.Value > 0)
                    {
                        _cache[key] = gc.Value;
                        return gc.Value;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[StandardTimeService] Lỗi SQL Server: {ex.Message}");
                }
            }

            // 3. Mặc định 60 phút nếu không tìm thấy trong DB
            return 60.0;
        }
    }
}
