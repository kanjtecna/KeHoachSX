using ClosedXML.Excel;
using Interfaces;
using Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Services
{
    public class WeeklyPlanParserService : IWeeklyPlanParserService
    {
        private readonly IStandardTimeService _standardTimeService;

        public WeeklyPlanParserService(IStandardTimeService standardTimeService)
        {
            _standardTimeService = standardTimeService;
        }

        public async Task<List<LotTask>> ParseWeeklyPlanAsync(Stream fileStream, string fileName, double defaultMinutes = 60.0)
        {
            var rawPlanItems = new List<WeeklyPlanItem>();
            string ext = Path.GetExtension(fileName).ToLowerInvariant();

            if (ext == ".xlsx" || ext == ".xls")
            {
                rawPlanItems = ParseExcel(fileStream);
            }
            else
            {
                rawPlanItems = ParseCsv(fileStream);
            }

            // Chuyển đổi và bóc tách từng dòng thành các LotTask đơn lẻ
            var lotTasks = new List<LotTask>();

            foreach (var item in rawPlanItems)
            {
                if (string.IsNullOrWhiteSpace(item.MaSanPham)) continue;

                // Lấy giờ công định mức từ DB nếu chưa có trong file
                double standardMinutes = item.GioCongDinhMuc > 0 
                    ? item.GioCongDinhMuc 
                    : await _standardTimeService.GetStandardMinutesAsync(item.MaSanPham);

                if (standardMinutes <= 0) standardMinutes = defaultMinutes;

                int qty = Math.Max(1, item.SoLuong);

                if (qty == 1)
                {
                    lotTasks.Add(new LotTask
                    {
                        MaSanPham = item.MaSanPham.Trim(),
                        SoLot = string.IsNullOrWhiteSpace(item.SoThuTuLot) ? "L01" : item.SoThuTuLot.Trim(),
                        TTLot = 1,
                        SoLuong = 1,
                        GioCongDinhMuc = standardMinutes,
                        ThoiGianThucTe = standardMinutes / 0.3 // Hiệu suất mặc định 100%
                    });
                }
                else
                {
                    // Khi số lượng >= 2: Tách từng Lot định danh riêng theo thứ tự Lot
                    string baseLot = string.IsNullOrWhiteSpace(item.SoThuTuLot) ? "L" : item.SoThuTuLot.Trim();
                    
                    for (int seq = 1; seq <= qty; seq++)
                    {
                        string lotName = baseLot.All(char.IsDigit) 
                            ? (int.Parse(baseLot) + seq - 1).ToString("D2")
                            : $"{baseLot}-{seq:D2}";

                        lotTasks.Add(new LotTask
                        {
                            MaSanPham = item.MaSanPham.Trim(),
                            SoLot = lotName,
                            TTLot = seq,
                            SoLuong = 1,
                            GioCongDinhMuc = standardMinutes,
                            ThoiGianThucTe = standardMinutes / 0.3
                        });
                    }
                }
            }

            return lotTasks;
        }

        private List<WeeklyPlanItem> ParseExcel(Stream stream)
        {
            var list = new List<WeeklyPlanItem>();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet == null) return list;

            var rows = worksheet.RangeUsed()?.RowsUsed()?.ToList();
            if (rows == null || rows.Count <= 1) return list;

            var headerRow = rows[0];
            int colProduct = -1, colQty = -1, colLot = -1, colGc = -1;

            for (int c = 1; c <= headerRow.CellCount(); c++)
            {
                string header = headerRow.Cell(c).GetString().Trim().ToLowerInvariant();
                if (colProduct == -1 && (header.Contains("sản phẩm") || header.Contains("mã") || header.Contains("product") || header.Contains("hinmoku")))
                    colProduct = c;
                else if (colQty == -1 && (header.Contains("lượng") || header.Contains("qty") || header.Contains("quantity") || header.Contains("sl")))
                    colQty = c;
                else if (colLot == -1 && (header.Contains("lot") || header.Contains("seq") || header.Contains("thứ tự")))
                    colLot = c;
                else if (colGc == -1 && (header.Contains("giờ công") || header.Contains("định mức") || header.Contains("time") || header.Contains("giocong")))
                    colGc = c;
            }

            if (colProduct == -1) colProduct = 1;
            if (colQty == -1) colQty = 2;
            if (colLot == -1) colLot = 3;

            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                string product = row.Cell(colProduct).GetString().Trim();
                if (string.IsNullOrWhiteSpace(product)) continue;

                int qty = 1;
                if (row.Cell(colQty).TryGetValue(out int parsedQty)) qty = parsedQty;
                else if (int.TryParse(row.Cell(colQty).GetString(), out int q)) qty = q;

                string lot = row.Cell(colLot).GetString().Trim();
                if (string.IsNullOrWhiteSpace(lot)) lot = "01";

                double gc = 0;
                if (colGc > 0)
                {
                    if (row.Cell(colGc).TryGetValue(out double parsedGc)) gc = parsedGc;
                    else double.TryParse(row.Cell(colGc).GetString(), out gc);
                }

                list.Add(new WeeklyPlanItem
                {
                    MaSanPham = product,
                    SoLuong = qty,
                    SoThuTuLot = lot,
                    GioCongDinhMuc = gc
                });
            }

            return list;
        }

        private List<WeeklyPlanItem> ParseCsv(Stream stream)
        {
            var list = new List<WeeklyPlanItem>();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string? headerLine = reader.ReadLine();
            if (headerLine == null) return list;

            var headers = headerLine.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(h => h.Trim().Trim('"').ToLowerInvariant()).ToList();

            int colProduct = -1, colQty = -1, colLot = -1, colGc = -1;

            for (int i = 0; i < headers.Count; i++)
            {
                string h = headers[i];
                if (colProduct == -1 && (h.Contains("sản phẩm") || h.Contains("mã") || h.Contains("product") || h.Contains("idproduct") || h.Contains("hinmoku")))
                    colProduct = i;
                else if (colQty == -1 && (h.Contains("lượng") || h.Contains("qty") || h.Contains("quantity") || h.Contains("sl")))
                    colQty = i;
                else if (colLot == -1 && (h.Contains("lot") || h.Contains("seq") || h.Contains("thứ tự") || h.Contains("ttlot")))
                    colLot = i;
                else if (colGc == -1 && (h.Contains("giờ công") || h.Contains("định mức") || h.Contains("time") || h.Contains("giocong")))
                    colGc = i;
            }

            if (colProduct == -1) colProduct = 0;
            if (colQty == -1) colQty = 1;
            if (colLot == -1) colLot = 2;

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(new[] { ',', ';' });
                if (parts.Length <= colProduct) continue;

                string product = parts[colProduct].Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(product)) continue;

                int qty = 1;
                if (parts.Length > colQty && int.TryParse(parts[colQty].Trim().Trim('"'), out int q)) qty = q;

                string lot = parts.Length > colLot ? parts[colLot].Trim().Trim('"') : "01";
                if (string.IsNullOrWhiteSpace(lot)) lot = "01";

                double gc = 0;
                if (colGc >= 0 && parts.Length > colGc)
                {
                    double.TryParse(parts[colGc].Trim().Trim('"'), NumberStyles.Any, CultureInfo.InvariantCulture, out gc);
                }

                list.Add(new WeeklyPlanItem
                {
                    MaSanPham = product,
                    SoLuong = qty,
                    SoThuTuLot = lot,
                    GioCongDinhMuc = gc
                });
            }

            return list;
        }
    }
}
