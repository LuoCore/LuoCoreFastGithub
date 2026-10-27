using LuoCoreFastGithub.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LuoCoreFastGithub.DomainResolve
{
    /// <summary>
    /// 内置DoH(DNS-over-HTTPS)域名解析器
    /// 以application/dns-json方式并发查询多个DoH节点，
    /// 不依赖dnscrypt-proxy外部进程，节点失败自动进入冷却期
    /// </summary>
    sealed class DohResolver : IDisposable
    {
        private const int DNS_RCODE_NOERROR = 0;
        private const int DNS_TYPE_A = 1;
        private const int DNS_TYPE_AAAA = 28;
        private const int DEFAULT_PORT = 443;

        private readonly LuoCoreFastGithubConfig fastGithubConfig;
        private readonly ILogger<DohResolver> logger;
        private readonly HttpClient httpClient;
        private readonly IMemoryCache answerCache;
        private readonly IMemoryCache endpointCooldown;

        private readonly TimeSpan minTimeToLive = TimeSpan.FromSeconds(30d);
        private readonly TimeSpan maxTimeToLive = TimeSpan.FromMinutes(10d);
        private readonly TimeSpan emptyResultTimeToLive = TimeSpan.FromSeconds(10d);
        private readonly TimeSpan cooldownTimeSpan = TimeSpan.FromMinutes(2d);

        /// <summary>
        /// 内置DoH域名解析器
        /// </summary>
        /// <param name="fastGithubConfig"></param>
        /// <param name="logger"></param>
        public DohResolver(LuoCoreFastGithubConfig fastGithubConfig, ILogger<DohResolver> logger)
        {
            this.fastGithubConfig = fastGithubConfig;
            this.logger = logger;
            this.answerCache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
            this.endpointCooldown = new MemoryCache(Options.Create(new MemoryCacheOptions()));

            var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(3d),
                PooledConnectionLifetime = TimeSpan.FromMinutes(10d)
            };
            this.httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(5d),
                DefaultRequestVersion = new Version(2, 0),
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };
        }

        /// <summary>
        /// 解析域名
        /// </summary>
        /// <param name="host">域名</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            if (this.answerCache.TryGetValue<IPAddress[]>(host, out var cached))
            {
                return cached;
            }

            var endpoints = this.GetActiveEndpoints();
            if (endpoints.Length == 0)
            {
                return Array.Empty<IPAddress>();
            }

            var types = new List<int> { DNS_TYPE_A };
            if (Socket.OSSupportsIPv6)
            {
                types.Add(DNS_TYPE_AAAA);
            }

            var tasks = new List<Task<QueryResult>>();
            foreach (var endpoint in endpoints)
            {
                foreach (var type in types)
                {
                    tasks.Add(this.QueryAsync(endpoint, host, type, cancellationToken));
                }
            }
            await Task.WhenAll(tasks);

            var addresses = new List<IPAddress>();
            var timeToLive = this.maxTimeToLive;
            var successCount = 0;
            foreach (var task in tasks)
            {
                var result = task.Result;
                if (result.Success == false)
                {
                    continue;
                }
                successCount++;
                addresses.AddRange(result.Addresses);
                if (result.TimeToLive < timeToLive)
                {
                    timeToLive = result.TimeToLive;
                }
            }

            // 全部节点不可用时短缓存空结果，避免故障放大
            var uniqueAddresses = Distinct(addresses);
            if (successCount == 0)
            {
                return this.answerCache.Set(host, uniqueAddresses, this.emptyResultTimeToLive);
            }

            if (timeToLive <= TimeSpan.Zero)
            {
                timeToLive = this.minTimeToLive;
            }
            else if (timeToLive > this.maxTimeToLive)
            {
                timeToLive = this.maxTimeToLive;
            }
            return this.answerCache.Set(host, uniqueAddresses, timeToLive);
        }

        /// <summary>
        /// 获取处于可用状态的doh节点
        /// </summary>
        /// <returns></returns>
        private string[] GetActiveEndpoints()
        {
            var endpoints = this.fastGithubConfig.DohEndpoints ?? Array.Empty<string>();
            var activeList = new List<string>();
            foreach (var endpoint in endpoints)
            {
                if (string.IsNullOrWhiteSpace(endpoint) || Uri.IsWellFormedUriString(endpoint, UriKind.Absolute) == false)
                {
                    continue;
                }
                if (this.endpointCooldown.TryGetValue(endpoint, out _))
                {
                    continue;
                }
                activeList.Add(endpoint.TrimEnd('/', '?'));
            }

            // 所有节点都处于冷却期时放行全部，给一次重试机会
            if (activeList.Count == 0)
            {
                foreach (var endpoint in endpoints)
                {
                    if (Uri.IsWellFormedUriString(endpoint, UriKind.Absolute))
                    {
                        activeList.Add(endpoint.TrimEnd('/', '?'));
                    }
                }
            }
            return activeList.ToArray();
        }

        /// <summary>
        /// 查询单个节点
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="host"></param>
        /// <param name="type"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task<QueryResult> QueryAsync(string endpoint, string host, int type, CancellationToken cancellationToken)
        {
            var uri = $"{endpoint}?name={Uri.EscapeDataString(host)}&type={type}";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-json"));
                using var response = await this.httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                this.endpointCooldown.Remove(endpoint);
                return ParseResponse(json);
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                this.MarkUnavailable(endpoint);
                return QueryResult.Failed;
            }
            catch (Exception ex)
            {
                this.MarkUnavailable(endpoint);
                this.logger.LogDebug($"DoH {endpoint} 查询 {host} 失败：{ex.Message}");
                return QueryResult.Failed;
            }
        }

        /// <summary>
        /// 标记节点进入冷却期
        /// </summary>
        /// <param name="endpoint"></param>
        private void MarkUnavailable(string endpoint)
        {
            this.endpointCooldown.Set(endpoint, true, this.cooldownTimeSpan);
        }

        /// <summary>
        /// 解析dns-json响应
        /// 字段名大小写因服务商而异(Status/STATUS、Answer/ANSWER、Ttl/TTL)，统一按忽略大小写读取
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        private static QueryResult ParseResponse(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (TryGetProperty(root, "Status", out var statusElement) &&
                TryGetInt(statusElement, out var status) &&
                status != DNS_RCODE_NOERROR)
            {
                return new QueryResult(Array.Empty<IPAddress>(), TimeSpan.Zero, Success: true);
            }

            var addresses = new List<IPAddress>();
            var timeToLive = TimeSpan.MaxValue;
            if (TryGetProperty(root, "Answer", out var answerElement) && answerElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in answerElement.EnumerateArray())
                {
                    if (TryGetProperty(item, "data", out var dataElement) == false ||
                        IPAddress.TryParse(dataElement.GetString(), out var address) == false)
                    {
                        continue;
                    }
                    if (IPAddress.IsLoopback(address))
                    {
                        continue;
                    }
                    addresses.Add(address);

                    var itemTtl = TimeSpan.FromSeconds(60d);
                    if (TryGetProperty(item, "Ttl", out var ttlElement) && TryGetInt(ttlElement, out var ttl))
                    {
                        itemTtl = TimeSpan.FromSeconds(ttl);
                    }
                    if (itemTtl < timeToLive)
                    {
                        timeToLive = itemTtl;
                    }
                }
            }

            if (timeToLive == TimeSpan.MaxValue)
            {
                timeToLive = TimeSpan.Zero;
            }
            return new QueryResult(Distinct(addresses), timeToLive, Success: true);
        }

        /// <summary>
        /// 忽略大小写读取属性
        /// </summary>
        /// <param name="element"></param>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }

        /// <summary>
        /// 读取数值字段(兼容数字与字符串两种序列化)
        /// </summary>
        /// <param name="element"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool TryGetInt(JsonElement element, out int value)
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.TryGetInt32(out value);
            }
            if (element.ValueKind == JsonValueKind.String)
            {
                return int.TryParse(element.GetString(), out value);
            }
            value = default;
            return false;
        }

        /// <summary>
        /// 去重
        /// </summary>
        /// <param name="addresses"></param>
        /// <returns></returns>
        private static IPAddress[] Distinct(List<IPAddress> addresses)
        {
            var seen = new HashSet<IPAddress>();
            var result = new List<IPAddress>();
            foreach (var address in addresses)
            {
                if (seen.Add(address))
                {
                    result.Add(address);
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// 查询结果
        /// </summary>
        private readonly record struct QueryResult(IPAddress[] Addresses, TimeSpan TimeToLive, bool Success)
        {
            public static QueryResult Failed { get; } = new QueryResult(Array.Empty<IPAddress>(), TimeSpan.Zero, false);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            this.httpClient.Dispose();
        }
    }
}
