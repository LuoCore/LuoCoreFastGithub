using System;
using System.Net;

namespace LuoCoreFastGithub.Configuration
{
    /// <summary>
    /// Sni自定义值表达式
    /// @domain变量表示取域名值
    /// @ipadress变量表示取ip
    /// @random变量表示取随机值
    /// </summary> 
    public struct TlsSniPattern
    {
        /// <summary>
        /// 获取表示式值
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// 无SNI
        /// </summary>
        public static TlsSniPattern None { get; } = new TlsSniPattern(string.Empty);

        /// <summary>
        /// 域名SNI
        /// </summary>
        public static TlsSniPattern Domain { get; } = new TlsSniPattern("@domain");

        /// <summary>
        /// IP值的SNI
        /// </summary>
        public static TlsSniPattern IPAddress { get; } = new TlsSniPattern("@ipaddress");

        /// <summary>
        /// 随机值的SNI
        /// </summary>
        public static TlsSniPattern Random { get; } = new TlsSniPattern("@random");

        /// <summary>
        /// 伪装SNI使用的域名池
        /// 选取公网无害域名，避免数字串等畸形SNI被cdn边缘节点直接拒绝
        /// </summary>
        private static readonly string[] decoyDomains = new string[]
        {
            "www.microsoft.com",
            "clients2.google.com",
            "update.code.visualstudio.com",
            "www.bing.com",
            "detectportal.firefox.com",
            "office.microsoft.com"
        };

        /// <summary>
        /// Sni自定义值表达式
        /// </summary>
        /// <param name="value">表示式值</param>
        public TlsSniPattern(string? value)
        {
            this.Value = value ?? string.Empty;
        }

        /// <summary>
        /// 更新域名
        /// </summary>
        /// <param name="domain"></param>
        public TlsSniPattern WithDomain(string domain)
        {
            var value = this.Value.Replace(Domain.Value, domain, StringComparison.OrdinalIgnoreCase);
            return new TlsSniPattern(value);
        }

        /// <summary>
        /// 更新ip地址
        /// </summary>
        /// <param name="address"></param>
        public TlsSniPattern WithIPAddress(IPAddress address)
        {
            var value = this.Value.Replace(IPAddress.Value, address.ToString(), StringComparison.OrdinalIgnoreCase);
            return new TlsSniPattern(value);
        }

        /// <summary>
        /// 更新随机数
        /// 从伪装域名池中随机选取一个域名作为SNI值
        /// </summary>
        public TlsSniPattern WithRandom()
        {
            var decoy = decoyDomains[System.Random.Shared.Next(decoyDomains.Length)];
            var value = this.Value.Replace(Random.Value, decoy, StringComparison.OrdinalIgnoreCase);
            return new TlsSniPattern(value);
        }

        /// <summary>
        /// 转换为文本
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return this.Value;
        }
    }
}
