using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Grimlo.Services;

/// <summary>
/// Manages automatic launching of Grimlo on Windows user login
/// via the HKCU\Software\Microsoft\Windows\CurrentVersion\Run registry key.
/// Does not require administrative elevation.
/// </summary>
public static class StartupService
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Grimlo";

    /// <summary>
    /// Checks whether Grimlo is registered to run on Windows startup.
    /// </summary>
    public static bool IsRunOnStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: false);
            if (key == null) return false;

            var val = key.GetValue(AppName) as string;
            return !string.IsNullOrWhiteSpace(val);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Grimlo] Could not read startup registry key: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Enables or disables running Grimlo on Windows user login.
    /// </summary>
    public static bool SetRunOnStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true);
            if (key == null) return false;

            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath) || exePath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                {
                    string candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Grimlo.exe");
                    if (File.Exists(candidate))
                    {
                        exePath = candidate;
                    }
                }

                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\"");
                    return true;
                }

                return false;
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Grimlo] Could not update startup registry key: {ex.Message}");
            return false;
        }
    }
}
