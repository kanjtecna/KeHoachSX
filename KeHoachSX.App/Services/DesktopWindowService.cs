using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using JtecShare.Core.Interfaces;

namespace Services
{
    public class DesktopWindowService : IWindowService
    {
        private readonly Form _mainForm;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        private DateTime _lastClickTime = DateTime.MinValue;

        public DesktopWindowService(Form mainForm)
        {
            _mainForm = mainForm;
        }

        public void HandleHeaderMouseDown()
        {
            if (!OperatingSystem.IsWindows() || _mainForm == null) return;

            var timeSinceLastClick = (DateTime.Now - _lastClickTime).TotalMilliseconds;
            _lastClickTime = DateTime.Now;

            if (timeSinceLastClick < 500)
            {
                if (_mainForm.WindowState == FormWindowState.Maximized)
                {
                    _mainForm.WindowState = FormWindowState.Normal;
                }
                else
                {
                    _mainForm.WindowState = FormWindowState.Maximized;
                }
            }
            else
            {
                ReleaseCapture();
                SendMessage(_mainForm.Handle, 0xA1, 0x2, 0);
            }
        }

    }
}