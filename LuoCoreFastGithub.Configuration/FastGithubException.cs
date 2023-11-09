using System;

namespace LuoCoreFastGithub.Configuration
{
    /// <summary>
    /// 表示LuoCoreFastGithub异常
    /// </summary>
    public class LuoCoreFastGithubException : Exception
    {
        /// <summary>
        /// LuoCoreFastGithub异常
        /// </summary>
        /// <param name="message"></param>
        public LuoCoreFastGithubException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// LuoCoreFastGithub异常
        /// </summary>
        /// <param name="message"></param>
        /// <param name="innerException"></param>
        public LuoCoreFastGithubException(string message, Exception? innerException)
            : base(message, innerException)
        {
        }
    }
}
