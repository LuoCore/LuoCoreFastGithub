using System;
using System.Collections.Generic;
using System.Net;

namespace LuoCoreFastGithub.Configuration
{
    /// <summary>
    /// LuoCoreFastGithub的配置
    /// </summary>
    public class LuoCoreFastGithubOptions
    {
        /// <summary>
        /// http代理端口
        /// </summary>
        public int HttpProxyPort { get; set; } = 38457;

        /// <summary>
        /// 回退的dns
        /// </summary>
        public IPEndPoint[] FallbackDns { get; set; } = Array.Empty<IPEndPoint>();

        /// <summary>
        /// 代理的域名配置
        /// </summary>
        public Dictionary<string, DomainConfig> DomainConfigs { get; set; } = new();
    }
}
