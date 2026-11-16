using Interfaces;
using JtecShare.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Services;
using System;
using System.Threading.Tasks;

namespace KeHoachSX.Infrastructure
{
    public class KeHoachSXRegisterServices : IModuleRegistration
    {
        public void RegisterServices(IServiceCollection services, IConfiguration configuration)
        {
            // 1. Đăng ký Ma trận kỹ năng (Dạng Singleton để lưu cache RAM không phải đọc lại file liên tục)
            services.AddSingleton<ISkillMatrixService, SkillMatrixService>();

            // 2. Đăng ký dịch vụ Tra cứu giờ công tiêu chuẩn
            services.AddSingleton<IStandardTimeService, StandardTimeService>();

            // 3. Đăng ký dịch vụ Lấy danh sách nhân viên công đoạn lắp ráp
            services.AddScoped<IAssemblyEmployeeService, AssemblyEmployeeService>();

            // 4. Đăng ký dịch vụ Đọc & Xử lý file kế hoạch tuần (CSV / Excel)
            services.AddScoped<IWeeklyPlanParserService, WeeklyPlanParserService>();

            // 5. Đăng ký Thuật toán Tự động Phân chia Công việc Thông minh
            services.AddScoped<IAutoDispatchService, AutoDispatchService>();
        }

        public void Initialize(IServiceProvider serviceProvider)
        {
            // Pre-warm nạp ma trận kỹ năng vào RAM ngay khi app khởi động
            try
            {
                var skillMatrix = serviceProvider.GetService<ISkillMatrixService>();
                if (skillMatrix != null)
                {
                    _ = Task.Run(() => skillMatrix.EnsureLoadedAsync());
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[KeHoachSX] Lỗi khởi tạo SkillMatrix: {ex.Message}");
            }
        }
    }
}