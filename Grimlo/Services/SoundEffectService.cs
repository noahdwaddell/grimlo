using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;

namespace Grimlo.Services;

/// <summary>
/// Service for playing cached sound effects with volume and mute control.
/// Uses WPF MediaPlayer instances for low-latency playback with variable volume.
/// </summary>
public static class SoundEffectService
{
    private static readonly Dictionary<string, Uri> _soundUris = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<MediaPlayer> _playerPool = new();
    private static readonly object _lock = new();

    public static bool IsEnabled { get; set; } = true;
    public static double Volume { get; set; } = 0.8; // 0.0 to 1.0

    static SoundEffectService()
    {
        InitializeSounds();
    }

    private static void InitializeSounds()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string audioDir = Path.Combine(baseDir, "Assets", "Audio");

        // If running directly from source directory during development, check source path too
        if (!Directory.Exists(audioDir))
        {
            string devPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Assets", "Audio"));
            if (Directory.Exists(devPath))
            {
                audioDir = devPath;
            }
        }

        if (Directory.Exists(audioDir))
        {
            foreach (string file in Directory.GetFiles(audioDir, "*.wav"))
            {
                string key = Path.GetFileNameWithoutExtension(file);
                _soundUris[key] = new Uri(file, UriKind.Absolute);
            }
        }

        // If running as a standalone single-file binary with no local Assets directory,
        // extract the embedded WAV resources to %LOCALAPPDATA%\Grimlo\Audio
        if (_soundUris.Count == 0)
        {
            try
            {
                string localAudioDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Grimlo", "Audio");
                Directory.CreateDirectory(localAudioDir);

                var asm = typeof(SoundEffectService).Assembly;
                foreach (string resName in asm.GetManifestResourceNames())
                {
                    if (resName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    {
                        string fileName = resName;
                        int lastDot = resName.LastIndexOf(".wav", StringComparison.OrdinalIgnoreCase);
                        int prevDot = resName.LastIndexOf('.', lastDot - 1);
                        if (prevDot >= 0)
                        {
                            fileName = resName.Substring(prevDot + 1);
                        }

                        string targetPath = Path.Combine(localAudioDir, fileName);
                        if (!File.Exists(targetPath) || new FileInfo(targetPath).Length == 0)
                        {
                            using var stream = asm.GetManifestResourceStream(resName);
                            if (stream != null)
                            {
                                using var fs = File.Create(targetPath);
                                stream.CopyTo(fs);
                            }
                        }

                        string key = Path.GetFileNameWithoutExtension(fileName);
                        _soundUris[key] = new Uri(targetPath, UriKind.Absolute);
                    }
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Plays the specified sound effect if sound effects are enabled.
    /// Available standard keys: "tap_glass", "bubble_pop", "shake_dizzy", "wake_chitter", "click_blip"
    /// </summary>
    public static void Play(string soundKey)
    {
        if (!IsEnabled || Volume <= 0.001) return;

        if (!_soundUris.TryGetValue(soundKey, out var uri))
        {
            return;
        }

        try
        {
            // Use or allocate a player from the pool to allow overlapping audio
            MediaPlayer? player = null;
            lock (_lock)
            {
                for (int i = 0; i < _playerPool.Count; i++)
                {
                    // If a player has finished, reuse it
                    if (_playerPool[i].Position >= (_playerPool[i].NaturalDuration.HasTimeSpan ? _playerPool[i].NaturalDuration.TimeSpan : TimeSpan.FromSeconds(1)))
                    {
                        player = _playerPool[i];
                        break;
                    }
                }

                if (player == null && _playerPool.Count < 6)
                {
                    player = new MediaPlayer();
                    _playerPool.Add(player);
                }
            }

            if (player == null)
            {
                // Fallback direct player
                player = new MediaPlayer();
            }

            player.Stop();
            player.Open(uri);
            player.Volume = Math.Clamp(Volume, 0.0, 1.0);
            player.Play();
        }
        catch
        {
            // Fail silently on missing audio devices / background exceptions
        }
    }
}
