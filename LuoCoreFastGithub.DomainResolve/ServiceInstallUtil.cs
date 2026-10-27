using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;

namespace LuoCoreFastGithub.DomainResolve
{
    /// <summary>
    /// Windows服务安装工具
    /// 通过sc.exe完成服务的创建/启动/停止/删除，避免对PInvoke绑定包的依赖
    /// </summary>
    public static class ServiceInstallUtil
    {
        /// <summary>
        /// 安装并启动服务
        /// </summary>
        /// <param name="serviceName">服务名</param>
        /// <param name="binaryPath">可执行文件路径</param>
        /// <param name="startType">启动类型：auto/demand/disabled</param>
        /// <returns></returns>
        [SupportedOSPlatform("windows")]
        public static bool InstallAndStartService(string serviceName, string binaryPath, string startType = "demand")
        {
            var fullPath = Path.GetFullPath(binaryPath);

            // sc create 对已存在的服务返回1073，此时视为可继续使用
            var (createExitCode, createOutput) = RunSc($"create \"{serviceName}\" binPath= \"{fullPath}\" start= {startType}");
            if (createExitCode == 1073)
            {
                RunSc($"config \"{serviceName}\" start= {startType}");
            }
            else if (createExitCode != 0 && Contains(createOutput, "1073") == false)
            {
                return false;
            }

            var (startExitCode, startOutput) = RunSc($"start \"{serviceName}\"");
            if (startExitCode == 0 || Contains(startOutput, "1056"))
            {
                return true;
            }

            // 已经处于运行状态时start会失败，二次确认后返回
            return IsRunning(serviceName) == true;
        }

        /// <summary>
        /// 停止并删除服务
        /// </summary>
        /// <param name="serviceName">服务名</param>
        /// <returns></returns>
        [SupportedOSPlatform("windows")]
        public static bool StopAndDeleteService(string serviceName)
        {
            var (queryExitCode, queryOutput) = RunSc($"query \"{serviceName}\"");
            if (queryExitCode != 0 || Contains(queryOutput, "1060"))
            {
                return true;
            }

            if (IsRunning(serviceName))
            {
                RunSc($"stop \"{serviceName}\"");

                // 等待服务退出，最长5秒
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed < TimeSpan.FromSeconds(5d))
                {
                    if (IsRunning(serviceName) == false)
                    {
                        break;
                    }
                    Thread.Sleep(200);
                }
            }

            var (deleteExitCode, _) = RunSc($"delete \"{serviceName}\"");
            return deleteExitCode == 0;
        }

        /// <summary>
        /// 服务是否处于运行状态
        /// </summary>
        /// <param name="serviceName"></param>
        /// <returns></returns>
        private static bool IsRunning(string serviceName)
        {
            var (_, output) = RunSc($"query \"{serviceName}\"");
            return Contains(output, "RUNNING");
        }

        /// <summary>
        /// 执行sc.exe
        /// </summary>
        /// <param name="arguments"></param>
        /// <returns>退出码与输出</returns>
        private static (int ExitCode, string Output) RunSc(string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return (-1, string.Empty);
                }

                var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                if (process.WaitForExit(10000) == false)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    }
                    return (-1, output);
                }
                return (process.ExitCode, output);
            }
            catch
            {
                return (-1, string.Empty);
            }
        }

        /// <summary>
        /// 是否包含文本
        /// </summary>
        /// <param name="text"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool Contains(string? text, string value)
        {
            return text != null && text.Contains(value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
