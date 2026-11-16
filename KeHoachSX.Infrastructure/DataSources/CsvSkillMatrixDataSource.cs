using KeHoachSX.Application.Contracts;
using System.Text;
using Abstractions;

namespace KeHoachSX.Infrastructure.DataSources;

public sealed class CsvSkillMatrixDataSource : ISkillMatrixDataSource
{
    private const string EmployeeFile =
        "_SELECT_TempTable_IDNHANVIEN_TempTable_Fullname_STRING_AGG_TempT.csv";
    private const string ProductFile =
        "_SELECT_TempTable_IDPRODUCT_STRING_AGG_TempTable_ThongTinNhanVie_nhungnhanvientunglammahang.csv";

    public async Task<IReadOnlyList<SkillMatrixRecord>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var records = new Dictionary<(string WorkerId, string ProductCode), SkillMatrixRecord>(
            new WorkerProductKeyComparer());

        var employeePath = FindFile(EmployeeFile);
        if (employeePath is not null)
        {
            var lines = await File.ReadAllLinesAsync(employeePath, Encoding.UTF8, cancellationToken);
            for (var index = 1; index < lines.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = lines[index].Trim();
                if (line.Length == 0) continue;

                var columns = SplitCsvLine(line);
                if (columns.Count < 2) continue;

                var workerId = columns[0].Trim().Trim('"');
                var fullName = columns[1].Trim().Trim('"');
                if (workerId.Length == 0) continue;

                records[(workerId, string.Empty)] =
                    new SkillMatrixRecord(workerId, fullName, string.Empty, 0, IncludeInSkillMap: false);

                if (columns.Count < 3) continue;
                var skills = columns[2].Trim().Trim('"');
                foreach (var rawItem in skills.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var item = rawItem.Trim();
                    var open = item.LastIndexOf('(');
                    var close = item.LastIndexOf(')');
                    if (open <= 0 || close <= open) continue;

                    var productCode = item[..open].Trim();
                    var countRaw = item[(open + 1)..close].Trim();
                    if (productCode.Length == 0 || !int.TryParse(countRaw, out var count)) continue;

                    records[(workerId, productCode)] =
                        new SkillMatrixRecord(workerId, fullName, productCode, count);
                }
            }
        }

        var productPath = FindFile(ProductFile);
        if (productPath is not null)
        {
            var lines = await File.ReadAllLinesAsync(productPath, Encoding.UTF8, cancellationToken);
            for (var index = 1; index < lines.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = lines[index].Trim();
                if (line.Length == 0) continue;

                var columns = SplitCsvLine(line);
                if (columns.Count < 2) continue;

                var productCode = columns[0].Trim().Trim('"');
                if (productCode.Length == 0) continue;

                foreach (var rawWorker in columns[1].Trim().Trim('"')
                    .Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var item = rawWorker.Trim();
                    var dash = item.IndexOf('-');
                    if (dash <= 0) continue;

                    var workerId = item[..dash].Trim();
                    var fullName = item[(dash + 1)..].Trim();
                    if (workerId.Length == 0) continue;

                    var key = (workerId, productCode);
                    // The detailed experience CSV remains authoritative; this source only fills gaps.
                    if (!records.ContainsKey(key))
                        records[key] = new SkillMatrixRecord(workerId, fullName, productCode, 1, IncludeInSkillMap: false);

                    if (!records.ContainsKey((workerId, string.Empty)))
                        records[(workerId, string.Empty)] =
                            new SkillMatrixRecord(workerId, fullName, string.Empty, 0, IncludeInSkillMap: false);
                }
            }
        }

        return records.Values.ToList();
    }

    private static string? FindFile(string fileName)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var solutionDirectory = FindSolutionDirectory(baseDirectory);
        var candidates = new[]
        {
            Path.Combine(baseDirectory, fileName),
            Path.Combine(baseDirectory, "data", fileName),
            Path.Combine(baseDirectory, "Plugins", fileName),
            Path.Combine(solutionDirectory, "doccuments", fileName),
            Path.Combine("D:/JTEC-WEB/Documents", fileName),
            Path.Combine(solutionDirectory, "Jtec.Modules", "LAPRAP", "KeHoachSX", fileName)
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string FindSolutionDirectory(string currentPath)
    {
        var directory = new DirectoryInfo(currentPath);
        while (directory is not null && !directory.GetFiles("*.sln").Any())
            directory = directory.Parent;
        return directory?.FullName ?? currentPath;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == ',' && !inQuotes)
            {
                result.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(character);
            }
        }

        result.Add(builder.ToString());
        return result;
    }

    private sealed class WorkerProductKeyComparer :
        IEqualityComparer<(string WorkerId, string ProductCode)>
    {
        public bool Equals(
            (string WorkerId, string ProductCode) left,
            (string WorkerId, string ProductCode) right)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(left.WorkerId, right.WorkerId)
                && StringComparer.OrdinalIgnoreCase.Equals(left.ProductCode, right.ProductCode);
        }

        public int GetHashCode((string WorkerId, string ProductCode) value)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.WorkerId),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.ProductCode));
        }
    }
}
