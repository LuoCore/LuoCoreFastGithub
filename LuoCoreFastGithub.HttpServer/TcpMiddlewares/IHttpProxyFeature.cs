using Microsoft.AspNetCore.Http;

namespace LuoCoreFastGithub.HttpServer.TcpMiddlewares
{
    interface IHttpProxyFeature
    {
        HostString ProxyHost { get; }

        ProxyProtocol ProxyProtocol { get; }
    }
}
