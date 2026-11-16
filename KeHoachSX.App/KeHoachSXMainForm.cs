using Helpers;
using JtecShare.Core.Interfaces;
using JtecShare.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Services;
using KeHoachSX.Application.Abstractions.Runtime;
using KeHoachSX.Infrastructure;
using KeHoachSX.Application;
using KeHoachSX.UI;
using KeHoachSX.UI.Pages;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace KeHoachSX.App
{
    public partial class KeHoachSXMainForm : Form
    {
        private readonly CancellationTokenSource _appCts = new();
        private BlazorWebView blazorWebViewKeHoachSX;
        private ServiceProvider? _serviceProvider;
        public KeHoachSXMainForm()
        {

            blazorWebViewKeHoachSX = new BlazorWebView
            {
                Dock = DockStyle.Fill,
                Name = "blazorWebViewKeHoachSX"
            };
            this.Controls.Add(blazorWebViewKeHoachSX);

            this.SetSizeAndCenter(600, 1000);
            this.Text = "JTECSOFT KeHoachSX";

            try
            {
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "favicon.ico");
                if (File.Exists(iconPath))
                {
                    this.Icon = new Icon(iconPath);
                }
            }
            catch { }

            this.FormClosing += MainForm_FormClosing;
            this.FormClosed += (_, _) =>
            {
                _serviceProvider?.Dispose();
                _appCts.Dispose();
            };
            ConfigureWebView();
            ConfigureBlazorServices();
        }

        private void ConfigureWebView()
        {
            blazorWebViewKeHoachSX.BlazorWebViewInitializing += (sender, e) =>
            {
                e.EnvironmentOptions = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = "--allow-running-insecure-content"
                };
            };

            blazorWebViewKeHoachSX.BlazorWebViewInitialized += (sender, e) =>
            {
                e.WebView.CoreWebView2.ServerCertificateErrorDetected += (certSender, certArgs) =>
                {
                    try
                    {
                        var uri = certArgs.RequestUri;
                        bool isImageServer = uri.StartsWith("https://192.168.160.30:222/", StringComparison.OrdinalIgnoreCase);

                        certArgs.Action = isImageServer
                            ? CoreWebView2ServerCertificateErrorAction.AlwaysAllow
                            : CoreWebView2ServerCertificateErrorAction.Cancel;
                    }
                    catch
                    {
                        certArgs.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
                    }
                };
            };
        }

        private void ConfigureBlazorServices()
        {
            var services = new ServiceCollection();

            services.AddWindowsFormsBlazorWebView();
#if DEBUG
            services.AddBlazorWebViewDeveloperTools();
#endif
            services.AddLocalization();

            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AppConfig:DefaultHomeRoute"] = "keHoachSX",
                    ["AppConfig:AppName"] = "JTECSOFT KeHoachSX"
                })
                .AddEnvironmentVariables()
                .Build();

            services.AddSingleton<IConfiguration>(configuration);

            // 1. Đăng ký các dịch vụ dùng chung của JtecShare (Login, UserState, ConnectionFactory, DB...)
            new SharedRegisterServices().RegisterServices(services, configuration);

            services.AddScoped<KeHoachSX_View>();

            // 3. Đăng ký dịch vụ tương tác nền tảng Windows Forms
            services.AddSingleton<Form>(this);
            //services.AddSingleton<IPlatformService>(new DesktopPlatformService(this, blazorWebViewStamping));
            //services.AddSingleton<IWindowService>(new DesktopWindowService(this));
            //services.AddKeHoachSXApplication();
            //services.AddKeHoachSXInfrastructure();

            _serviceProvider = services.BuildServiceProvider();

            // Cấu hình BlazorWebView
            blazorWebViewKeHoachSX.HostPage = "wwwroot\\index.html";
            blazorWebViewKeHoachSX.Services = _serviceProvider;
            //blazorWebViewKeHoachSX.RootComponents.Add<StampingApp>("#app");
            blazorWebViewKeHoachSX.RootComponents.Add<KeHoachSX.App.Views.App>("#app");
            blazorWebViewKeHoachSX.RootComponents.Add<HeadOutlet>("head::after");

            string defaultRoute = configuration["AppConfig:DefaultHomeRoute"] ?? "keHoachsx";

            // 2. Ép BlazorWebView mở trang gốc (Login) kèm theo biến returnUrl
            blazorWebViewKeHoachSX.StartPath = $"/?returnUrl={Uri.EscapeDataString(defaultRoute)}";
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!e.Cancel)
            {
                _appCts.Cancel();

            }
        }
    }
}
