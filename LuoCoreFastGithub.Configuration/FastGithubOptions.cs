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
        /// 流量统计api端口
        /// 由UI进程通过命令行传入；为0或缺失时不启用
        /// </summary>
        public int FlowApiPort { get; set; }

        /// <summary>
        /// 是否启用外置dnscrypt-proxy进程作为解析上游
        /// 默认关闭，使用内置DoH解析链路
        /// </summary>
        public bool EnableDnscrypt { get; set; }

        /// <summary>
        /// 内置DoH解析服务地址
        /// 要求支持application/dns-json的GET查询
        /// </summary>
        public string[] DohEndpoints { get; set; } = new string[]
        {
            "https://sm2.doh.pub/dns-query",
            "https://dns.alidns.com/resolve",
            "https://dns.google/resolve",
            "https://cloudflare-dns.com/dns-query"
        };

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
