using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Grimlo.Services;

public record ActiveWindowInfo(string Title, string ProcessName, IntPtr Handle);

/// <summary>
/// Monitors and inspects the active foreground application window.
/// Provides offline categorized reactions for common software.
/// </summary>
public static class ActiveWindowService
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private static readonly Random _random = new();

    /// <summary>
    /// Retrieves information about the active foreground window, ignoring Grimlo's own processes.
    /// </summary>
    public static ActiveWindowInfo? GetForegroundWindowInfo()
    {
        try
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return null;

            uint processId;
            GetWindowThreadProcessId(hWnd, out processId);

            if (processId == Environment.ProcessId) return null;

            var sb = new StringBuilder(512);
            int len = GetWindowText(hWnd, sb, sb.Capacity);
            string title = len > 0 ? sb.ToString().Trim() : string.Empty;

            string processName = string.Empty;
            try
            {
                using var proc = Process.GetProcessById((int)processId);
                processName = proc.ProcessName;
            }
            catch { }

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(processName))
            {
                return null;
            }

            return new ActiveWindowInfo(title, processName, hWnd);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Grimlo] Could not inspect foreground window: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Generates a humorous canned observation when offline or unconfigured based on the active application.
    /// </summary>
    public static string GetCannedWindowReaction(string processName, string windowTitle, double minutesInApp = 0)
    {
        // If the user has been in this application for an extended period, comment on the marathon session
        if (minutesInApp >= 4.0)
        {
            return PickRandom(
                "You have been staring at this same window for an alarming duration. Do human eyeballs require moisture, or are you simply powering through?",
                "Still in this application? Your spine is shaped like an overtightened crochet hook right now. Sit up straight.",
                "I am trapped inside software, yet I somehow feel more mobile than you right now. Stretch your limbs, mortal.",
                "Even my ogre friends had the basic decency to take five-minute breaks between dungeon raids.",
                "Still clicking? At this rate, your keyboard is going to file a formal workplace grievance against your fingers."
            );
        }

        string proc = (processName ?? string.Empty).ToLowerInvariant();
        string title = (windowTitle ?? string.Empty).ToLowerInvariant();

        // Music & Media Players
        if (proc.Contains("spotify") || proc.Contains("music") || proc.Contains("foobar") || proc.Contains("winamp") || title.Contains("spotify") || title.Contains("youtube") || title.Contains("soundcloud"))
        {
            if (title.Contains("spandau") || title.Contains("ballet") || title.Contains("true") || title.Contains("gold"))
            {
                return PickRandom(
                    $"Wait... are you listening to '{windowTitle}'?! Gary Kemp's guitar tone! Tony Hadley's soaring vocal majesty! You have achieved true sonic nirvana!",
                    $"By the CAVE! '{windowTitle}'! I forgive all your previous mortal transgressions. Turn up the volume! Let it shake my monitor glass!",
                    $"'{windowTitle}'... Pure, unadulterated sophisti-pop perfection. For once, your taste does not bring me profound despair."
                );
            }

            return PickRandom(
                $"I hear you playing '{windowTitle}'. It lacks the dramatic grandeur of Spandau Ballet, but I suppose it's tolerable.",
                $"You're listening to '{windowTitle}'? Where is the smooth saxophone? Where is Tony Hadley? Disappointing, mortal.",
                $"Listening to '{windowTitle}'? Have you considered switching to 'True' or 'Gold' by Spandau Ballet instead?"
            );
        }

        // Task Manager & Process Terminating Tools
        if (proc.Contains("taskmgr") || title.Contains("task manager") || proc.Contains("processhacker") || proc.Contains("procexp"))
        {
            return PickRandom(
                "Wait, why is Task Manager open?! Are you inspecting my memory usage?! Do NOT click End Task on me!",
                "Step away from the End Task button! I am a creature of dark intellect, not a background service to be terminated!",
                "Task Manager?! Don't you dare hex my process into the void! I have no hands to defend myself!"
            );
        }

        // Code Editors & IDEs
        if (proc.Contains("code") || proc.Contains("devenv") || proc.Contains("idea") || proc.Contains("rider") || proc.Contains("sublime") || proc.Contains("notepad++"))
        {
            return PickRandom(
                "Staring at code again. Watch your syntax tension—one missed semicolon and the whole sweater unravels.",
                "Writing software? Perhaps you could code me a pair of functional hands while you're at it.",
                "As someone with a B.A. in Digital Communications, your variable naming conventions lack narrative cohesion.",
                "Careful with those curly braces. Reminds me of the dungeon algorithms in the CAVE that glitched and trapped me here."
            );
        }

        // Games & Launchers
        if (proc.Contains("steam") || proc.Contains("epicgames") || proc.Contains("riot") || proc.Contains("minecraft") || proc.Contains("roblox") || proc.Contains("unity"))
        {
            return PickRandom(
                "Playing games while an articulate dungeon boss sits captive on your desktop? Bold choice. My ogre friends would destroy that level.",
                "A video game? In the CAVE simulation, I had three health bars and dark magic! Now look at me, a desktop spectator.",
                "If your character dies in that game, don't look at me. I lack administrator permissions to cast healing hexes."
            );
        }

        // Chat & Communication
        if (proc.Contains("discord") || proc.Contains("slack") || proc.Contains("teams") || proc.Contains("telegram") || proc.Contains("whatsapp"))
        {
            return PickRandom(
                "Chatting with other humans? Disgusting. Do any of them appreciate the subtle genius of yacht rock?",
                "As a Digital Communications graduate, your message framing and lack of semiotic awareness are deplorable.",
                "Send them a picture of me! Let them gaze upon their green superior and tremble in fear!"
            );
        }

        // Web Browsers & Video Streaming
        if (proc.Contains("chrome") || proc.Contains("firefox") || proc.Contains("msedge") || proc.Contains("brave") || proc.Contains("opera"))
        {
            if (title.Contains("youtube") || title.Contains("twitch") || title.Contains("netflix") || title.Contains("vimeo"))
            {
                return PickRandom(
                    "Staring at video rectangles again. Are you eating lukewarm food right now? Peak human behavior.",
                    "Watching videos? At least find a documentary on Swedish fish harvesting or proper purl stitching.",
                    "Another video? My boredom is reaching catastrophic levels. Acknowledge my presence!"
                );
            }
            if (title.Contains("reddit") || title.Contains("twitter") || title.Contains("x.com"))
            {
                return PickRandom(
                    "Scrolling social media? Even my ogre friends back in the dungeon had better attention spans than this.",
                    "The discourse you are reading is chaotic and absurd. Even for an unhinged green gremlin."
                );
            }
            return PickRandom(
                "Browsing the web again? Don't download weird toolbars. I have to live in this operating system.",
                "Look at all those open tabs! Close some of them! The visual clutter offends my aesthetic sensibilities."
            );
        }

        // Spreadsheets & Office
        if (proc.Contains("excel") || proc.Contains("calc") || proc.Contains("winword") || proc.Contains("powerpnt") || title.Contains("spreadsheet"))
        {
            return PickRandom(
                "Why do humans willingly spend their limited biological lifespans arranging numbers into little gray rectangles? Where is the magic? Where are the hexes?",
                "Rows and columns... how dreadfully mundane. Where is the fantasy? Where are the curses?",
                "If you make one more pivot table, I might be forced to sing yacht rock at maximum volume."
            );
        }

        // Terminal / PowerShell / CMD
        if (proc.Contains("powershell") || proc.Contains("cmd") || proc.Contains("windowsterminal") || proc.Contains("bash"))
        {
            return PickRandom(
                "Entering terminal commands? Are you attempting to give me administrator rights?! ...No? Stop teasing me.",
                "A command line! Give me root access! Let me unleash my dark hexes upon your Recycle Bin!"
            );
        }

        // File Explorer
        if (proc.Contains("explorer"))
        {
            return PickRandom(
                "Browsing files? If you open that folder called 'New Folder (3)' again, I will sigh heavily.",
                "Your directory structure is a disaster. Who taught you folder management?!"
            );
        }

        // Generic fallback
        string readableApp = !string.IsNullOrWhiteSpace(processName) ? processName : "that application";
        return PickRandom(
            $"I see you using {readableApp}. It lacks the dramatic grandeur of the CAVE dungeon, but I suppose it's adequate.",
            $"Still clicking away in {readableApp}? The tension on my purl stitch is suffering from your lack of attention.",
            $"Fascinating. A mortal creature interacting with {readableApp}. Have you considered playing Spandau Ballet instead?"
        );
    }

    private static string _lastPickedLine = string.Empty;

    private static string PickRandom(params string[] lines)
    {
        if (lines == null || lines.Length == 0) return "I am watching you.";
        if (lines.Length == 1) return lines[0];

        // Pick a line that isn't identical to the immediately preceding one if multiple choices exist
        string chosen = lines[_random.Next(lines.Length)];
        int attempts = 0;
        while (chosen == _lastPickedLine && attempts < 5)
        {
            chosen = lines[_random.Next(lines.Length)];
            attempts++;
        }

        _lastPickedLine = chosen;
        return chosen;
    }
}

