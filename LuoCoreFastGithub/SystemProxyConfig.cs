using LuoCoreFastGithub.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LuoCoreFastGithub
{
    /// <summary>
    /// Windows系统代理配置器
    /// 降级模式：把WinINET系统代理指向本地转发代理端口(浏览器/Edge/大部分应用自动走代理)，
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
        private readonly ILogger<SystemProxyConfig> logger;

        private string? savedProxyEnable;
        private string? savedProxyServer;
        private string? savedProxyOverride;
        private bool applied;

        /// <summary>
        /// 系统代理配置器
        /// </summary>
        /// <param name="options"></param>
        /// <param name="logger"></param>
        public SystemProxyConfig(IOptions<LuoCoreFastGithubOptions> options, ILogger<SystemProxyConfig> logger)
        {
            this.options = options;
            this.logger = logger;
        }

        /// <summary>
        /// 是否已接管系统代理
        /// </summary>
        public bool IsApplied => this.applied;

        /// <summary>
        /// 把系统代理指向本地代理端口
        /// </summary>
        /// <returns></returns>
        public bool Apply()
        {
            var httpProxyPort = this.options.Value.HttpProxyPort;
            try
            {
                using var settings = Registry.CurrentUser.OpenSubKey(INTERNET_SETTINGS, writable: true);
                if (settings == null)
                {
                    return false;
                }

                // 记录原始状态用于退出时还原
                this.savedProxyEnable = settings.GetValue("ProxyEnable")?.ToString();
                this.savedProxyServer = settings.GetValue("ProxyServer")?.ToString();
                this.savedProxyOverride = settings.GetValue("ProxyOverride")?.ToString();

                settings.SetValue("ProxyServer", $"127.0.0.1:{httpProxyPort}", RegistryValueKind.String);

                // 旁路列表中不能有加速域名(旧版冲突处理可能添加过)，否则浏览器会绕过代理直连
                settings.SetValue("ProxyOverride", CleanOverride(this.savedProxyOverride), RegistryValueKind.String);
                settings.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);

                NotifySettingsChanged();
                this.applied = true;
                this.logger.LogInformation($"已设置系统代理 http://127.0.0.1:{httpProxyPort}（退出后自动还原）");
                return true;
            }
            catch (Exception ex)
            {
                this.logger.LogWarning($"设置系统代理失败：{ex.Message}，请手动设置代理 127.0.0.1:{httpProxyPort}");
                return false;
            }

            static string CleanOverride(string? value)
            {
                var bypassHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var items = (value ?? string.Empty)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0)
                    .ToList();
                if (items.Any(item => item.Equals("<-loopback>", StringComparison.OrdinalIgnoreCase)) == false)
                {
                    items.Add("<-loopback>");
                }
                return string.Join(";", items);
            }
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
                var enable = settings.GetValue("ProxyEnable")?.ToString();
                if (proxyServer == null || proxyServer.Contains($"127.0.0.1:{httpProxyPort}") == false)
                {
                    return false;
                }

                if (enable == "1")
                {
                    settings.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    NotifySettingsChanged();
                    this.logger.LogInformation("内核拦截模式已启用，清除降级模式设置的系统代理");
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
