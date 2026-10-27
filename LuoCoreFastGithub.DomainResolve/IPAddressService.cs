using LuoCoreFastGithub.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace LuoCoreFastGithub.DomainResolve
{
    /// <summary>
    /// IP服务
    /// 域名IP关系缓存10分钟
    /// IPEndPoint时延缓存5分钟
    /// IPEndPoint连接超时5秒
    /// 对https目标的探测与真实转发一致：完成tls握手(按域名配置的SNI策略)，
    /// 排除"tcp可连但tls即断"的节点，避免劣化ip被优选
    /// </summary>
    sealed class IPAddressService
    {
        private const int HTTPS_PORT = 443;

        private record DomainAddress(string Domain, IPAddress Address);
        private readonly TimeSpan domainAddressExpiration = TimeSpan.FromMinutes(10d);
        private readonly IMemoryCache domainAddressCache = new MemoryCache(Options.Create(new MemoryCacheOptions()));

        private record AddressElapsed(IPAddress Address, TimeSpan Elapsed);
        private readonly TimeSpan problemElapsedExpiration = TimeSpan.FromMinutes(1d);
        private readonly TimeSpan normalElapsedExpiration = TimeSpan.FromMinutes(5d);
        private readonly TimeSpan connectTimeout = TimeSpan.FromSeconds(5d);
        private readonly IMemoryCache addressElapsedCache = new MemoryCache(Options.Create(new MemoryCacheOptions()));

        private readonly DnsClient dnsClient;
        private readonly LuoCoreFastGithubConfig fastGithubConfig;

        /// <summary>
        /// IP服务
        /// </summary>
        /// <param name="dnsClient"></param>
        /// <param name="fastGithubConfig"></param>
        public IPAddressService(DnsClient dnsClient, LuoCoreFastGithubConfig fastGithubConfig)
        {
            this.dnsClient = dnsClient;
            this.fastGithubConfig = fastGithubConfig;
        }

        /// <summary>
        /// 并行获取可连接的IP
        /// </summary>
        /// <param name="dnsEndPoint"></param>
        /// <param name="oldAddresses"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<IPAddress[]> GetAddressesAsync(DnsEndPoint dnsEndPoint, IEnumerable<IPAddress> oldAddresses, CancellationToken cancellationToken)
        {
            var ipEndPoints = new HashSet<IPEndPoint>();

            // 历史未过期的IP节点
            foreach (var address in oldAddresses)
            {
                var domainAddress = new DomainAddress(dnsEndPoint.Host, address);
                if (this.domainAddressCache.TryGetValue(domainAddress, out _))
                {
                    ipEndPoints.Add(new IPEndPoint(address, dnsEndPoint.Port));
                }
            }

            // 新解析出的IP节点
            await foreach (var address in this.dnsClient.ResolveAsync(dnsEndPoint, fastSort: false, cancellationToken))
            {
                ipEndPoints.Add(new IPEndPoint(address, dnsEndPoint.Port));
                var domainAddress = new DomainAddress(dnsEndPoint.Host, address);
                this.domainAddressCache.Set(domainAddress, default(object), this.domainAddressExpiration);
            }

            if (ipEndPoints.Count == 0)
            {
                return Array.Empty<IPAddress>();
            }

            var addressElapsedTasks = ipEndPoints.Select(item => this.GetAddressElapsedAsync(dnsEndPoint.Host, item, cancellationToken));
            var addressElapseds = await Task.WhenAll(addressElapsedTasks);

            return addressElapseds
                .Where(item => item.Elapsed < TimeSpan.MaxValue)
                .OrderBy(item => item.Elapsed)
                .Select(item => item.Address)
                .ToArray();
        }


        /// <summary>
        /// 获取IP节点的时延
        /// </summary> 
        /// <param name="host">域名</param>
        /// <param name="endPoint"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task<AddressElapsed> GetAddressElapsedAsync(string host, IPEndPoint endPoint, CancellationToken cancellationToken)
        {
            if (this.addressElapsedCache.TryGetValue<AddressElapsed>(endPoint, out var addressElapsed))
            {
                return addressElapsed;
            }

            if (Enum.IsDefined(typeof(AddressFamily), endPoint.AddressFamily) == false)
            {
                return new AddressElapsed(endPoint.Address, TimeSpan.MaxValue);
            }

            // https且命中域名配置的节点，使用与真实请求一致的tls握手探测
            if (endPoint.Port == HTTPS_PORT && this.fastGithubConfig.TryGetDomainConfig(host, out var domainConfig))
            {
                return await this.GetTlsAddressElapsedAsync(domainConfig, host, endPoint, cancellationToken);
            }

            return await this.GetTcpAddressElapsedAsync(endPoint, cancellationToken);
        }

        /// <summary>
        /// tcp连接探测
        /// </summary>
        /// <param name="endPoint"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task<AddressElapsed> GetTcpAddressElapsedAsync(IPEndPoint endPoint, CancellationToken cancellationToken)
        {
            var stopWatch = Stopwatch.StartNew();
            try
            {
                using var timeoutTokenSource = new CancellationTokenSource(this.connectTimeout);
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTokenSource.Token);
                using var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(endPoint, linkedTokenSource.Token);

                var addressElapsed = new AddressElapsed(endPoint.Address, stopWatch.Elapsed);
                return this.addressElapsedCache.Set(endPoint, addressElapsed, this.normalElapsedExpiration);
            }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var addressElapsed = new AddressElapsed(endPoint.Address, TimeSpan.MaxValue);
                var expiration = IsLocalNetworkProblem(ex) ? this.problemElapsedExpiration : this.normalElapsedExpiration;
                return this.addressElapsedCache.Set(endPoint, addressElapsed, expiration);
            }
            finally
            {
                stopWatch.Stop();
            }
        }

        /// <summary>
        /// tls握手探测
        /// 按域名配置的候选SNI模式逐一尝试，全部失败视为劣化节点
        /// </summary>
        /// <param name="domainConfig"></param>
        /// <param name="host">域名</param>
        /// <param name="endPoint"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task<AddressElapsed> GetTlsAddressElapsedAsync(DomainConfig domainConfig, string host, IPEndPoint endPoint, CancellationToken cancellationToken)
        {
            var stopWatch = Stopwatch.StartNew();
            Exception lastException = null;
            try
            {
                using var timeoutTokenSource = new CancellationTokenSource(this.connectTimeout);
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTokenSource.Token);

                foreach (var pattern in domainConfig.GetHandshakeSniPatterns(host))
                {
                    try
                    {
                        var targetHost = pattern.WithIPAddress(endPoint.Address).Value;
                        await ConnectAndHandshakeAsync(endPoint, host, targetHost, domainConfig, linkedTokenSource.Token);

                        var addressElapsed = new AddressElapsed(endPoint.Address, stopWatch.Elapsed);
                        return this.addressElapsedCache.Set(endPoint, addressElapsed, this.normalElapsedExpiration);
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                stopWatch.Stop();
            }

            var problemElapsed = new AddressElapsed(endPoint.Address, TimeSpan.MaxValue);
            var problemExpiration = IsLocalNetworkProblem(lastException) ? this.problemElapsedExpiration : this.normalElapsedExpiration;
            return this.addressElapsedCache.Set(endPoint, problemElapsed, problemExpiration);
        }

        /// <summary>
        /// 建立连接并完成tls握手
        /// </summary>
        /// <param name="endPoint"></param>
        /// <param name="host">域名</param>
        /// <param name="targetHost">tls握手的SNI值，空字符串表示不发送SNI</param>
        /// <param name="domainConfig"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private static async Task ConnectAndHandshakeAsync(IPEndPoint endPoint, string host, string targetHost, DomainConfig domainConfig, CancellationToken cancellationToken)
        {
            using var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(endPoint, cancellationToken);

            using var networkStream = new NetworkStream(socket, ownsSocket: false);
            using var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = targetHost,
                RemoteCertificateValidationCallback = (sender, cert, chain, errors) => ValidateServerCertificate(errors, cert, domainConfig, host)
            }, cancellationToken);
        }

        /// <summary>
        /// 验证证书有效性(与真实转发层的判定规则一致)
        /// </summary>
        /// <param name="errors"></param>
        /// <param name="cert"></param>
        /// <param name="domainConfig"></param>
        /// <param name="host"></param>
        /// <returns></returns>
        private static bool ValidateServerCertificate(SslPolicyErrors errors, X509Certificate cert, DomainConfig domainConfig, string host)
        {
            if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
            {
                if (domainConfig.TlsIgnoreNameMismatch == true)
                {
                    return true;
                }

                var dnsNames = ReadDnsNames(cert);
                return dnsNames.Any(dns => IsMatch(dns, host));
            }

            return errors == SslPolicyErrors.None;
        }

        /// <summary>
        /// 读取证书的dns名称
        /// </summary>
        /// <param name="cert"></param>
        /// <returns></returns>
        private static IEnumerable<string> ReadDnsNames(X509Certificate cert)
        {
            if (cert is X509Certificate2 x509)
            {
                var extension = x509.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
                if (extension != null)
                {
                    return extension.EnumerateDnsNames();
                }
            }
            return Array.Empty<string>();
        }

        /// <summary>
        /// 比较域名
        /// </summary>
        /// <param name="dnsName"></param>
        /// <param name="domain"></param>
        /// <returns></returns>
        private static bool IsMatch(string dnsName, string domain)
        {
            if (dnsName == domain)
            {
                return true;
            }
            if (dnsName.Length > 0 && dnsName[0] == '*')
            {
                return domain.EndsWith(dnsName[1..]);
            }
            return false;
        }

        /// <summary>
        /// 是否为本机网络问题
        /// </summary>
        /// <param name="ex"></param>
        /// <returns></returns>
        private static bool IsLocalNetworkProblem(Exception ex)
        {
            if (ex is SocketException socketException)
            {
                var code = socketException.SocketErrorCode;
                return code == SocketError.NetworkDown || code == SocketError.NetworkUnreachable;
            }
            var inner = ex?.InnerException;
            return inner != null && IsLocalNetworkProblem(inner);
        }
    }
}
