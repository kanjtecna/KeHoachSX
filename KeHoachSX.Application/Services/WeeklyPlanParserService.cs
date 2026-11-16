using Interfaces;
using Models;

namespace Services;

public sealed class WeeklyPlanParserService : IWeeklyPlanParserService
{
    private readonly IStandardTimeService _standardTimeService;
    private readonly IWeeklyPlanFileReader _fileReader;

    public WeeklyPlanParserService(
        IStandardTimeService standardTimeService,
        IWeeklyPlanFileReader fileReader)
    {
        _standardTimeService = standardTimeService;
        _fileReader = fileReader;
    }

    public async Task<List<LotTask>> ParseWeeklyPlanAsync(
        Stream fileStream,
        string fileName,
        double defaultMinutes = 60.0)
    {
        var rawItems = await _fileReader.ReadAsync(fileStream, fileName);
        var lotTasks = new List<LotTask>();

        foreach (var item in rawItems)
        {
            if (string.IsNullOrWhiteSpace(item.MaSanPham)) continue;

            // Data in the uploaded plan keeps priority over a DB lookup.
            var standardMinutes = item.GioCongDinhMuc > 0
                ? item.GioCongDinhMuc
                : await _standardTimeService.GetStandardMinutesAsync(item.MaSanPham);
            if (standardMinutes <= 0) standardMinutes = defaultMinutes;

            var quantity = Math.Max(1, item.SoLuong);
            if (quantity == 1)
            {
                lotTasks.Add(CreateLot(
                    item.MaSanPham.Trim(),
                    string.IsNullOrWhiteSpace(item.SoThuTuLot) ? "L01" : item.SoThuTuLot.Trim(),
                    1,
                    standardMinutes));
                continue;
            }

            var baseLot = string.IsNullOrWhiteSpace(item.SoThuTuLot) ? "L" : item.SoThuTuLot.Trim();
            for (var sequence = 1; sequence <= quantity; sequence++)
            {
                var lotName = baseLot.All(char.IsDigit)
                    ? (int.Parse(baseLot) + sequence - 1).ToString("D2")
                    : $"{baseLot}-{sequence:D2}";
                lotTasks.Add(CreateLot(item.MaSanPham.Trim(), lotName, sequence, standardMinutes));
            }
        }

        return lotTasks;
    }

    private static LotTask CreateLot(string productCode, string lotName, int sequence, double standardMinutes)
    {
        return new LotTask
        {
            MaSanPham = productCode,
            SoLot = lotName,
            TTLot = sequence,
            SoLuong = 1,
            GioCongDinhMuc = standardMinutes,
            // Retain the existing application's 30% baseline calculation.
            ThoiGianThucTe = standardMinutes / 0.3
        };
    }
}
