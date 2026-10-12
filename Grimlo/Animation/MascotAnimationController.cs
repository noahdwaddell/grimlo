using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Grimlo.Animation;

public enum MascotState
{
    Idle,
    Talking,
    Thinking,
    Sleeping
}

public class SpriteFrame
{
    public BitmapSource Image { get; }
    public int DurationMs { get; }

    public SpriteFrame(BitmapSource image, int durationMs = 100)
    {
        Image = image;
        DurationMs = Math.Max(20, durationMs);
    }
}

/// <summary>
/// Manages Grimlo's visual states and sprite animations.
/// Automatically detects and loads single PNGs, multi-frame animation sequences,
/// and animated GIFs (e.g. blink anim, talk anim) from Assets/Art/.
/// Preserves native GIF frame delays for frame-accurate timing.
/// Falls back to built-in vector shapes if no custom art is installed.
/// </summary>
public class MascotAnimationController
{
    private readonly Image _targetImage;
    private readonly UIElement _placeholderCanvas;
    private readonly DispatcherTimer _frameTimer;
    private readonly DispatcherTimer _sleepTimer;

    private readonly Dictionary<MascotState, List<SpriteFrame>> _animations = new();
    private MascotState _currentState = MascotState.Idle;
    private int _currentFrame = 0;

    public bool HasCustomArt => _animations.ContainsKey(MascotState.Idle) && _animations[MascotState.Idle].Count > 0;
    public MascotState CurrentState => _currentState;

    public MascotAnimationController(Image targetImage, UIElement placeholderCanvas)
    {
        _targetImage = targetImage;
        _placeholderCanvas = placeholderCanvas;

        _frameTimer = new DispatcherTimer();
        _frameTimer.Tick += (_, _) => AdvanceFrame();

        // 2 minutes of idle time before Grimlo falls asleep
        _sleepTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        _sleepTimer.Tick += (_, _) =>
        {
            if (_currentState == MascotState.Idle)
            {
                SetState(MascotState.Sleeping);
            }
        };

        ReloadArt();
        ResetSleepTimer();
    }

    /// <summary>
    /// Resets the 2-minute sleep countdown whenever the user interacts with Grimlo.
    /// Wakes Grimlo up if he was currently asleep.
    /// </summary>
    public void ResetSleepTimer()
    {
        _sleepTimer.Stop();
        _sleepTimer.Start();

        if (_currentState == MascotState.Sleeping)
        {
            SetState(MascotState.Idle);
        }
    }

    /// <summary>
    /// Switches Grimlo's visual state (Idle, Talking, Thinking, Sleeping).
    /// </summary>
    public void SetState(MascotState state)
    {
        if (_currentState == state) return;

        _currentState = state;
        _currentFrame = 0;

        if (state != MascotState.Sleeping)
        {
            _sleepTimer.Stop();
            _sleepTimer.Start();
        }

        UpdateDisplay();
    }

    /// <summary>
    /// Scans the Assets/Art folder and loads art files for all states.
    /// </summary>
    public void ReloadArt()
    {
        _animations.Clear();

        string? artDir = FindArtDirectory();
        if (artDir is not null && Directory.Exists(artDir))
        {
            var allFiles = Directory.GetFiles(artDir)
                .Where(f => IsSupportedImage(f))
                .ToList();

            // 1. IDLE: Look for blink animations or idle files first, then still files
            LoadState(MascotState.Idle, allFiles,
                primaryKeywords: new[] { "blink", "idle" },
                fallbackKeywords: new[] { "still", "default" });

            // 2. TALKING: Look for talk/speak/mouth animations, then fallback to still/idle
            LoadState(MascotState.Talking, allFiles,
                primaryKeywords: new[] { "talk", "speak", "mouth", "chat" },
                fallbackKeywords: new[] { "still" });

            // 3. THINKING: Look for think/ponder animations, then fallback to still/idle
            LoadState(MascotState.Thinking, allFiles,
                primaryKeywords: new[] { "think", "ponder", "scratch" },
                fallbackKeywords: new[] { "still" });

            // 4. SLEEPING: Look for sleep/snooze/zzz animations, then fallback to idle
            LoadState(MascotState.Sleeping, allFiles,
                primaryKeywords: new[] { "sleep", "snooze", "zzz", "nap" },
                fallbackKeywords: Array.Empty<string>());
        }

        // Global fallback: if Idle has frames, fill in any missing states with Idle
        if (_animations.TryGetValue(MascotState.Idle, out var idleFrames) && idleFrames.Count > 0)
        {
            if (!_animations.ContainsKey(MascotState.Talking)) _animations[MascotState.Talking] = idleFrames;
            if (!_animations.ContainsKey(MascotState.Thinking)) _animations[MascotState.Thinking] = idleFrames;
            if (!_animations.ContainsKey(MascotState.Sleeping)) _animations[MascotState.Sleeping] = idleFrames;
        }

        UpdateDisplay();
    }

    private void LoadState(MascotState state, List<string> allFiles, string[] primaryKeywords, string[] fallbackKeywords)
    {
        // Try finding files matching primary keywords
        var matched = FindMatchingFiles(allFiles, primaryKeywords);
        if (matched.Count == 0 && fallbackKeywords.Length > 0)
        {
            matched = FindMatchingFiles(allFiles, fallbackKeywords);
        }

        if (matched.Count == 0 && state == MascotState.Idle && allFiles.Count > 0)
        {
            // If no keywords matched anything for Idle, pick the first image available
            matched = new List<string> { allFiles[0] };
        }

        if (matched.Count > 0)
        {
            var frames = LoadFramesFromFiles(matched);
            if (frames.Count > 0)
            {
                _animations[state] = frames;
            }
        }
    }

