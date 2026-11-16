using Dapper;
using Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Services
{
    public class AssemblyEmployeeService : IAssemblyEmployeeService
    {
        private readonly string _connectionString;
        private readonly ISkillMatrixService _skillMatrix;

        // Danh sách nhóm lắp ráp được phép phân công
        private static readonly HashSet<string> AllowedGroups = new(StringComparer.OrdinalIgnoreCase)
            { "LR1", "LR2", "LR3", "LR4" };

        // Các cấp bậc bị loại trừ (không phân công) – LevelUser varchar(5)
        private static readonly HashSet<string> ExcludedLevels = new(StringComparer.OrdinalIgnoreCase)
            { "CHIEF", "LEADE", "LEADER" };

        public AssemblyEmployeeService(IConfiguration configuration, ISkillMatrixService skillMatrix)
        {
            _connectionString = configuration.GetConnectionString("Server30StockConnection")
                                ?? configuration.GetConnectionString("DefaultConnection")
                                ?? "";
            _skillMatrix = skillMatrix;
        }

        public async Task<List<AssemblyWorker>> GetActiveAssemblyWorkersAsync()
        {
            await _skillMatrix.EnsureLoadedAsync();
            var workers = new List<AssemblyWorker>();

            // ────────────────────────────────────────────────
            // 1. Lấy danh sách công nhân từ SQL Server
            //    - STATUS = '1'  (đang làm việc)
            //    - GROUPBOPHAN IN (LR1, LR2, LR3, LR4)
            //    - LevelUser NOT IN ('CHIEF','LEADE','LEADER')
            //    - Hiệu suất lấy từ HIEUSUATNHANVIEN, mặc định 20 nếu không có
            // ────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(_connectionString))
            {
                try
                {
                    using var connection = new SqlConnection(_connectionString);
                    await connection.OpenAsync();

                    const string sql = @"
                        SELECT
                            LTRIM(RTRIM(nv.UserName))    AS WorkerId,
                            LTRIM(RTRIM(nv.Fullname))    AS FullName,
                            LTRIM(RTRIM(nv.GROUPBOPHAN)) AS GroupName,
                            CASE
                                WHEN hs.HieuSuat > 0 THEN hs.HieuSuat
                                ELSE 20.0
                            END AS HieuSuat
                        FROM STOCKMANAGEMENT.dbo.NHANVIEN nv
                        OUTER APPLY (
                            SELECT TOP (1)
                                TRY_CONVERT(FLOAT, REPLACE(NULLIF(LTRIM(RTRIM(h.HIEUSUAT)), ''), ',', '.')) AS HieuSuat
                            FROM STOCKMANAGEMENT.dbo.HIEUSUATNHANVIEN h
                            WHERE LTRIM(RTRIM(h.IDNHANVIEN)) = LTRIM(RTRIM(nv.UserName))
                            ORDER BY h.[No] DESC
                        ) hs
                        WHERE nv.STATUS = '1'
                          AND UPPER(LTRIM(RTRIM(nv.GROUPBOPHAN))) IN ('LR1', 'LR2', 'LR3', 'LR4')
                          AND UPPER(LTRIM(RTRIM(ISNULL(nv.LevelUser, '')))) NOT IN ('CHIEF', 'LEADE', 'LEADER')
                        ORDER BY nv.GROUPBOPHAN, nv.UserName ASC";

                    var dbWorkers = (await connection.QueryAsync<dynamic>(sql)).ToList();

                    foreach (var w in dbWorkers)
                    {
                        string wId    = (string)w.WorkerId;
                        string wName  = (string)w.FullName;
                        string group  = (string)w.GroupName;
                        double hs     = w.HieuSuat != null ? (double)w.HieuSuat : 20.0;

                        workers.Add(new AssemblyWorker
                        {
                            WorkerId  = wId,
                            FullName  = wName,
                            GroupName = group,
                            HieuSuat  = hs,
                            SkillMap  = _skillMatrix.GetWorkerSkills(wId)
                        });
                    }

                    System.Diagnostics.Debug.WriteLine(
                        $"[AssemblyEmployeeService] Tải {workers.Count} công nhân từ SQL Server.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AssemblyEmployeeService] Lỗi kết nối SQL Server: {ex.Message}");
                }
            }

            // ────────────────────────────────────────────────
            // 2. Fallback: đọc từ file JSON nếu không có kết nối DB
            //    Tìm file NHANVIEN.json và HIEUSUATNHANVIEN.json
            //    ở thư mục Documents hoặc cùng cấp module
            // ────────────────────────────────────────────────
            if (!workers.Any())
            {
                System.Diagnostics.Debug.WriteLine(
                    "[AssemblyEmployeeService] DB không có dữ liệu, fallback đọc JSON...");

                try
                {
                    // Tìm file JSON ở các vị trí có thể có
                    string? employeeJsonPath    = FindFile("NHANVIEN.json");
                    string? efficiencyJsonPath  = FindFile("HIEUSUATNHANVIEN.json");

                    // Đọc hiệu suất vào dict trước
                    var efficiencyDict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    if (efficiencyJsonPath != null)
                    {
                        var effRaw  = await File.ReadAllTextAsync(efficiencyJsonPath);
                        var effRoot = JsonDocument.Parse(effRaw).RootElement;

                        // Hỗ trợ 2 dạng JSON: Array gốc  hoặc { "HIEUSUATNHANVIEN": [...] }
                        JsonElement effArray = effRoot.ValueKind == JsonValueKind.Array
                            ? effRoot
                            : effRoot.TryGetProperty("HIEUSUATNHANVIEN", out var inner) ? inner : default;

                        if (effArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in effArray.EnumerateArray())
                            {
                                string id  = item.TryGetProperty("IDNHANVIEN", out var idProp) ? idProp.GetString() ?? "" : "";
                                string hsStr = item.TryGetProperty("HIEUSUAT", out var hsProp) ? hsProp.GetString() ?? "" : "";

                                if (!string.IsNullOrWhiteSpace(id) &&
                                    double.TryParse(hsStr.Replace(',', '.'),
                                        System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture, out double hsVal)
                                    && hsVal > 0)
                                {
                                    efficiencyDict[id] = hsVal;
                                }
                            }
                        }
                    }

                    // Đọc danh sách nhân viên
                    if (employeeJsonPath != null)
                    {
                        var empRaw  = await File.ReadAllTextAsync(employeeJsonPath);
                        var empRoot = JsonDocument.Parse(empRaw).RootElement;

                        // Hỗ trợ Array hoặc { "NHANVIEN": [...] }
                        JsonElement empArray = empRoot.ValueKind == JsonValueKind.Array
                            ? empRoot
                            : empRoot.TryGetProperty("NHANVIEN", out var inner) ? inner : default;

                        if (empArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var emp in empArray.EnumerateArray())
                            {
                                string status = emp.TryGetProperty("STATUS",      out var sp) ? sp.GetString() ?? "" : "";
                                string group  = emp.TryGetProperty("GROUPBOPHAN", out var gp) ? gp.GetString()?.ToUpperInvariant() ?? "" : "";
                                string level  = emp.TryGetProperty("LevelUser",   out var lp) ? lp.GetString()?.ToUpperInvariant() ?? "" : "";

                                // Lọc điều kiện
                                if (status != "1") continue;
                                if (!AllowedGroups.Contains(group)) continue;
                                if (ExcludedLevels.Contains(level)) continue;

                                string wId   = emp.TryGetProperty("UserName", out var up) ? up.GetString() ?? "" : "";
                                string wName = emp.TryGetProperty("Fullname",  out var fp) ? fp.GetString() ?? "" : "";
                                if (string.IsNullOrWhiteSpace(wId)) continue;

                                double hs = efficiencyDict.TryGetValue(wId, out double val) && val > 0 ? val : 20.0;

                                workers.Add(new AssemblyWorker
                                {
                                    WorkerId  = wId,
                                    FullName  = wName,
                                    GroupName = group,
                                    HieuSuat  = hs,
                                    SkillMap  = _skillMatrix.GetWorkerSkills(wId)
                                });
                            }
                        }
                    }

                    System.Diagnostics.Debug.WriteLine(
                        $"[AssemblyEmployeeService] Fallback JSON tải {workers.Count} công nhân.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AssemblyEmployeeService] Lỗi đọc fallback JSON: {ex.Message}");
                }
            }

            // Sắp xếp: nhóm → mã nhân viên
            return workers
                .OrderBy(w => w.GroupName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(w => w.WorkerId)
                .ToList();
        }

        /// <summary>
        /// Tìm file JSON ở các thư mục phổ biến trong dự án
        /// </summary>
        private static string? FindFile(string fileName)
        {
            var candidates = new[]
            {
                Path.Combine("D:/JTEC-WEB/Documents", fileName),
                Path.Combine(AppContext.BaseDirectory, fileName),
                Path.Combine(AppContext.BaseDirectory, "data", fileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName),
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path)) return path;
            }
            return null;
        }
    }
}
