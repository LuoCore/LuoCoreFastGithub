using System.Net;

namespace LuoCoreFastGithub.DomainResolve
{
    /// <summary>
    /// 连接失败回报器
    /// </summary>
    public interface IConnectionFailureReporter
    {
        /// <summary>
        /// 回报真实请求中连接失败的ip节点，使其在测速中被淘汰
        /// </summary>
        /// <param name="endPoint">失败的ip节点</param>
        void ReportFailure(IPEndPoint endPoint);
    }
}
