using System.ComponentModel.DataAnnotations;

namespace Models;

// Model mô phỏng file Excel/Dữ liệu nhập
public class ProductionInput
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [Required]
    public string MaSanPham { get; set; } = "";
    public string CongDoan { get; set; } = "";
    [Range(1, int.MaxValue)]
    public int SoLuong { get; set; }
    public int SoLuongGoc { get; set; } // Để theo dõi lúc thay đổi số lượng tách dòng
    public double GioCongDinhMuc { get; set; } // Phút/Sản phẩm

    // Danh sách nhân viên được phân công cho dòng sản phẩm này
    public List<AssignedWorker> AssignedWorkers { get; set; } = new();
}

// Model dữ liệu hiệu suất
public class WorkerEfficiency
{
    public string UserCode { get; set; } = "";
    public double HieuSuat { get; set; } // % Hiệu suất (VD: 40)
}

// Model phân công nhân viên vào 1 công đoạn
public class AssignedWorker
{
    public string UserCode { get; set; } = "";
    public double HieuSuat { get; set; }
    // Cờ đánh dấu để binding UI
    public bool IsSelected { get; set; }
}