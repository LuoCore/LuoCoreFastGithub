using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;




namespace LuoCoreFastGithub.DomainResolve
{
    public static class ServiceInstallUtil
    {
        /// <summary>
        /// 安装并启动服务
        /// </summary>
        /// <param name="serviceName"></param>
        /// <param name="binaryPath"></param>
        /// <param name="startType"></param>
        /// <returns></returns>
        [SupportedOSPlatform("windows")]
        public static bool InstallAndStartService(string serviceName, string binaryPath, PInvoke.AdvApi32.ServiceStartType startType = PInvoke.AdvApi32.ServiceStartType.SERVICE_AUTO_START)
        {
     
            using var hSCManager = PInvoke.AdvApi32.OpenSCManager(null, null, PInvoke.AdvApi32.ServiceManagerAccess.SC_MANAGER_ALL_ACCESS);
            if (hSCManager.IsInvalid == true)
            {
                return false;
            }

            var hService = PInvoke.AdvApi32.OpenService(hSCManager, serviceName, PInvoke.AdvApi32.ServiceAccess.SERVICE_ALL_ACCESS);
            if (hService.IsInvalid == true)
            {
                hService = PInvoke.AdvApi32.CreateService(
                    hSCManager,
                    serviceName,
                    serviceName,
                    PInvoke.AdvApi32.ServiceAccess.SERVICE_ALL_ACCESS,
                    PInvoke.AdvApi32.ServiceType.SERVICE_WIN32_OWN_PROCESS,
                    startType,
                    PInvoke.AdvApi32.ServiceErrorControl.SERVICE_ERROR_NORMAL,
                    Path.GetFullPath(binaryPath),
                    lpLoadOrderGroup: null,
                    lpdwTagId: 0,
                    lpDependencies: null,
                    lpServiceStartName: null,
                    lpPassword: null);
            }

            if (hService.IsInvalid == true)
            {
                return false;
            }

            using (hService)
            {
                return PInvoke.AdvApi32.StartService(hService, 0, null);
            }
        }

        /// <summary>
        /// 停止并删除服务
        /// </summary>
        /// <param name="serviceName"></param>
        /// <returns></returns>
        [SupportedOSPlatform("windows")]
        public static bool StopAndDeleteService(string serviceName)
        {
            using var hSCManager = PInvoke.AdvApi32.OpenSCManager(null, null, PInvoke.AdvApi32.ServiceManagerAccess.SC_MANAGER_ALL_ACCESS);
            if (hSCManager.IsInvalid == true)
            {
                return false;
            }

            using var hService = PInvoke.AdvApi32.OpenService(hSCManager, serviceName, PInvoke.AdvApi32.ServiceAccess.SERVICE_ALL_ACCESS);
            if (hService.IsInvalid == true)
            {
                return true;
            }

            var status = new PInvoke.AdvApi32.SERVICE_STATUS();
            if (PInvoke.AdvApi32.QueryServiceStatus(hService, ref status) == true)
            {
                if (status.dwCurrentState != PInvoke.AdvApi32.ServiceState.SERVICE_STOP_PENDING &&
                    status.dwCurrentState != PInvoke.AdvApi32.ServiceState.SERVICE_STOPPED)
                {
                    PInvoke.AdvApi32.ControlService(hService, PInvoke.AdvApi32.ServiceControl.SERVICE_CONTROL_STOP, ref status);
                }
            }

            return PInvoke.AdvApi32.DeleteService(hService);
        }
    }
}
