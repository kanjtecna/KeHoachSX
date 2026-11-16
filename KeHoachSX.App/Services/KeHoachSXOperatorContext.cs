using Models.Login;
using Microsoft.Extensions.Configuration;
using Abstractions;
using KeHoachSX.Application.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace KeHoachSX.App.Services
{
    public sealed class KeHoachSXOperatorContext : ICurrentOperatorProvider
    {
        private readonly UserState _userState;
        private readonly IOperatorRepository _operatorRepo;
        private readonly IConfiguration _configuration;

        public KeHoachSXOperatorContext(
            UserState userState,
            IOperatorRepository operatorRepo,
            IConfiguration configuration)
        {
            _userState = userState;
            _operatorRepo = operatorRepo;
            _configuration = configuration;
        }

        public async Task<OperatorContext> GetCurrentAsync(CancellationToken ct)
        {
            var machine = Environment.MachineName;

            // 1. Lấy mã nhân viên từ phiên đăng nhập UserState (ưu tiên hàng đầu)
            var empCode = _userState.UserName?.Trim();

            // 2. Fallback cấu hình nếu chạy chế độ test / dev mà không qua màn đăng nhập
            if (string.IsNullOrWhiteSpace(empCode))
            {
                empCode = _configuration["KeHoachSX:EmployeeCode"]?.Trim();
            }

            if (string.IsNullOrWhiteSpace(empCode))
            {
                // Chưa đăng nhập: trả về ngữ cảnh rỗng để UI yêu cầu đăng nhập
                return new OperatorContext(string.Empty, string.Empty, machine);
            }

            // 3. Tra cứu thêm thông tin chi tiết từ DB Operator (nếu có)
            var op = await _operatorRepo.GetAsync(empCode, machine, ct);
            if (op != null)
            {
                return op;
            }

            // Fallback: nếu DB không có dòng tương ứng thì dùng thông tin từ UserState
            var name = !string.IsNullOrWhiteSpace(_userState.FullName) ? _userState.FullName : empCode;
            return new OperatorContext(empCode, name, machine);
        }
    }
}
