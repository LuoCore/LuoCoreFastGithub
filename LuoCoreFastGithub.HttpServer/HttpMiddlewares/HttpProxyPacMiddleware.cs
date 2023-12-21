using LuoCoreFastGithub.Configuration;
using LuoCoreFastGithub.HttpServer.TcpMiddlewares;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LuoCoreFastGithub.HttpServer.HttpMiddlewares
{
    /// <summary>
    /// http代理策略中间件
    /// </summary>
    sealed class HttpProxyPacMiddleware
    {
        private readonly LuoCoreFastGithubConfig LuoCoreFastGithubConfig;

        /// <summary>
        /// http代理策略中间件
        /// </summary>
        /// <param name="LuoCoreFastGithubConfig"></param> 
        public HttpProxyPacMiddleware(LuoCoreFastGithubConfig LuoCoreFastGithubConfig)
        {
            this.LuoCoreFastGithubConfig = LuoCoreFastGithubConfig;
        }

        /// <summary>
        /// 处理请求
        /// </summary>
        /// <param name="context"></param>
        /// <param name="next"></param>
        /// <returns></returns>
        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            // http请求经过了httpProxy中间件
            var proxyFeature = context.Features.Get<IHttpProxyFeature>();
            if (proxyFeature != null && proxyFeature.ProxyProtocol == ProxyProtocol.None)
            {
                var proxyPac = this.CreateProxyPac(context.Request.Host);
                context.Response.ContentType = "application/x-ns-proxy-autoconfig";
                context.Response.Headers.Append("Content-Disposition", $"attachment;filename=proxy.pac");
                await context.Response.WriteAsync(proxyPac);
            }
            else
            {
                await next(context);
            }
        }

        /// <summary>
        /// 创建proxypac脚本
        /// </summary>
        /// <param name="proxyHost"></param>
        /// <returns></returns>
        private string CreateProxyPac(HostString proxyHost)
        {
            var buidler = new StringBuilder();
            buidler.AppendLine("function FindProxyForURL(url, host){");
            buidler.AppendLine($"    var LuoCoreFastGithub = 'PROXY {proxyHost}';");
            foreach (var domain in LuoCoreFastGithubConfig.GetDomainPatterns())
            {
                buidler.AppendLine($"    if (shExpMatch(host, '{domain}')) return LuoCoreFastGithub;");
            }
            buidler.AppendLine("    return 'DIRECT';");
            buidler.AppendLine("}");
            return buidler.ToString();
        }
    }
}