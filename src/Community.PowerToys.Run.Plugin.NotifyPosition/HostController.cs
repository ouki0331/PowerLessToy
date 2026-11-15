using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using PowerLessToy.NotifyPosition.Core;

namespace Community.PowerToys.Run.Plugin.NotifyPosition;

public static class HostController
{
    private static string? _cachedHostPath;

    public static string FindHostExecutable()
    {
        if (!string.IsNullOrEmpty(_cachedHostPath) && File.Exists(_cachedHostPath))
        {
            return _cachedHostPath;
        }

        string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
        string candidate = Path.Combine(assemblyDir, "PowerLessToy.NotificationHost.exe");
        if (File.Exists(candidate))
        {
            _cachedHostPath = candidate;
            return candidate;
        }

        // Check fallback location (e.g. build output or plugins dir)
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] searchPaths = new[]
        {
            Path.Combine(localAppData, "Microsoft", "PowerToys", "PowerToys Run", "Plugins", "NotifyPosition", "PowerLessToy.NotificationHost.exe"),
            Path.Combine(localAppData, "PowerToys", "RunPlugins", "NotifyPosition", "PowerLessToy.NotificationHost.exe"),
            Path.Combine(assemblyDir, "..", "PowerLessToy.NotificationHost", "bin", "x64", "Release", "net10.0-windows10.0.26100.0", "PowerLessToy.NotificationHost.exe")
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                _cachedHostPath = path;
                return path;
            }
        }

        return candidate;
    }

    public static bool EnsureHostRunning()
    {
        if (IpcClient.IsHostRunning())
        {
            return true;
        }

        string exePath = FindHostExecutable();
        if (!File.Exists(exePath))
        {
            NotifyPositionLogger.Error($"PowerLessToy.NotificationHost.exe not found at '{exePath}'", null, nameof(HostController));
            return false;
        }

        try
        {
            NotifyPositionLogger.Info($"Launching NotificationHost: '{exePath}'", nameof(HostController));
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(exePath)
            };

            Process.Start(psi);

            // Wait briefly for host to initialize IPC pipe
            for (int i = 0; i < 15; i++)
            {
                Thread.Sleep(200);
                if (IpcClient.IsHostRunning())
                {
                    NotifyPositionLogger.Info("NotificationHost IPC connection verified", nameof(HostController));
                    return true;
                }
            }

            NotifyPositionLogger.Warn("NotificationHost was spawned but IPC check did not respond within 3s", null, nameof(HostController));
            return false;
        }
        catch (Exception ex)
        {
            NotifyPositionLogger.Error($"Failed to launch NotificationHost executable '{exePath}'", ex, nameof(HostController));
            return false;
        }
    }

    public static bool SendCommand(string command, out string response)
    {
        EnsureHostRunning();
        return IpcClient.SendCommand(command, out response);
    }
}
