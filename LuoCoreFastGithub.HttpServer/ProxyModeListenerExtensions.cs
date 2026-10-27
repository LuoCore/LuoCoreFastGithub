using LuoCoreFastGithub.Configuration;
using LuoCoreFastGithub.HttpServer.TcpMiddlewares;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LuoCoreFastGithub.HttpServer
{
    /// <summary>
    /// 转发代理监听扩展(降级模式用)
    /// 端口被占用时跳过监听并告警，而不是像ListenHttpProxy那样抛异常终止进程
    /// </summary>
    public static class ProxyModeListenerExtensions
    {
        /// <summary>
        /// 尝试监听http转发代理
        /// </summary>
        /// <param name="kestrel"></param>
        public static void ListenHttpProxyIfAvailable(this KestrelServerOptions kestrel)
        {
            var services = kestrel.ApplicationServices;
            var httpProxyPort = services.GetRequiredService<IOptions<LuoCoreFastGithubOptions>>().Value.HttpProxyPort;

            if (GlobalListener.CanListenTcp(httpProxyPort) == false)
            {
                services
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger($"{nameof(LuoCoreFastGithub)}.{nameof(HttpServer)}")
                    .LogWarning($"tcp端口{httpProxyPort}被占用，转发代理未监听，请修改配置{nameof(LuoCoreFastGithubOptions.HttpProxyPort)}");
                return;
            }

            var proxyMiddleware = services.GetRequiredService<HttpProxyMiddleware>();
            var tunnelMiddleware = services.GetRequiredService<TunnelMiddleware>();

            kestrel.ListenLocalhost(httpProxyPort, listen =>
            {
                // 降级模式下转发代理是唯一流量入口，需在此挂流量计数
                listen.UseFlowAnalyze();
                listen.Use(next => context => proxyMiddleware.InvokeAsync(next, context));
                listen.UseTls();
                listen.Use(next => context => tunnelMiddleware.InvokeAsync(next, context));
            });

            services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger($"{nameof(LuoCoreFastGithub)}.{nameof(HttpServer)}")
                .LogInformation($"已监听http://127.0.0.1:{httpProxyPort}，转发代理服务启动完成");
        }
    }
}