    private static List<string> FindMatchingFiles(List<string> allFiles, string[] keywords)
    {
        foreach (string kw in keywords)
        {
            // Check for multi-frame sequence: e.g. talk_0.png, talk_1.png
            var sequence = allFiles
                .Where(f => NormalizeFilename(f).Contains(kw) && Regex.IsMatch(Path.GetFileNameWithoutExtension(f), @"\d+$"))
                .OrderBy(f => ExtractFrameNumber(f))
                .ToList();

            if (sequence.Count > 0) return sequence;

            // Check for single file or animated GIF: e.g. "Grimlo - blink anim.gif", "idle.png"
            var single = allFiles
                .Where(f => NormalizeFilename(f).Contains(kw))
                .OrderByDescending(f => f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) // Prefer animated gif over static if both match
                .ToList();

            if (single.Count > 0) return new List<string> { single[0] };
        }

        return new List<string>();
    }

    private static string NormalizeFilename(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return name.Replace('-', ' ').Replace('_', ' ');
    }

    private static List<SpriteFrame> LoadFramesFromFiles(List<string> files)
    {
        var frames = new List<SpriteFrame>();

        // If it's a single GIF file, extract all of its animation frames
        if (files.Count == 1 && files[0].EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = File.OpenRead(files[0]);
                var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

                if (decoder.Frames.Count > 0)
                {
                    foreach (BitmapFrame frame in decoder.Frames)
                    {
                        int delayMs = 100; // default 10 fps
                        if (frame.Metadata is BitmapMetadata metadata && metadata.ContainsQuery("/grctlext/Delay"))
                        {
                            object delayVal = metadata.GetQuery("/grctlext/Delay");
                            if (delayVal is ushort d && d > 0)
                            {
                                delayMs = d * 10; // centiseconds (1/100s) to milliseconds
                            }
                        }

                        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                        converted.Freeze();
                        frames.Add(new SpriteFrame(converted, delayMs));
                    }
                    return frames;
                }
            }
            catch
            {
                // Fall through to standard loading if GIF decoding fails
            }
        }

        // Multi-frame sequence (PNGs/JPGs) or single static image
        foreach (string file in files)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(file, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad; // Releases file lock so images can be edited live
                bmp.EndInit();
                bmp.Freeze();
                frames.Add(new SpriteFrame(bmp, 110));
            }
            catch { }
        }

        return frames;
    }

    private void UpdateDisplay()
    {
        if (HasCustomArt && _animations.TryGetValue(_currentState, out var frames) && frames.Count > 0)
        {
            _targetImage.Visibility = Visibility.Visible;
            _placeholderCanvas.Visibility = Visibility.Collapsed;

            if (_currentFrame >= frames.Count) _currentFrame = 0;
            var currentFrameData = frames[_currentFrame];
            _targetImage.Source = currentFrameData.Image;

            if (frames.Count > 1)
            {
                _frameTimer.Interval = TimeSpan.FromMilliseconds(currentFrameData.DurationMs);
                _frameTimer.Start();
            }
            else
            {
                _frameTimer.Stop();
            }
        }
        else
        {
            // Fallback: show placeholder vector shapes
            _frameTimer.Stop();
            _targetImage.Visibility = Visibility.Collapsed;
            _placeholderCanvas.Visibility = Visibility.Visible;
        }
    }

    private void AdvanceFrame()
    {
        if (_animations.TryGetValue(_currentState, out var frames) && frames.Count > 1)
        {
            _currentFrame = (_currentFrame + 1) % frames.Count;
            var frameData = frames[_currentFrame];
            _targetImage.Source = frameData.Image;
            _frameTimer.Interval = TimeSpan.FromMilliseconds(frameData.DurationMs);
        }
        else
        {
            _frameTimer.Stop();
        }
    }

    private static bool IsSupportedImage(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".gif" or ".bmp" or ".jpg" or ".jpeg";
    }

    private static int ExtractFrameNumber(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        var match = Regex.Match(name, @"\d+$");
        return match.Success && int.TryParse(match.Value, out int n) ? n : 0;
    }

    /// <summary>
    /// Searches for the Assets/Art directory in both the running bin folder and project directory.
    /// </summary>
    private static string? FindArtDirectory()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string candidate = Path.Combine(baseDir, "Assets", "Art");
        if (Directory.Exists(candidate)) return candidate;

        // In dev (dotnet run), bin/Debug/net8.0-windows is 3 folders below Grimlo
        DirectoryInfo? current = new DirectoryInfo(baseDir);
        for (int i = 0; i < 5 && current is not null; i++)
        {
            string devCandidate = Path.Combine(current.FullName, "Grimlo", "Assets", "Art");
            if (Directory.Exists(devCandidate)) return devCandidate;

            string directCandidate = Path.Combine(current.FullName, "Assets", "Art");
            if (Directory.Exists(directCandidate)) return directCandidate;

            current = current.Parent;
        }

        return null;
    }
}
