using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace LuoCoreFastGithub.PacketIntercept
{
    /// <summary>
    /// dns拦截后台服务
    /// </summary>
    [SupportedOSPlatform("windows")]
    sealed class DnsInterceptHostedService : BackgroundService
    {
        private readonly IDnsInterceptor dnsInterceptor;
        private readonly IEnumerable<IDnsConflictSolver> conflictSolvers;
        private readonly PacketInterceptCapability capability;
        private readonly ILogger<DnsInterceptHostedService> logger;
        private readonly IHost host;
        private bool interceptActivated;

        /// <summary>
        /// dns拦截后台服务
        /// </summary>
        /// <param name="dnsInterceptor"></param>
        /// <param name="conflictSolvers"></param>
        /// <param name="capability"></param>
        /// <param name="logger"></param>
        /// <param name="host"></param>
        public DnsInterceptHostedService(
            IDnsInterceptor dnsInterceptor,
            IEnumerable<IDnsConflictSolver> conflictSolvers,
            PacketInterceptCapability capability,
            ILogger<DnsInterceptHostedService> logger,
            IHost host)
        {
            this.dnsInterceptor = dnsInterceptor;
            this.conflictSolvers = conflictSolvers;
            this.capability = capability;
            this.logger = logger;
            this.host = host;
        }

        /// <summary>
        /// 启动时处理冲突
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            // 驱动不可用时整层跳过：既避免冲突处理改写系统代理直连规则，
            // 也避免拦截循环反复崩溃，由主程序降级为本地代理模式
            if (this.capability.IsAvailable == false)
            {
                return;
            }

            this.interceptActivated = true;
            foreach (var solver in this.conflictSolvers)
            {
                await solver.SolveAsync(cancellationToken);
            }
            await base.StartAsync(cancellationToken);
        }

        /// <summary>
        /// 停止时恢复冲突
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (this.interceptActivated == false)
            {
                return;
            }
            this.interceptActivated = false;
            foreach (var solver in this.conflictSolvers)
            {
                await solver.RestoreAsync(cancellationToken);
            }
            await base.StopAsync(cancellationToken);
        }

        /// <summary>
        /// dns后台
        /// </summary>
        /// <param name="stoppingToken"></param>
        /// <returns></returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await this.dnsInterceptor.InterceptAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 995)
            {
            }
            catch (Exception ex)
            { 
                this.logger.LogError(ex, "dns拦截器异常");
                await this.host.StopAsync(stoppingToken);
            }
        }
    }
}
