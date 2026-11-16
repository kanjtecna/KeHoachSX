using Interfaces;
using Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Services
{
    public class AutoDispatchService : IAutoDispatchService
    {
        private static readonly string[] ProductPalette = new[]
        {
            "#1976d2", "#388e3c", "#d32f2f", "#7b1fa2", "#f57c00",
            "#0097a7", "#c2185b", "#512da8", "#0288d1", "#00796b",
            "#689f38", "#ef6c00", "#455a64", "#e64a19", "#5d4037"
        };

        public DispatchResult AutoDispatch(
            List<LotTask> tasks,
            List<AssemblyWorker> workers,
            ShiftConfig shift,
            DispatchOptions? options = null)
        {
            options ??= new DispatchOptions();
            var result = new DispatchResult
            {
                Shift = shift,
                Workers = workers.Select(CloneWorkerSkeleton).ToList(),
                AllLots = tasks.Select(t => CloneTask(t)).ToList()
            };

            if (result.Workers.Count == 0 || result.AllLots.Count == 0)
            {
                return result;
            }

            // Gán màu sắc phân biệt cho từng mã sản phẩm
            var uniqueProducts = result.AllLots.Select(l => l.MaSanPham).Distinct().ToList();
            var colorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < uniqueProducts.Count; i++)
            {
                colorMap[uniqueProducts[i]] = ProductPalette[i % ProductPalette.Length];
            }

            foreach (var lot in result.AllLots)
            {
                if (colorMap.TryGetValue(lot.MaSanPham, out var color))
                {
                    lot.ColorHex = color;
                }
            }

            // Thiết lập sức chứa theo cấu hình ca làm việc
            foreach (var w in result.Workers)
            {
                w.DailyCapacityMinutes = shift.NetWorkingMinutesPerDay;
                w.WeeklyCapacityMinutes = shift.TotalWeeklyWorkingMinutes;
                w.DailyTasks.Clear();
                for (int d = 0; d < shift.NumberOfDays; d++)
                {
                    w.DailyTasks[d] = new List<LotTask>();
                }
            }

            // Bảng theo dõi đồng hồ làm việc hiện tại của từng công nhân cho mỗi ngày (tính bằng phút từ StartTime)
            // [WorkerId, DayIndex] -> currentMinute
            var workerClock = new Dictionary<string, double[]>();
            foreach (var w in result.Workers)
            {
                workerClock[w.WorkerId] = new double[shift.NumberOfDays];
                for (int d = 0; d < shift.NumberOfDays; d++) workerClock[w.WorkerId][d] = 0.0;
            }

            // Theo dõi công nhân vừa làm mã sản phẩm nào gần nhất để ưu tiên làm liên tục (giảm thời gian setup bàn gá)
            var lastProductWorked = new Dictionary<string, string>();

            // 1. SẮP XẾP NHIỆM VỤ THEO THUẬT TOÁN TỐI ƯU
            // Gom nhóm theo mã sản phẩm, sau đó sắp xếp các mã có tổng thời gian lớn nhất trước (LPT - Longest Processing Time)
            // hoặc các mã có ít thợ biết làm nhất trước (Most Constrained First)
            var groupedTasks = result.AllLots
                .GroupBy(l => l.MaSanPham)
                .OrderByDescending(g => g.Count(lot => true) * g.First().ThoiGianThucTe)
                .ToList();

            var prioritizedTasks = new List<LotTask>();
            foreach (var group in groupedTasks)
            {
                // Sắp xếp các lot cùng mã theo thứ tự Lot (TTLot) tăng dần (L01, L02, L03...)
                prioritizedTasks.AddRange(group.OrderBy(l => l.TTLot));
            }

            // 2. PHÂN BỔ TỪNG LOT TASK VÀO CÔNG NHÂN PHÙ HỢP
            foreach (var task in prioritizedTasks)
            {
                double baseDuration = task.GioCongDinhMuc > 0 ? task.GioCongDinhMuc : task.ThoiGianThucTe;
                if (baseDuration <= 0) baseDuration = 60.0;

                // Tìm công nhân có điểm số phù hợp nhất
                var candidateScores = new List<(AssemblyWorker Worker, double Score)>();

                foreach (var worker in result.Workers)
                {
                    double workerDuration = GetTaskDuration(task, worker, baseDuration);

                    // A. Điểm số kinh nghiệm (Skill Score)
                    int skillCount = worker.GetSkillCount(task.MaSanPham);
                    double skillScore = 0.0;
                    if (skillCount > 0)
                    {
                        // Hàm logarit để làm mượt số lần làm (1 lần ~ 0.5, 10 lần ~ 0.75, 50+ lần ~ 1.0)
                        skillScore = Math.Min(1.0, 0.4 + 0.6 * (Math.Log(1 + skillCount) / Math.Log(60)));
                    }
                    else
                    {
                        // Kiểm tra nếu từng làm mã cùng họ (tiền tố mã)
                        string prefix = task.MaSanPham.Length > 6 ? task.MaSanPham.Substring(0, 6) : task.MaSanPham;
                        if (worker.SkillMap.Keys.Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                        {
                            skillScore = 0.2; // Điểm khuyến khích làm họ tương tự
                        }
                    }

                    // B. Điểm cân bằng tải (Workload Balancing Score)
                    double remainingCapacityRatio = worker.WeeklyCapacityMinutes > 0
                        ? (1.0 - (worker.TotalAssignedMinutes / worker.WeeklyCapacityMinutes))
                        : 0.0;
                    double balanceScore = Math.Max(0.0, remainingCapacityRatio);

                    // C. Điểm liên tục mã hàng (Setup Continuity Bonus)
                    double continuityBonus = 0.0;
                    if (lastProductWorked.TryGetValue(worker.WorkerId, out var lastProd) &&
                        lastProd.Equals(task.MaSanPham, StringComparison.OrdinalIgnoreCase))
                    {
                        continuityBonus = 0.35; // Thưởng điểm lớn nếu làm tiếp cùng mã hàng
                    }

                    // Tổng hợp điểm số (Trọng số: Kỹ năng 50%, Cân bằng tải 35%, Liên tục 15%)
                    double totalScore = (skillScore * 0.50) + (balanceScore * 0.35) + (continuityBonus * 0.15);

                    // Nếu công nhân đã đầy 100% cả tuần thì trừ điểm mạnh
                    if (worker.TotalAssignedMinutes + workerDuration > worker.WeeklyCapacityMinutes)
                    {
                        totalScore -= 2.0;
                    }

                    candidateScores.Add((worker, totalScore));
                }

                // Sắp xếp các ứng viên điểm cao nhất lên đầu
                var rankedCandidates = candidateScores.OrderByDescending(c => c.Score).Select(c => c.Worker).ToList();

                bool isTaskScheduled = false;

                // Thử xếp lịch cho ứng viên có điểm cao nhất
                foreach (var candidate in rankedCandidates)
                {
                    double duration = GetTaskDuration(task, candidate, baseDuration);

                    // Tìm ngày trong tuần có đủ thời gian làm
                    for (int day = 0; day < shift.NumberOfDays; day++)
                    {
                        double usedMinutesInDay = candidate.GetDayAssignedMinutes(day);
                        if (usedMinutesInDay + duration <= shift.NetWorkingMinutesPerDay + 0.1) // Cho phép sai số nhỏ
                        {
                            // Tìm được vị trí! Bắt đầu tính toán các Chunk theo giờ nghỉ
                            double startClock = workerClock[candidate.WorkerId][day];
                            var chunks = CalculateChunks(startClock, duration, shift);

                            if (chunks.Any())
                            {
                                task.AssignedWorkerId = candidate.WorkerId;
                                task.AssignedWorkerName = candidate.FullName;
                                task.ThoiGianThucTe = duration;
                                task.SkillCount = candidate.GetSkillCount(task.MaSanPham);
                                task.DayIndex = day;
                                task.StartMinuteInDay = chunks.First().StartMinute;
                                task.EndMinuteInDay = chunks.Last().EndMinute;
                                task.Chunks = chunks;

                                candidate.DailyTasks[day].Add(task);
                                workerClock[candidate.WorkerId][day] = task.EndMinuteInDay;
                                lastProductWorked[candidate.WorkerId] = task.MaSanPham;

                                isTaskScheduled = true;
                                break;
                            }
                        }
                    }

                    if (isTaskScheduled) break;
                }

                // Nếu không ngày nào vừa khít hoàn toàn trong giới hạn tiêu chuẩn,
                // tìm công nhân còn ít giờ nhất để giao (có thể vượt nhẹ over-capacity trong ngày cuối)
                if (!isTaskScheduled)
                {
                    var leastLoadedWorker = rankedCandidates.FirstOrDefault();
                    if (leastLoadedWorker != null)
                    {
                        double duration = GetTaskDuration(task, leastLoadedWorker, baseDuration);

                        // Tìm ngày còn ít việc nhất của thợ này
                        int bestDay = 0;
                        double minDayLoad = double.MaxValue;
                        for (int d = 0; d < shift.NumberOfDays; d++)
                        {
                            double load = leastLoadedWorker.GetDayAssignedMinutes(d);
                            if (load < minDayLoad)
                            {
                                minDayLoad = load;
                                bestDay = d;
                            }
                        }

                        double startClock = workerClock[leastLoadedWorker.WorkerId][bestDay];
                        var chunks = CalculateChunks(startClock, duration, shift);

                        task.AssignedWorkerId = leastLoadedWorker.WorkerId;
                        task.AssignedWorkerName = leastLoadedWorker.FullName;
                        task.ThoiGianThucTe = duration;
                        task.SkillCount = leastLoadedWorker.GetSkillCount(task.MaSanPham);
                        task.DayIndex = bestDay;
                        task.StartMinuteInDay = chunks.Any() ? chunks.First().StartMinute : startClock;
                        task.EndMinuteInDay = chunks.Any() ? chunks.Last().EndMinute : startClock + duration;
                        task.Chunks = chunks;

                        leastLoadedWorker.DailyTasks[bestDay].Add(task);
                        workerClock[leastLoadedWorker.WorkerId][bestDay] = task.EndMinuteInDay;
                        lastProductWorked[leastLoadedWorker.WorkerId] = task.MaSanPham;
                    }
                }
            }

            return result;
        }

        private static double GetTaskDuration(LotTask task, AssemblyWorker worker, double baseDuration)
        {
            double efficiency = worker.HieuSuat > 0 ? worker.HieuSuat : 20.0;
            return baseDuration / (efficiency / 100.0);
        }

        private static List<LotTaskChunk> CalculateChunks(double startMin, double duration, ShiftConfig shift)
        {
            var chunks = new List<LotTaskChunk>();
            double remaining = duration;
            double current = startMin;

            // Nhảy qua nếu startMin rơi trúng vào giờ giải lao
            current = PushPastBreaks(current, shift);

            while (remaining > 0.001)
            {
                // Tìm giờ nghỉ giải lao tiếp theo
                double nextBreakStart = double.MaxValue;
                double nextBreakEnd = double.MaxValue;

                foreach (var b in shift.Breaks.OrderBy(b => b.Start))
                {
                    double bStart = (b.Start - shift.StartTime).TotalMinutes;
                    double bEnd = (b.End - shift.StartTime).TotalMinutes;

                    if (bStart > current)
                    {
                        nextBreakStart = bStart;
                        nextBreakEnd = bEnd;
                        break;
                    }
                }

                double availableUntilBreak = nextBreakStart - current;

                if (availableUntilBreak >= remaining)
                {
                    chunks.Add(new LotTaskChunk
                    {
                        StartMinute = current,
                        EndMinute = current + remaining
                    });
                    current += remaining;
                    remaining = 0;
                }
                else
                {
                    if (availableUntilBreak > 0)
                    {
                        chunks.Add(new LotTaskChunk
                        {
                            StartMinute = current,
                            EndMinute = nextBreakStart
                        });
                        remaining -= availableUntilBreak;
                    }
                    current = nextBreakEnd; // Chuyển sang sau giờ giải lao
                }

                // Nếu đã vượt quá giờ kết thúc ca, vẫn tạo nốt chunk còn lại
                if (current >= shift.TotalElapsedMinutesPerDay && remaining > 0)
                {
                    chunks.Add(new LotTaskChunk
                    {
                        StartMinute = current,
                        EndMinute = current + remaining
                    });
                    break;
                }
            }

            return chunks;
        }

        private static double PushPastBreaks(double current, ShiftConfig shift)
        {
            foreach (var b in shift.Breaks)
            {
                double bStart = (b.Start - shift.StartTime).TotalMinutes;
                double bEnd = (b.End - shift.StartTime).TotalMinutes;
                if (current >= bStart && current < bEnd)
                {
                    return bEnd;
                }
            }
            return current;
        }

        private static AssemblyWorker CloneWorkerSkeleton(AssemblyWorker w)
        {
            return new AssemblyWorker
            {
                WorkerId = w.WorkerId,
                FullName = w.FullName,
                GroupName = w.GroupName,
                HieuSuat = w.HieuSuat,
                DailyCapacityMinutes = w.DailyCapacityMinutes,
                WeeklyCapacityMinutes = w.WeeklyCapacityMinutes,
                SkillMap = w.SkillMap
            };
        }

        private static LotTask CloneTask(LotTask t)
        {
            return new LotTask
            {
                Id = t.Id,
                MaSanPham = t.MaSanPham,
                SoLot = t.SoLot,
                TTLot = t.TTLot,
                SoLuong = t.SoLuong,
                GioCongDinhMuc = t.GioCongDinhMuc,
                ThoiGianThucTe = t.ThoiGianThucTe,
                ColorHex = t.ColorHex
            };
        }
    }
}
