using System.Diagnostics;
using System.Management;
using System.ServiceProcess;
using Velopack;

namespace HshAgent;

public static class WindowsInstallHooks
{
    private const string ServiceName = "HshAgent";
    private const string DisplayName = "HSH Agent";

    public static bool RegisteredSelfStartingAutostart { get; set; }

    public static void AfterInstall(SemanticVersion version)
    {
        RegisterWindowsService();
    }

    public static void BeforeUninstall(SemanticVersion version)
    {
        UnregisterWindowsService();
    }

    public static void FirstRun(SemanticVersion version)
    {
        // Service was already started by AfterInstall (elevated), just hand off to it
        RegisteredSelfStartingAutostart = true;
        Console.WriteLine($"HSH Agent v{version} installed.");
    }

    private static void RegisterWindowsService()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        try
        {
#pragma warning disable CA1416
            var scope = new ManagementScope(@"\\.\root\cimv2");
            scope.Connect();

            var mcd = new ManagementClass(scope, new ManagementPath("Win32_Service"), null);
            var inParams = mcd.GetMethodParameters("Create");
            inParams["Name"] = ServiceName;
            inParams["PathName"] = exe;
            inParams["DisplayName"] = DisplayName;
            inParams["StartMode"] = "Automatic";
            var outParams = mcd.InvokeMethod("Create", inParams, null);

            var returnValue = Convert.ToInt32(outParams["returnValue"]);
            if (returnValue != 0)
            {
                Console.Error.WriteLine($"Failed to create service: error code {returnValue}");
                return;
            }

            using var service = new ServiceController(ServiceName);
            service.Start();
            Console.WriteLine($"Service '{DisplayName}' registered and started successfully.");
#pragma warning restore CA1416
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to register Windows Service: {ex.Message}");
        }
    }

    private static void UnregisterWindowsService()
    {
        try
        {
#pragma warning disable CA1416
            using var service = new ServiceController(ServiceName);
            if (service.Status != ServiceControllerStatus.Stopped)
                service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
#pragma warning restore CA1416
        }
        catch
        {
            // Service might not exist, continue with deletion
        }

        try
        {
            Run("sc.exe", $"delete {ServiceName}");
            Console.WriteLine($"Service '{DisplayName}' unregistered successfully.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to unregister Windows Service: {ex.Message}");
        }
    }

    private static void Run(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            p?.WaitForExit();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to run {file}: {ex.Message}");
        }
    }
}
