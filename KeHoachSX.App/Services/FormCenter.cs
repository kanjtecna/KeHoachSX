using System;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Services;

public static class FormCenter
{
    /// <summary>
    /// Thu nhỏ cửa sổ về kích thước chỉ định và tự động căn giữa màn hình
    /// </summary>
    public static void SetSizeAndCenter(this Form form, int width, int height)
    {
        if (form == null) return;

        // 1. Đưa cửa sổ về trạng thái bình thường trước khi set kích thước
        form.WindowState = FormWindowState.Normal;

        // 2. Set kích thước
        form.Size = new Size(width, height);

        // 3. Đưa ra giữa màn hình
        form.StartPosition = FormStartPosition.CenterScreen;

        // (Tùy chọn) Force đưa cửa sổ ra giữa màn hình ngay lập tức, 
        // lấy theo đúng màn hình mà form đang hiển thị (phòng trường hợp dùng 2, 3 màn hình)
        var screen = Screen.FromControl(form);
        form.Location = new Point(
            screen.WorkingArea.X + (screen.WorkingArea.Width - form.Width) / 2,
            screen.WorkingArea.Y + (screen.WorkingArea.Height - form.Height) / 2
        );
    }

    public static void SetSizeByViewport(this Form form, double vw, double vh)
    {
        // 1. Lấy thông tin màn hình máy tính đang hiển thị phần mềm
        var screen = Screen.FromControl(form);
        int screenWidth = screen.WorkingArea.Width;
        int screenHeight = screen.WorkingArea.Height;

        // 2. Chuyển đổi vw và vh sang Pixel
        int targetWidth = (int)Math.Round(screenWidth * (vw / 100.0));
        int targetHeight = (int)Math.Round(screenHeight * (vh / 100.0));

        // 3. Trả về trạng thái bình thường (không phóng to)
        form.WindowState = FormWindowState.Normal;

        // 4. Thiết lập kích thước mới
        form.Size = new Size(targetWidth, targetHeight);

        // 5. Căn giữa màn hình
        form.Location = new Point(
            screen.WorkingArea.X + (screenWidth - form.Width) / 2,
            screen.WorkingArea.Y + (screenHeight - form.Height) / 2
        );
    }
}
