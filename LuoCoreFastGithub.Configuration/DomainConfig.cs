using System;
using System.Net;

namespace LuoCoreFastGithub.Configuration
{
    /// <summary>
    /// 域名配置
    /// </summary>
    public record DomainConfig
    {
        /// <summary>
        /// 是否发送SNI
        /// </summary>
        public bool TlsSni { get; init; }

        /// <summary>
        /// 自定义SNI值的表达式
        /// </summary>
        public string? TlsSniPattern { get; init; }

        /// <summary>
        /// 是否忽略服务器证书域名不匹配
        /// 当不发送SNI时服务器可能发回域名不匹配的证书
        /// </summary>
        public bool TlsIgnoreNameMismatch { get; init; }

        /// <summary>
        /// 使用的ip地址
        /// </summary>
        public IPAddress? IPAddress { get; init; }

        /// <summary>
        /// 请求超时时长
        /// </summary>
        public TimeSpan? Timeout { get; init; }

        /// <summary>
        /// 目的地
        /// 格式为相对或绝对uri
        /// </summary>
        public Uri? Destination { get; init; }

        /// <summary>
        /// 自定义响应
        /// </summary>
        public ResponseConfig? Response { get; init; }

        /// <summary>
        /// 获取TlsSniPattern
        /// </summary>
        /// <returns></returns>
        public TlsSniPattern GetTlsSniPattern()
        {
            if (this.TlsSni == false)
            {
                return Configuration.TlsSniPattern.None;
            }
            if (string.IsNullOrEmpty(this.TlsSniPattern))
            {
                return Configuration.TlsSniPattern.Domain;
            }
            return new TlsSniPattern(this.TlsSniPattern);
        }

        /// <summary>
        /// 获取tls握手的候选SNI模式
        /// 优先使用域名配置指定的模式；
        /// 当配置为无SNI时，追加伪装域名模式作为容错，
        /// 以适配拒绝空SNI、但接受其它SNI的cdn边缘节点
        /// </summary>
        /// <param name="host">目标域名</param>
        /// <returns></returns>
        public TlsSniPattern[] GetHandshakeSniPatterns(string host)
        {
            var primary = this.GetTlsSniPattern().WithDomain(host).WithRandom();
            if (primary.Value.Length > 0)
            {
                return new[] { primary };
            }

            var decoy = new TlsSniPattern(Configuration.TlsSniPattern.Random.Value).WithRandom();
            return new[] { primary, decoy };
        }
    }

}
