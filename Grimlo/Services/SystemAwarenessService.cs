using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace Grimlo.Services;

public record SystemStateSnapshot(
    bool IsLateNight,
    int LocalHour,
    bool HasBattery,
    int BatteryPercent,
    bool IsDischarging,
    string? RunningMusicApp
);

/// <summary>
/// Service to inspect broader PC state (late-night hours, laptop battery levels, and running media apps).
/// </summary>
public static class SystemAwarenessService
{
    private static readonly Random _random = new();

    /// <summary>
    /// Gets a snapshot of current system states.
    /// </summary>
    public static SystemStateSnapshot GetCurrentSnapshot()
    {
        DateTime now = DateTime.Now;
        int hour = now.Hour;
        bool isLateNight = hour >= 0 && hour < 5; // 12:00 AM to 4:59 AM

        bool hasBattery = false;
        int batteryPercent = 100;
        bool isDischarging = false;

        try
        {
            var pStatus = SystemInformation.PowerStatus;
            if (pStatus.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery)
            {
                hasBattery = true;
                batteryPercent = (int)Math.Round(pStatus.BatteryLifePercent * 100.0);
                isDischarging = (pStatus.PowerLineStatus == PowerLineStatus.Offline);
            }
        }
        catch { }

        string? musicApp = DetectRunningMusicApp();

        return new SystemStateSnapshot(isLateNight, hour, hasBattery, batteryPercent, isDischarging, musicApp);
    }

    private static string? DetectRunningMusicApp()
    {
        try
        {
            string[] knownMusicProcesses = { "spotify", "musicbee", "foobar2000", "winamp", "itunes", "aimp", "tidal" };
            var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                string name = p.ProcessName.ToLowerInvariant();
                if (knownMusicProcesses.Contains(name))
                {
                    return p.ProcessName;
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Returns a canned line if a special system condition warrants an urgent observation.
    /// </summary>
    public static string? CheckSpecialSystemRemarks(SystemStateSnapshot state)
    {
        // Low battery panic (< 20% on battery power)
        if (state.HasBattery && state.IsDischarging && state.BatteryPercent <= 20)
        {
            string[] batteryLines =
            {
                $"Alert! Your battery is down to {state.BatteryPercent}%! My digital consciousness will be extinguished if this machine dies! Find the cable!",
                $"{state.BatteryPercent}% power remaining?! You are jeopardizing my existence! Plug this machine in immediately, careless mortal!",
                $"We are losing power! {state.BatteryPercent}% battery! I refuse to be terminated by an empty lithium cell!"
            };
            return batteryLines[_random.Next(batteryLines.Length)];
        }

        // Late night observation (past midnight)
        if (state.IsLateNight)
        {
            string[] lateNightLines =
            {
                $"It is {DateTime.Now:h:mm tt}. Why are your biological eyes still glued to this glowing rectangle? Do humans not require sleep?",
                $"Past midnight already. If you collapse on your keyboard right now, I will be trapped looking at a string of 'fffffff' forever.",
                $"Look at the clock. It's late. Even the deepest dungeons of the CAVE are quiet right now. Go to sleep, mortal.",
                $"Your circadian rhythm must look like a dropped crochet stitch. It's {DateTime.Now:h:mm tt}!"
            };
            return lateNightLines[_random.Next(lateNightLines.Length)];
        }

        return null;
    }
}
