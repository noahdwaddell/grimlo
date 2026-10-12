namespace Grimlo.Settings;

/// <summary>
/// Serializable configuration options for Grimlo.
/// Note: API keys are stored separately in encrypted form via DPAPI,
/// never in plain text in this object.
/// </summary>
public class UserSettings
{
    public string ActiveProvider { get; set; } = "gemini";

    public string GeminiModel { get; set; } = "gemini-flash-lite-latest";

    public string ClaudeModel { get; set; } = "claude-sonnet-4-5";

    public string OpenAiModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Legacy chaos level for backwards compatibility.
    /// </summary>
    public int ChaosLevel
    {
        get => NormalcyChaosLevel;
        set => NormalcyChaosLevel = value;
    }

    /// <summary>
    /// Bluster vs Melancholy (1 = Heavy Brooding/Self-Pity to 5 = Theatrical Megalomania). Default is 3 (Signature Balance).
    /// </summary>
    public int BlusterMelancholyLevel { get; set; } = 3;

    /// <summary>
    /// Normalcy vs Gremlin Chaos (1 = Oddly Lucid & Professional to 5 = Unhinged Gremlin Overdrive). Default is 3 (Uncanny Whiplash).
    /// </summary>
    public int NormalcyChaosLevel { get; set; } = 3;

    /// <summary>
    /// Optional user-defined micro-interests or additional lore quirks.
    /// </summary>
    public string CustomMicroInterests { get; set; } = string.Empty;

    /// <summary>
    /// Optional custom instructions/directives appended to Grimlo's prompt.
    /// </summary>
    public string CustomInstructions { get; set; } = string.Empty;

    /// <summary>
    /// Whether Grimlo occasionally speaks up and taps the glass when left idle on the desktop.
    /// </summary>
    public bool EnableIdleChatter { get; set; } = true;

    /// <summary>
    /// Frequency of idle chatter in minutes (default 5, ranges from 2 to 20).
    /// </summary>
    public int IdleChatterIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Whether Grimlo will nod off and fall into deep sleep when left idle on the desktop.
    /// </summary>
    public bool EnableSleepCycles { get; set; } = true;

    /// <summary>
    /// Minutes of inactivity before Grimlo falls into deep sleep (default 8, ranges from 3 to 20).
    /// Drowsiness begins at approximately half this duration.
    /// </summary>
    public int SleepInactivityMinutes { get; set; } = 8;

    /// <summary>
    /// Name of the installed Windows SAPI voice to speak with (empty = system default).
    /// </summary>
    public string VoiceName { get; set; } = string.Empty;

    /// <summary>
    /// Speech rate (-10 to +10, default is 0).
    /// </summary>
    public int VoiceRate { get; set; } = 0;

    /// <summary>
    /// Speech volume (0 to 100, default is 100).
    /// </summary>
    public int VoiceVolume { get; set; } = 100;

    /// <summary>
    /// Whether retro sound effects (pops, taps, dizzy warbles) are enabled.
    /// </summary>
    public bool EnableSoundEffects { get; set; } = true;

    /// <summary>
    /// Sound effects volume (0 to 100, default is 80).
    /// </summary>
    public int SfxVolume { get; set; } = 80;

    /// <summary>
    /// Whether Grimlo is configured to launch automatically when logging into Windows.
    /// </summary>
    public bool StartWithWindows { get; set; } = false;

    /// <summary>
    /// Whether Grimlo occasionally makes ambient observations about your active foreground windows.
    /// </summary>
    public bool EnableWindowReactions { get; set; } = true;

    /// <summary>
    /// Cooldown between ambient active window reactions in minutes (ranges from 1 to 20, default 3).
    /// </summary>
    public int WindowReactionIntervalMinutes { get; set; } = 3;

    /// <summary>
    /// Whether Grimlo occasionally stretches his legs and paces/wanders along the bottom screen edge.
    /// </summary>
    public bool EnableDesktopWander { get; set; } = true;

    /// <summary>
    /// Interval in minutes between occasional wander/pacing behaviors (default 12, ranges from 5 to 30).
    /// </summary>
    public int WanderIntervalMinutes { get; set; } = 12;
}
