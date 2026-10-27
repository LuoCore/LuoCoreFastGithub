using Microsoft.Extensions.Logging;
using System;
using System.Runtime.Versioning;

namespace LuoCoreFastGithub.PacketIntercept
{
    /// <summary>
    /// 包拦截能力探测
    /// 只试探一次并缓存结果：WinDivert驱动在部分新版Windows(HVCI/Smart App Control)上无法加载，
    /// 探测失败时整个包拦截层跳过，由主程序降级为本地代理模式
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class PacketInterceptCapability
    {
        private readonly ILogger<PacketInterceptCapability> logger;
        private readonly object syncRoot = new();
        private bool? available;

        /// <summary>
        /// 包拦截能力探测
        /// </summary>
        /// <param name="logger"></param>
        public PacketInterceptCapability(ILogger<PacketInterceptCapability> logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// 内核驱动是否可用
        /// </summary>
        public bool IsAvailable
        {
            get
            {
                if (this.available.HasValue)
                {
                    return this.available.Value;
                }
                lock (this.syncRoot)
                {
                    this.available ??= this.Probe();
                    return this.available.Value;
                }
            }
        }

        /// <summary>
        /// 试探性打开WinDivert句柄
        /// </summary>
        /// <returns></returns>
        private bool Probe()
        {
            try
            {
                using var divert = new WindivertDotnet.WinDivert("udp.DstPort == 65533", WindivertDotnet.WinDivertLayer.Network);
                this.logger.LogInformation("WinDivert内核驱动可用，启用透明包拦截模式");
                return true;
            }
            catch (Exception ex)
            {
                this.logger.LogWarning($"WinDivert内核驱动不可用({ex.Message})，将降级为本地代理模式（无需内核驱动）");
                return false;
            }
        }
    }
}
