using ClosedXML.Excel;
using Interfaces;
using Models;
using System.Globalization;
using System.Text;

namespace KeHoachSX.Infrastructure.DataSources;

public sealed class WeeklyPlanFileReader : IWeeklyPlanFileReader
{
    public async Task<List<WeeklyPlanItem>> ReadAsync(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return await Task.Run(
            () => extension is ".xlsx" or ".xls" ? ParseExcel(stream) : ParseCsv(stream),
            cancellationToken);
    }

    private static List<WeeklyPlanItem> ParseExcel(Stream stream)
    {
        var result = new List<WeeklyPlanItem>();
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet is null) return result;

        var rows = worksheet.RangeUsed()?.RowsUsed().ToList();
        if (rows is null || rows.Count <= 1) return result;

        var headerRow = rows[0];
        var columnCount = headerRow.LastCellUsed()?.Address.ColumnNumber ?? headerRow.CellCount();
        var productColumn = -1;
        var quantityColumn = -1;
        var lotColumn = -1;
        var standardTimeColumn = -1;

        for (var column = 1; column <= columnCount; column++)
        {
            var header = NormalizeHeader(headerRow.Cell(column).GetString());
            if (productColumn == -1 && IsProductHeader(header)) productColumn = column;
            else if (quantityColumn == -1 && IsQuantityHeader(header)) quantityColumn = column;
            else if (lotColumn == -1 && IsLotHeader(header)) lotColumn = column;
            else if (standardTimeColumn == -1 && IsStandardTimeHeader(header)) standardTimeColumn = column;
        }

        if (productColumn == -1) productColumn = 1;
        if (quantityColumn == -1) quantityColumn = 2;
        if (lotColumn == -1) lotColumn = 3;

        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var product = row.Cell(productColumn).GetString().Trim();
            if (product.Length == 0) continue;

            var quantity = 1;
            if (row.Cell(quantityColumn).TryGetValue(out int parsedQuantity))
                quantity = parsedQuantity;
            else if (!int.TryParse(row.Cell(quantityColumn).GetString(), out quantity))
                quantity = 1;

            var lot = row.Cell(lotColumn).GetString().Trim();
            if (lot.Length == 0) lot = "01";

            var standardMinutes = 0.0;
            if (standardTimeColumn > 0
                && !row.Cell(standardTimeColumn).TryGetValue(out standardMinutes))
            {
                double.TryParse(
                    row.Cell(standardTimeColumn).GetString(),
                    NumberStyles.Any,
                    CultureInfo.CurrentCulture,
                    out standardMinutes);
            }

            result.Add(new WeeklyPlanItem
            {
                MaSanPham = product,
                SoLuong = quantity,
                SoThuTuLot = lot,
                GioCongDinhMuc = standardMinutes
            });
        }

        return result;
    }

    private static List<WeeklyPlanItem> ParseCsv(Stream stream)
    {
        var result = new List<WeeklyPlanItem>();
        using var reader = new StreamReader(
            stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);

        var headerLine = reader.ReadLine();
        if (headerLine is null) return result;

        var delimiter = DetectDelimiter(headerLine);
        var headers = SplitCsvLine(headerLine, delimiter).Select(NormalizeHeader).ToList();

        var productColumn = FindHeader(headers, IsProductHeader, 0);
        var quantityColumn = FindHeader(headers, IsQuantityHeader, 1);
        var lotColumn = FindHeader(headers, IsLotHeader, 2);
        var standardTimeColumn = FindHeader(headers, IsStandardTimeHeader, -1);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var columns = SplitCsvLine(line, delimiter);
            if (productColumn >= columns.Count) continue;

            var product = columns[productColumn].Trim();
            if (product.Length == 0) continue;

            var quantity = 1;
            if (quantityColumn < columns.Count
                && !int.TryParse(columns[quantityColumn].Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out quantity))
                quantity = 1;

            var lot = lotColumn < columns.Count ? columns[lotColumn].Trim() : "01";
            if (lot.Length == 0) lot = "01";

            var standardMinutes = 0.0;
            if (standardTimeColumn >= 0 && standardTimeColumn < columns.Count)
                double.TryParse(columns[standardTimeColumn].Trim(), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out standardMinutes);

            result.Add(new WeeklyPlanItem
            {
                MaSanPham = product,
                SoLuong = quantity,
                SoThuTuLot = lot,
                GioCongDinhMuc = standardMinutes
            });
        }

        return result;
    }

    private static int FindHeader(IReadOnlyList<string> headers, Func<string, bool> predicate, int fallback)
    {
        for (var index = 0; index < headers.Count; index++)
            if (predicate(headers[index])) return index;
        return fallback;
    }

    private static bool IsProductHeader(string header) =>
        header.Contains("san pham") || header.Contains("ma") ||
        header.Contains("product") || header.Contains("idproduct") || header.Contains("hinmoku");

    private static bool IsQuantityHeader(string header) =>
        header.Contains("luong") || header.Contains("qty") ||
        header.Contains("quantity") || header == "sl" || header.StartsWith("sl ");

    private static bool IsLotHeader(string header) =>
        header.Contains("lot") || header.Contains("seq") ||
        header.Contains("thu tu") || header.Contains("ttlot");

    private static bool IsStandardTimeHeader(string header) =>
        header.Contains("gio cong") || header.Contains("dinh muc") ||
        header.Contains("time") || header.Contains("giocong");

    private static string NormalizeHeader(string value)
    {
        var normalized = value.Trim().Trim('"').ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(character == 'đ' ? 'd' : character);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static char DetectDelimiter(string header) =>
        CountDelimiterOutsideQuotes(header, ';') > CountDelimiterOutsideQuotes(header, ',') ? ';' : ',';

    private static int CountDelimiterOutsideQuotes(string value, char delimiter)
    {
        var count = 0;
        var inQuotes = false;
        foreach (var character in value)
        {
            if (character == '"') inQuotes = !inQuotes;
            else if (character == delimiter && !inQuotes) count++;
        }
        return count;
    }

    private static List<string> SplitCsvLine(string line, char delimiter)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == delimiter && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else current.Append(character);
        }

        values.Add(current.ToString());
        return values;
    }
}
