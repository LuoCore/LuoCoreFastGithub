using LuoCoreFastGithub.HttpServer.Certs;
using LuoCoreFastGithub.HttpServer.Certs.CaCertInstallers;
using LuoCoreFastGithub.HttpServer.HttpMiddlewares;
using LuoCoreFastGithub.HttpServer.TcpMiddlewares;
using LuoCoreFastGithub.HttpServer.TlsMiddlewares;
using Microsoft.Extensions.DependencyInjection;
namespace LuoCoreFastGithub
{
    /// <summary>
    /// http反向代理的服务注册扩展
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加http反向代理
        /// </summary>
        /// <param name="services"></param> 
        /// <returns></returns>
        public static IServiceCollection AddReverseProxy(this IServiceCollection services)
        {
            return services
                .AddMemoryCache()
                .AddHttpForwarder()
                .AddSingleton<CertService>()
                .AddSingleton<ICaCertInstaller, CaCertInstallerOfMacOS>()
                .AddSingleton<ICaCertInstaller, CaCertInstallerOfWindows>()
                .AddSingleton<ICaCertInstaller, CaCertInstallerOfLinuxRedHat>()
                .AddSingleton<ICaCertInstaller, CaCertInstallerOfLinuxDebian>()

                // tcp
                .AddSingleton<HttpProxyMiddleware>()
                .AddSingleton<TunnelMiddleware>()

                // tls
                .AddSingleton<TlsInvadeMiddleware>()
                .AddSingleton<TlsRestoreMiddleware>()

                // http
                .AddSingleton<HttpProxyPacMiddleware>()
                .AddSingleton<RequestLoggingMiddleware>()
                .AddSingleton<HttpReverseProxyMiddleware>();
        }
    }
}
