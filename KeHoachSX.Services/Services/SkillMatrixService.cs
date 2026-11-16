using Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Services
{
    public class SkillMatrixService : ISkillMatrixService
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> _workerSkills = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, List<(string WorkerId, string FullName, int Count)>> _productWorkers = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _workerNames = new(StringComparer.OrdinalIgnoreCase);
        private bool _isLoaded = false;
        private readonly object _lock = new();

        public async Task EnsureLoadedAsync()
        {
            if (_isLoaded) return;

            await Task.Run(() =>
            {
                lock (_lock)
                {
                    if (_isLoaded) return;
                    LoadCsvFiles();
                    _isLoaded = true;
                }
            });
        }

        private void LoadCsvFiles()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string solutionDir = FindSolutionDirectory(baseDir);

            // 1. Tìm đường dẫn file CSV kinh nghiệm nhân viên
            string[] possiblePathsEmployeeCsv = new[]
            {
                Path.Combine(baseDir, "_SELECT_TempTable_IDNHANVIEN_TempTable_Fullname_STRING_AGG_TempT.csv"),
                Path.Combine(baseDir, "Plugins", "_SELECT_TempTable_IDNHANVIEN_TempTable_Fullname_STRING_AGG_TempT.csv"),
                Path.Combine(solutionDir, "Jtec.Modules", "LAPRAP", "KeHoachSX", "_SELECT_TempTable_IDNHANVIEN_TempTable_Fullname_STRING_AGG_TempT.csv")
            };

            string? empCsvPath = possiblePathsEmployeeCsv.FirstOrDefault(File.Exists);

            if (!string.IsNullOrEmpty(empCsvPath))
            {
                ParseEmployeeSkillsCsv(empCsvPath);
            }

            // 2. Tìm đường dẫn file CSV danh sách nhân viên theo sản phẩm
            string[] possiblePathsProductCsv = new[]
            {
                Path.Combine(baseDir, "_SELECT_TempTable_IDPRODUCT_STRING_AGG_TempTable_ThongTinNhanVie_nhungnhanvientunglammahang.csv"),
                Path.Combine(baseDir, "Plugins", "_SELECT_TempTable_IDPRODUCT_STRING_AGG_TempTable_ThongTinNhanVie_nhungnhanvientunglammahang.csv"),
                Path.Combine(solutionDir, "Jtec.Modules", "LAPRAP", "KeHoachSX", "_SELECT_TempTable_IDPRODUCT_STRING_AGG_TempTable_ThongTinNhanVie_nhungnhanvientunglammahang.csv")
            };

            string? prodCsvPath = possiblePathsProductCsv.FirstOrDefault(File.Exists);

            if (!string.IsNullOrEmpty(prodCsvPath))
            {
                ParseProductWorkersCsv(prodCsvPath);
            }
        }

        private void ParseEmployeeSkillsCsv(string filePath)
        {
            try
            {
                // Thử đọc với UTF8 (fallback sang UTF8 không BOM)
                var lines = File.ReadAllLines(filePath, Encoding.UTF8);
                if (lines.Length <= 1) return;

                // Bỏ qua dòng tiêu đề
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = SplitCsvLine(line);
                    if (parts.Count < 2) continue;

                    string workerId = parts[0].Trim().Trim('"').Trim();
                    string fullName = parts[1].Trim().Trim('"').Trim();

                    if (!string.IsNullOrEmpty(workerId))
                    {
                        _workerNames[workerId] = fullName;
                        var workerMap = _workerSkills.GetOrAdd(workerId, _ => new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase));

                        if (parts.Count >= 3)
                        {
                            string skillRaw = parts[2].Trim().Trim('"');
                            // Tách từng mã: "14Y0641811-02 (32), 14Y0645321-01 (22)"
                            var items = skillRaw.Split(',', StringSplitOptions.RemoveEmptyEntries);
                            foreach (var rawItem in items)
                            {
                                string item = rawItem.Trim();
                                int openIdx = item.LastIndexOf('(');
                                int closeIdx = item.LastIndexOf(')');

                                if (openIdx > 0 && closeIdx > openIdx)
                                {
                                    string productCode = item.Substring(0, openIdx).Trim();
                                    string countStr = item.Substring(openIdx + 1, closeIdx - openIdx - 1).Trim();

                                    if (int.TryParse(countStr, out int count))
                                    {
                                        workerMap[productCode] = count;

                                        // Cập nhật bản đồ ngược
                                        var prodList = _productWorkers.GetOrAdd(productCode, _ => new List<(string, string, int)>());
                                        lock (prodList)
                                        {
                                            if (!prodList.Any(p => p.WorkerId.Equals(workerId, StringComparison.OrdinalIgnoreCase)))
                                            {
                                                prodList.Add((workerId, fullName, count));
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Sắp xếp lại danh sách thợ theo số lần kinh nghiệm giảm dần
                foreach (var kvp in _productWorkers)
                {
                    lock (kvp.Value)
                    {
                        kvp.Value.Sort((a, b) => b.Count.CompareTo(a.Count));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SkillMatrixService] Lỗi đọc Employee CSV: {ex.Message}");
            }
        }

        private void ParseProductWorkersCsv(string filePath)
        {
            try
            {
                var lines = File.ReadAllLines(filePath, Encoding.UTF8);
                if (lines.Length <= 1) return;

                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = SplitCsvLine(line);
                    if (parts.Count < 2) continue;

                    string productCode = parts[0].Trim().Trim('"').Trim();
                    string workersRaw = parts[1].Trim().Trim('"').Trim();

                    if (!string.IsNullOrEmpty(productCode) && !string.IsNullOrEmpty(workersRaw))
                    {
                        var workerItems = workersRaw.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        var prodList = _productWorkers.GetOrAdd(productCode, _ => new List<(string, string, int)>());

                        foreach (var rawItem in workerItems)
                        {
                            // Định dạng: "248004 - LÊ THỊ DUYÊN"
                            var item = rawItem.Trim();
                            var dashIdx = item.IndexOf('-');
                            if (dashIdx > 0)
                            {
                                string wId = item.Substring(0, dashIdx).Trim();
                                string wName = item.Substring(dashIdx + 1).Trim();

                                if (!string.IsNullOrEmpty(wId))
                                {
                                    _workerNames.TryAdd(wId, wName);
                                    lock (prodList)
                                    {
                                        if (!prodList.Any(p => p.WorkerId.Equals(wId, StringComparison.OrdinalIgnoreCase)))
                                        {
                                            prodList.Add((wId, wName, 1)); // Mặc định count = 1 nếu từ file 2
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SkillMatrixService] Lỗi đọc Product CSV: {ex.Message}");
            }
        }

        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var sb = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            result.Add(sb.ToString());
            return result;
        }

        public int GetSkillCount(string workerId, string productCode)
        {
            if (string.IsNullOrWhiteSpace(workerId) || string.IsNullOrWhiteSpace(productCode)) return 0;

            if (_workerSkills.TryGetValue(workerId.Trim(), out var map))
            {
                if (map.TryGetValue(productCode.Trim(), out int count))
                {
                    return count;
                }
            }
            return 0;
        }

        public List<(string WorkerId, string FullName, int Count)> GetTopExperiencedWorkers(string productCode)
        {
            if (string.IsNullOrWhiteSpace(productCode)) return new();

            if (_productWorkers.TryGetValue(productCode.Trim(), out var list))
            {
                lock (list)
                {
                    return list.ToList();
                }
            }
            return new();
        }

        public Dictionary<string, int> GetWorkerSkills(string workerId)
        {
            if (string.IsNullOrWhiteSpace(workerId)) return new();

            if (_workerSkills.TryGetValue(workerId.Trim(), out var map))
            {
                return map.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
            }
            return new();
        }

        public IReadOnlyDictionary<string, string> GetAllWorkersFromMatrix()
        {
            return _workerNames;
        }

        private static string FindSolutionDirectory(string currentPath)
        {
            var dir = new DirectoryInfo(currentPath);
            while (dir != null && !dir.GetFiles("*.sln").Any())
            {
                dir = dir.Parent;
            }
            return dir?.FullName ?? currentPath;
        }
    }
}
