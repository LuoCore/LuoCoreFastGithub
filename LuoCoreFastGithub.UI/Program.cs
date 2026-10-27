using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace LuoCoreFastGithub.UI
{
    class Program
    {
        private const string MUTEX_NAME = "Global\\LuoCoreFastGithub.UI";
        private const string MAIN_WINDOWS = "MainWindow.xaml";
        private const string LuoCoreFastGithub_PATH = "LuoCoreFastGithub.exe";
        private const int FLOW_API_PORT_MIN = 38900;

        /// <summary>
        /// 与后端握手的流量统计api端口
        /// </summary>
        public static int FlowApiPort { get; private set; }

        [STAThread]
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            using var mutex = new Mutex(true, MUTEX_NAME, out var isFirstInstance);
            if (isFirstInstance == false)
            {
                return;
            }
            FlowApiPort = SelectFlowApiPort();
            StartLuoCoreFastGithub();
            SetWebBrowserDPI();
            SetWebBrowserVersion();
            var app = new Application();
            app.StartupUri = new Uri(MAIN_WINDOWS, UriKind.Relative);
            app.Run();
        }

        /// <summary>
        /// 程序集加载失败时
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private static Assembly? OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            if (name.EndsWith(".resources"))
            {
                return default;
            }

            var stream = Application.GetResourceStream(new Uri($"Resource/{name}.dll", UriKind.Relative)).Stream;
            var buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);
            return Assembly.Load(buffer);
        }

        /// <summary>
        /// 设置浏览器版本
        /// </summary>
        private static void SetWebBrowserVersion()
        {
            const string subKey = @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION";
            var registryKey = Registry.CurrentUser.OpenSubKey(subKey, true);
            if (registryKey == null)
            {
                registryKey = Registry.CurrentUser.CreateSubKey(subKey);
            }
            var name = $"{Process.GetCurrentProcess().ProcessName}.exe";
            using var webBrowser = new System.Windows.Forms.WebBrowser();
            var value = int.Parse($"{webBrowser.Version.Major}000");
            registryKey.SetValue(name, value, RegistryValueKind.DWord);
        }

        /// <summary>
        /// 设置浏览器DPI
        /// </summary>
        private static void SetWebBrowserDPI()
        {
            const string subKey = @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_96DPI_PIXEL";
            var registryKey = Registry.CurrentUser.OpenSubKey(subKey, true);
            if (registryKey == null)
            {
                registryKey = Registry.CurrentUser.CreateSubKey(subKey);
            }
            var name = $"{Process.GetCurrentProcess().ProcessName}.exe";
            registryKey.SetValue(name, 1, RegistryValueKind.DWord);
        }

        /// <summary>
        /// 启动LuoCoreFastGithub
        /// </summary>
        /// <returns></returns>
        private static void StartLuoCoreFastGithub()
        {
            if (File.Exists(LuoCoreFastGithub_PATH) == false)
            {
                return;
            }

            var flowApiArg = FlowApiPort > 0 ? $" --LuoCoreFastGithub:flowApiPort={FlowApiPort}" : string.Empty;
            var startInfo = new ProcessStartInfo
            {
                FileName = LuoCoreFastGithub_PATH,
                Arguments = $"--ParentProcessId={Process.GetCurrentProcess().Id} --UdpLoggerPort={UdpLogger.Port}{flowApiArg}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(startInfo);
        }

        /// <summary>
        /// 选择一个本机可用的tcp端口作为流量统计api端口
        /// </summary>
        /// <returns></returns>
        private static int SelectFlowApiPort()
        {
            for (var port = FLOW_API_PORT_MIN; port < FLOW_API_PORT_MIN + 100; port++)
            {
                try
                {
                    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                    listener.Start();
                    listener.Stop();
                    return port;
                }
                catch (System.Net.Sockets.SocketException)
                {
                }
            }
            return 0;
        }
    }
}
