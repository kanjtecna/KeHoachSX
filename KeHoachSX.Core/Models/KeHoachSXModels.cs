using System;
using System.Collections.Generic;
using System.Linq;

namespace Models
{
    /// <summary>
    /// Cấu hình ca làm việc và các khoảng thời gian nghỉ
    /// </summary>
    public class ShiftConfig
    {
        public TimeSpan StartTime { get; set; } = new TimeSpan(7, 30, 0); // 07:30
        public TimeSpan EndTime { get; set; } = new TimeSpan(17, 0, 0);   // 17:00
        public int NumberOfDays { get; set; } = 6; // 6 ngày làm việc (Thứ 2 đến Thứ 7)

        public List<ShiftBreak> Breaks { get; set; } = new List<ShiftBreak>
        {
            new ShiftBreak { Name = "Nghỉ giải lao sáng", Start = new TimeSpan(10, 0, 0), End = new TimeSpan(10, 10, 0) },
            new ShiftBreak { Name = "Nghỉ trưa", Start = new TimeSpan(12, 0, 0), End = new TimeSpan(13, 0, 0) },
            new ShiftBreak { Name = "Nghỉ giải lao chiều", Start = new TimeSpan(15, 0, 0), End = new TimeSpan(15, 10, 0) }
        };

        public double TotalElapsedMinutesPerDay => (EndTime - StartTime).TotalMinutes;
        public double TotalBreakMinutesPerDay => Breaks.Sum(b => (b.End - b.Start).TotalMinutes);
        public double NetWorkingMinutesPerDay => TotalElapsedMinutesPerDay - TotalBreakMinutesPerDay;
        public double TotalWeeklyWorkingMinutes => NetWorkingMinutesPerDay * NumberOfDays;
    }

    public class ShiftBreak
    {
        public string Name { get; set; } = "";
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public double DurationMinutes => (End - Start).TotalMinutes;
    }

    /// <summary>
    /// Thông tin 1 dòng kế hoạch tuần được đọc từ file kế hoạch sản xuất
    /// </summary>
    public class WeeklyPlanItem
    {
        public string MaSanPham { get; set; } = "";
        public int SoLuong { get; set; } = 1;
        public string SoThuTuLot { get; set; } = "1";
        public double GioCongDinhMuc { get; set; } // Phút / Sản phẩm
        public string GhiChu { get; set; } = "";
    }

    /// <summary>
    /// Phân đoạn thời gian làm việc thực tế trong ngày (đã loại trừ giờ giải lao, ăn trưa)
    /// </summary>
    public class LotTaskChunk
    {
        public int DayIndex { get; set; } // 0: Thứ 2, 1: Thứ 3, ..., 5: Thứ 7
        public double StartMinute { get; set; } // Số phút tính từ StartTime ca làm
        public double EndMinute { get; set; }
        public double Duration => EndMinute - StartMinute;
    }

    /// <summary>
    /// Đối tượng 1 Lot công việc cụ thể được phân bổ cho 1 công nhân
    /// </summary>
    public class LotTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string MaSanPham { get; set; } = "";
        public string SoLot { get; set; } = "";
        public int TTLot { get; set; } = 1; // Thứ tự Lot (định danh khi số lượng >= 2)
        public int SoLuong { get; set; } = 1;
        public double GioCongDinhMuc { get; set; } // Phút định mức theo hệ thống
        public double ThoiGianThucTe { get; set; } // Phút thực tế sau khi tính theo hiệu suất

        public string? AssignedWorkerId { get; set; }
        public string? AssignedWorkerName { get; set; }
        public int SkillCount { get; set; } = 0; // Số lần công nhân này từng làm mã sản phẩm này

        public int DayIndex { get; set; } = 0; // 0 = Thứ 2, 1 = Thứ 3, ..., 5 = Thứ 7
        public double StartMinuteInDay { get; set; }
        public double EndMinuteInDay { get; set; }

        public bool IsAssigned => !string.IsNullOrEmpty(AssignedWorkerId);
        public List<LotTaskChunk> Chunks { get; set; } = new();

        public string ColorHex { get; set; } = "#0d6efd";
    }

    /// <summary>
    /// Thông tin công nhân công đoạn lắp ráp kèm ma trận kỹ năng và lịch làm việc
    /// </summary>
    public class AssemblyWorker
    {
        public string WorkerId { get; set; } = "";
        public string FullName { get; set; } = "";
        public string GroupName { get; set; } = "Tổ Lắp ráp";
        public double HieuSuat { get; set; } = 20.0; // % Hiệu suất mặc định

        public double DailyCapacityMinutes { get; set; } = 490.0;
        public double WeeklyCapacityMinutes { get; set; } = 2940.0;

        // Ma trận kỹ năng của riêng công nhân này: Mã SP -> Số lần từng làm
        public Dictionary<string, int> SkillMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // Lịch công việc đã phân chia theo ngày (DayIndex: 0..5 -> Danh sách LotTask)
        public Dictionary<int, List<LotTask>> DailyTasks { get; set; } = new();

        public double TotalAssignedMinutes => DailyTasks.Values.SelectMany(t => t).Sum(t => t.ThoiGianThucTe);
        public double UtilizationRate => WeeklyCapacityMinutes > 0 ? (TotalAssignedMinutes / WeeklyCapacityMinutes) * 100.0 : 0.0;

        public double GetDayAssignedMinutes(int dayIndex)
        {
            if (DailyTasks.TryGetValue(dayIndex, out var tasks))
            {
                return tasks.Sum(t => t.ThoiGianThucTe);
            }
            return 0.0;
        }

        public int GetSkillCount(string maSanPham)
        {
            if (string.IsNullOrWhiteSpace(maSanPham)) return 0;
            return SkillMap.TryGetValue(maSanPham.Trim(), out int count) ? count : 0;
        }
    }

    /// <summary>
    /// Kết quả chạy thuật toán phân chia công việc tự động
    /// </summary>
    public class DispatchResult
    {
        public ShiftConfig Shift { get; set; } = new();
        public List<AssemblyWorker> Workers { get; set; } = new();
        public List<LotTask> AllLots { get; set; } = new();
        public List<LotTask> AssignedLots => AllLots.Where(l => l.IsAssigned).ToList();
        public List<LotTask> UnassignedLots => AllLots.Where(l => !l.IsAssigned).ToList();

        public double TotalPlanMinutes => AllLots.Sum(l => l.ThoiGianThucTe);
        public double TotalAssignedMinutes => AssignedLots.Sum(l => l.ThoiGianThucTe);
        public double TotalCapacityMinutes => Workers.Sum(w => w.WeeklyCapacityMinutes);
        public double OverallUtilizationPercentage => TotalCapacityMinutes > 0 ? (TotalAssignedMinutes / TotalCapacityMinutes) * 100.0 : 0.0;

        public int TotalLotsCount => AllLots.Count;
        public int AssignedLotsCount => AssignedLots.Count;
        public int UnassignedLotsCount => UnassignedLots.Count;
    }

    /// <summary>
    /// Tham số tùy chọn thuật toán phân chia công việc
    /// </summary>
    public class DispatchOptions
    {
        public bool BalanceWorkload { get; set; } = true; // Cân bằng tải đều giữa các công nhân
        public bool PrioritizeExperiencedWorkers { get; set; } = true; // Ưu tiên người đã từng làm nhiều lần
        public bool KeepSameProductContinuous { get; set; } = true; // Ưu tiên giao các Lot cùng mã SP cho cùng người để tránh đổi bàn gá
        public double DefaultEfficiency { get; set; } = 100.0; // Hiệu suất mặc định (%)
    }
}
