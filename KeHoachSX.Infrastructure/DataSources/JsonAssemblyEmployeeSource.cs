using KeHoachSX.Application.Contracts;
using Interfaces;
using System.Text.Json;

namespace KeHoachSX.Infrastructure.DataSources;

public sealed class JsonAssemblyEmployeeSource : IAssemblyEmployeeJsonSource
{
    public async Task<IReadOnlyList<AssemblyEmployeeRecord>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var employeePath = FindFile("NHANVIEN.json");
        var efficiencyPath = FindFile("HIEUSUATNHANVIEN.json");
        var efficiencies = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        if (efficiencyPath is not null)
        {
            await using var efficiencyStream = File.OpenRead(efficiencyPath);
            using var efficiencyDocument = await JsonDocument.ParseAsync(
                efficiencyStream, cancellationToken: cancellationToken);

            foreach (var row in GetArray(efficiencyDocument.RootElement, "HIEUSUATNHANVIEN"))
            {
                var id = GetString(row, "IDNHANVIEN").Trim();
                var raw = GetString(row, "HIEUSUAT");
                if (id.Length == 0) continue;

                if (double.TryParse(
                    raw.Replace(',', '.'),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value) && value > 0)
                    efficiencies[id] = value;
            }
        }

        if (employeePath is null) return Array.Empty<AssemblyEmployeeRecord>();

        await using var employeeStream = File.OpenRead(employeePath);
        using var employeeDocument = await JsonDocument.ParseAsync(
            employeeStream, cancellationToken: cancellationToken);

        var result = new List<AssemblyEmployeeRecord>();
        foreach (var row in GetArray(employeeDocument.RootElement, "NHANVIEN"))
        {
            var id = GetString(row, "UserName").Trim();
            if (id.Length == 0) continue;

            var efficiency = efficiencies.TryGetValue(id, out var value) && value > 0
                ? value
                : 20.0;

            result.Add(new AssemblyEmployeeRecord(
                id,
                GetString(row, "Fullname").Trim(),
                GetString(row, "GROUPBOPHAN").Trim(),
                GetString(row, "LevelUser").Trim(),
                GetString(row, "STATUS").Trim(),
                efficiency));
        }

        return result;
    }

    private static string? FindFile(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine("D:/JTEC-WEB/Documents", fileName),
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "data", fileName),
            Path.Combine(AppContext.BaseDirectory, "Documents", fileName)
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static IEnumerable<JsonElement> GetArray(JsonElement root, string propertyName)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray().ToArray();

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().ToArray();

        return Array.Empty<JsonElement>();
    }

    private static string GetString(JsonElement row, string property)
    {
        if (!row.TryGetProperty(property, out var value)) return string.Empty;
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ToString();
    }
}
