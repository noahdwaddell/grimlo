using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Grimlo.Services;

public record DetectedMediaSession(string ProcessName, string Title, string ArtistOrSource);

public static class WindowsMediaDetector
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>
    /// Scans all running top-level windows to locate playing media tracks (Spotify, YouTube/Soundcloud in browsers, VLC, etc.)
    /// </summary>
    public static DetectedMediaSession? FindActiveMediaSession()
    {
        DetectedMediaSession? detected = null;

        try
        {
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true;

                var sb = new StringBuilder(512);
                int len = GetWindowText(hWnd, sb, sb.Capacity);
                if (len <= 0) return true;

                string title = sb.ToString().Trim();
                if (string.IsNullOrWhiteSpace(title)) return true;

                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == Environment.ProcessId) return true;

                string procName = string.Empty;
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    procName = proc.ProcessName;
                }
                catch { }

                string lowerProc = procName.ToLowerInvariant();
                string lowerTitle = title.ToLowerInvariant();

                // 1. Dedicated Spotify App
                // When Spotify plays a song, its window title is: "Artist - Track Title"
                // When Spotify is paused/idle or launched, its title is just "Spotify" or "Spotify Premium" / "Spotify Free"
                if (lowerProc.Contains("spotify"))
                {
                    if (title.Contains(" - "))
                    {
                        detected = new DetectedMediaSession("Spotify", title, "Spotify");
                        return false; // Stop enumeration, high confidence!
                    }
                    else if (!title.Equals("spotify", StringComparison.OrdinalIgnoreCase))
                    {
                        detected = new DetectedMediaSession("Spotify", title, "Spotify");
                    }
                }

                // 2. Browser Tabs with YouTube, Music, or Soundcloud
                if (lowerProc.Contains("chrome") || lowerProc.Contains("msedge") || lowerProc.Contains("firefox") || lowerProc.Contains("brave") || lowerProc.Contains("opera"))
                {
                    if (lowerTitle.Contains("youtube") || lowerTitle.Contains("soundcloud") || lowerTitle.Contains("bandcamp") || lowerTitle.Contains("spotify"))
                    {
                        // E.g. "Spandau Ballet - True (Official Video) - YouTube - Google Chrome"
                        string cleanTitle = title;
                        // Strip browser suffix
                        string[] browserSuffixes = { "- Google Chrome", "- Microsoft Edge", "— Mozilla Firefox", "- Brave", "- Opera" };
                        foreach (var s in browserSuffixes)
                        {
                            if (cleanTitle.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                            {
                                cleanTitle = cleanTitle.Substring(0, cleanTitle.Length - s.Length).Trim();
                            }
                        }

                        detected = new DetectedMediaSession(procName, cleanTitle, "Browser Media");
                    }
                }

                // 3. Other dedicated desktop players (Foobar2000, VLC, MusicBee, AIMP, Winamp, Apple Music)
                if (lowerProc.Contains("vlc") || lowerProc.Contains("foobar") || lowerProc.Contains("musicbee") || lowerProc.Contains("winamp") || lowerProc.Contains("aimp") || lowerProc.Contains("applemusic"))
                {
                    detected = new DetectedMediaSession(procName, title, procName);
                }

                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Grimlo] Media detection failed: {ex.Message}");
        }

        return detected;
    }
}
