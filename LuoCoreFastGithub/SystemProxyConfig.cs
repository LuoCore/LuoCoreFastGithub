using LuoCoreFastGithub.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace LuoCoreFastGithub
{
    /// <summary>
    /// Windows系统代理配置器
    /// 白名单模式：生成本地proxy.pac并接管自动配置脚本，仅加速域名走本地代理，其余流量直连；
    /// 程序退出时还原原始代理设置
    /// </summary>
    [SupportedOSPlatform("windows")]
    sealed class SystemProxyConfig
    {
        private const string INTERNET_SETTINGS = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        [DllImport("wininet.dll")]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private readonly IOptions<LuoCoreFastGithubOptions> options;
        private readonly LuoCoreFastGithubConfig fastGithubConfig;
        private readonly ILogger<SystemProxyConfig> logger;

        private string? savedProxyEnable;
        private string? savedProxyServer;
        private string? savedProxyOverride;
        private string? savedAutoConfigUrl;
        private bool applied;

        /// <summary>
        /// 系统代理配置器
        /// </summary>
        /// <param name="options"></param>
        /// <param name="fastGithubConfig"></param>
        /// <param name="logger"></param>
        public SystemProxyConfig(
            IOptions<LuoCoreFastGithubOptions> options,
            LuoCoreFastGithubConfig fastGithubConfig,
            ILogger<SystemProxyConfig> logger)
        {
            this.options = options;
            this.fastGithubConfig = fastGithubConfig;
            this.logger = logger;
        }

        /// <summary>
        /// 是否已接管系统代理
        /// </summary>
        public bool IsApplied => this.applied;

        /// <summary>
        /// 以proxy.pac白名单接管系统代理
        /// </summary>
        /// <returns></returns>
        public bool Apply()
        {
            var httpProxyPort = this.options.Value.HttpProxyPort;
            try
            {
                var pacFile = Path.Combine(AppContext.BaseDirectory, "proxy.pac");
                File.WriteAllText(pacFile, this.BuildPac(httpProxyPort), new UTF8Encoding(false));

                using var settings = Registry.CurrentUser.OpenSubKey(INTERNET_SETTINGS, writable: true);
                if (settings == null)
                {
                    return false;
                }

                // 记录原始状态用于退出时还原
                this.savedProxyEnable = settings.GetValue("ProxyEnable")?.ToString();
                this.savedProxyServer = settings.GetValue("ProxyServer")?.ToString();
                this.savedProxyOverride = settings.GetValue("ProxyOverride")?.ToString();
                this.savedAutoConfigUrl = settings.GetValue("AutoConfigURL")?.ToString();

                settings.SetValue("AutoConfigURL", $"file:///{pacFile.Replace('\\', '/')}", RegistryValueKind.String);
                settings.SetValue("ProxyServer", $"127.0.0.1:{httpProxyPort}", RegistryValueKind.String);
                settings.SetValue("ProxyOverride", "<-loopback>", RegistryValueKind.String);
                settings.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);

                NotifySettingsChanged();
                this.applied = true;
                this.logger.LogInformation($"已设置系统代理白名单模式(仅加速域名走http://127.0.0.1:{httpProxyPort}，其余直连；退出后自动还原)");
                return true;
            }
            catch (Exception ex)
            {
                this.logger.LogWarning($"设置系统代理失败：{ex.Message}，请手动设置自动代理脚本指向本程序目录的proxy.pac");
                return false;
            }
        }


        /// <summary>
        /// 生成白名单pac脚本：加速域名走代理，其余直连
        /// 域名表达式与转发层同源(DomainPattern)
        /// </summary>
        /// <param name="httpProxyPort"></param>
        /// <returns></returns>
        private string BuildPac(int httpProxyPort)
        {
            var shExpPatterns = new List<string>();
            foreach (var pattern in this.fastGithubConfig.GetDomainPatterns())
            {
                var text = pattern.ToString();
                shExpPatterns.Add(text);
                if (text.StartsWith("*."))
                {
                    shExpPatterns.Add(text[2..]);
                }
            }
            shExpPatterns.Sort(StringComparer.OrdinalIgnoreCase);

            var builder = new StringBuilder();
            builder.AppendLine("function FindProxyForURL(url, host) {");
            builder.AppendLine($"    var proxy = 'PROXY 127.0.0.1:{httpProxyPort}';");
            builder.AppendLine("    host = host.toLowerCase();");
            foreach (var item in shExpPatterns.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"    if (shExpMatch(host, '{item}')) return proxy;");
            }
            builder.AppendLine("    return 'DIRECT';");
            builder.AppendLine("}");
            return builder.ToString();
        }

        /// <summary>
        /// 还原原始系统代理
        /// </summary>
        public void Restore()
        {
            if (this.applied == false)
            {
                return;
            }
            this.applied = false;

            try
            {
                using var settings = Registry.CurrentUser.OpenSubKey(INTERNET_SETTINGS, writable: true);
                if (settings == null)
                {
                    return;
                }

                settings.SetValue("ProxyServer", this.savedProxyServer ?? string.Empty, RegistryValueKind.String);
                settings.SetValue("ProxyOverride", this.savedProxyOverride ?? string.Empty, RegistryValueKind.String);
                if (string.IsNullOrEmpty(this.savedAutoConfigUrl))
                {
                    settings.DeleteValue("AutoConfigURL", false);
                }
                else
                {
                    settings.SetValue("AutoConfigURL", this.savedAutoConfigUrl, RegistryValueKind.String);
                }
                if (int.TryParse(this.savedProxyEnable, out var enable))
                {
                    settings.SetValue("ProxyEnable", enable, RegistryValueKind.DWord);
                }
                else
                {
                    settings.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                }

                NotifySettingsChanged();
                this.logger.LogInformation("系统代理已还原为程序启动前的设置");
            }
            catch (Exception ex)
            {
                this.logger.LogWarning($"还原系统代理失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 若当前系统代理正指向本软件的转发端口则清除它
        /// 用于驱动恢复可用(切回透明模式)时清理降级模式留下的代理设置，避免代理与DNS毒化互相干扰
        /// </summary>
        /// <returns></returns>
        public bool ClearIfOurs()
        {
            var httpProxyPort = this.options.Value.HttpProxyPort;
            try
            {
                using var settings = Registry.CurrentUser.OpenSubKey(INTERNET_SETTINGS, writable: true);
                if (settings == null)
                {
                    return false;
                }

                var proxyServer = settings.GetValue("ProxyServer")?.ToString();
                var autoConfigUrl = settings.GetValue("AutoConfigURL")?.ToString();
                var enable = settings.GetValue("ProxyEnable")?.ToString();
                var isOurs = (proxyServer != null && proxyServer.Contains($"127.0.0.1:{httpProxyPort}")) ||
                    (autoConfigUrl != null && autoConfigUrl.EndsWith("proxy.pac") && File.Exists(Path.Combine(AppContext.BaseDirectory, "proxy.pac")));
                if (isOurs == false)
                {
                    return false;
                }

                if (enable == "1")
                {
                    settings.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    NotifySettingsChanged();
                    this.logger.LogInformation("内核拦截模式已启用，清除白名单模式设置的系统代理");
                }
                return true;
            }
            catch (Exception ex)
            {
                this.logger.LogWarning($"清除系统代理失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 通知wininet代理设置变更
        /// </summary>
        private static void NotifySettingsChanged()
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
    }
}
