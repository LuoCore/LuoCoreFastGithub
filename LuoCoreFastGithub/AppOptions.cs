namespace LuoCoreFastGithub
{
    /// <summary>
    /// app选项
    /// </summary>
    public record AppOptions
    {
        /// <summary>
        /// 父进程id
        /// </summary>
        public int ParentProcessId { get; init; }

        /// <summary>
        /// udp日志服务器端口
        /// </summary>
        public int UdpLoggerPort { get; init; }

        /// <summary>
        /// 是否自动接管windows系统代理(指向本地转发代理并在退出时还原)
        /// </summary>
        public bool AutoSetSystemProxy { get; init; } = true;
    }
}
