using Helpers;
using Interfaces;
using JtecShare.Core.Interfaces;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Services;

public class DesktopPlatformService : IPlatformService
{
    private readonly Form _mainForm;
    private readonly BlazorWebView _webView;
    public DesktopPlatformService(Form mainForm, BlazorWebView webView)
    {
        _mainForm = mainForm;
        _webView = webView;
    }

    public Task LaunchApplicationAsync(string exePath)
    {
        try
        {
            if (File.Exists(exePath))
            {
                // TỐI ƯU: Bổ sung WorkingDirectory để ứng dụng con không bị lỗi thiếu file tham chiếu
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            else if (_mainForm != null && !_mainForm.IsDisposed)
            {
                _mainForm.Invoke(new Action(() =>
                {
                    MessageBox.Show(_mainForm, $"Không tìm thấy phần mềm tại đường dẫn:\n{exePath}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LỖI] Không thể mở ứng dụng: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    public Task<string> ExtractIconBase64Async(string filePath)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath)) return string.Empty;
                using var icon = Icon.ExtractAssociatedIcon(filePath);
                if (icon != null)
                {
                    using var bitmap = icon.ToBitmap();
                    using var ms = new MemoryStream();
                    bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    return $"data:image/png;base64,{Convert.ToBase64String(ms.ToArray())}";
                }
            }
            catch { }
            return string.Empty;
        });
    }

    public string GetLocalDatabasePath() => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "localdb.sqlite");

    public void OpenFolder(string path) => OpenExternalUrl(path);

    public void OpenLocalFolder(string folderPath) => OpenExternalUrl(folderPath);

    public void OpenExternalUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
    }

    public bool MapVirtualHostFolder(string hostName, string folderPath)
    {
        try
        {
            if (_webView?.WebView.CoreWebView2 != null)
            {
                _webView.WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    hostName, folderPath, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                return true;
            }
        }
        catch { }
        return false;
    }

    public void SetSizeAndCenter(int width, int height)
    {
        if (_mainForm == null || _mainForm.IsDisposed) return;
        _mainForm.Invoke(new Action(() =>
        {
            _mainForm.WindowState = FormWindowState.Normal;
            _mainForm.Size = new Size(width, height);
            _mainForm.StartPosition = FormStartPosition.CenterScreen;
        }));
    }

    public void MaximizeWindow()
    {
        if (_mainForm == null || _mainForm.IsDisposed) return;
        _mainForm.Invoke(new Action(() => _mainForm.WindowState = FormWindowState.Maximized));
    }

    public string GetDatabaseConnectionString() => "Data Source=production.db";

    public Task<string> SelectFileAsync(string filter, string title)
    {
        var tcs = new TaskCompletionSource<string>();
        // TỐI ƯU: Đẩy việc mở Dialog về luồng chính (Main Thread) thay vì cấp phát Thread riêng
        _mainForm.Invoke((MethodInvoker)delegate
        {
            using var ofd = new OpenFileDialog { Filter = filter, Title = title, CheckFileExists = true };
            tcs.SetResult(ofd.ShowDialog(_mainForm) == DialogResult.OK ? ofd.FileName : string.Empty);
        });
        return tcs.Task;
    }

    public async Task CleanupOnLogoutAsync()
    {
        await Task.Run(() => { try { CastIniHelper.RemoveUserInfoSync(); } catch { } });
        SetSizeAndCenter(600, 1000);
    }

    public string GetImageUrl(string imagePath, string maSanPham)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return string.Empty;
        return $"https://jtec-images/{imagePath.Replace("\\", "/")}";
    }

    public Task ExportFileAsync(string fileName, byte[] fileData, string contentType)
    {
        var tcs = new TaskCompletionSource<bool>();
        // TỐI ƯU: Bỏ Thread thừa, dùng Invoke đồng bộ
        _mainForm.Invoke((MethodInvoker)delegate
        {
            try
            {
                using var sfd = new SaveFileDialog
                {
                    FileName = fileName,
                    Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                    DefaultExt = "csv",
                    AddExtension = true,
                    OverwritePrompt = true,
                    Title = "Lưu file dữ liệu",
                    RestoreDirectory = true
                };

                if (sfd.ShowDialog(_mainForm) == DialogResult.OK)
                {
                    File.WriteAllBytes(sfd.FileName, fileData);
                    MessageBox.Show(_mainForm, $"Xuất dữ liệu thành công!\n\n{sfd.FileName}", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(_mainForm, $"Lỗi lưu file:\n{ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }
}