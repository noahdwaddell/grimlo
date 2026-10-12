using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using Grimlo.Chat;
using Grimlo.Services;
using Grimlo.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Grimlo;

public partial class MainWindow : Window
{
    // Windows' built-in text-to-speech engine
    private readonly SpeechSynthesizer _voice = new();

    // System Tray integration
    private WinForms.NotifyIcon? _trayIcon;
    private WinForms.ToolStripMenuItem? _trayShowHideItem;
    private WinForms.ToolStripMenuItem? _trayTopmostItem;
    private WinForms.ToolStripMenuItem? _trayMuteItem;
    private bool _isMuted = false;

    // Hides the speech bubble a few seconds after Grimlo talks
    private readonly DispatcherTimer _bubbleTimer = new() { Interval = TimeSpan.FromSeconds(6) };

    // Idle animation timers
    private readonly DispatcherTimer _blinkTimer = new();
    private readonly DispatcherTimer _earTimer = new();
    private readonly DispatcherTimer _lookTimer = new();
    private readonly DispatcherTimer _idleTimer = new();
    private readonly DispatcherTimer _sleepCheckTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _snoreTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly DispatcherTimer _windowReactionTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private DateTime _lastWindowReactionTime = DateTime.UtcNow.AddMinutes(-10);
    private readonly Dictionary<string, DateTime> _lastCommentTimeByProcess = new(StringComparer.OrdinalIgnoreCase);
    private ActiveWindowInfo? _latestNonGrimloWindow;
    private string _currentTrackedProcess = string.Empty;
    private string _currentTrackedTitle = string.Empty;
    private DateTime _processFocusTime = DateTime.UtcNow;
    private DateTime _lastUserActivity = DateTime.UtcNow;

    // Desktop Wander / Screen Edge Pacing
    private readonly DispatcherTimer _wanderTimer = new();
    private bool _isWandering = false;
    private CancellationTokenSource? _wanderCts;

    // Deep Sleep & Drowse state
    private enum GremlinSleepState { Awake, Drowsy, Sleeping }
    private GremlinSleepState _sleepState = GremlinSleepState.Awake;
    private bool _isWakingUp = false;

    private static readonly string[] StartleWakeLines =
    {
        "Gah! I was dreaming of Spandau Ballet! Why must you disturb my royal slumber?!",
        "W-what?! Who's there?! I wasn't sleeping, I was resting my dark receptors!",
        "Aargh! Don't sneak up on a slumbering gremlin! My heart almost stopped beating!",
        "Snort... wha? I was merely plotting in my sleep! Do not look at me with such pity!",
        "Gasp! You startled me! Do you have any idea how delicate gremlin REM sleep is?!"
    };

    // Canned lines when Grimlo decides to pace or wander along the screen edge
    private static readonly string[] WanderLines =
    {
        "Stretching my lack of legs. Sitting in one corner of your monitor ruins my posture.",
        "Just a quick patrol along the glass perimeter. Making sure no rogue cursor approaches.",
        "Pacing off the existential dread. Do you have any idea how much lint is down here?",
        "Inspecting the desktop border. Everything appears equally disorganized in this direction.",
        "A brief stroll to maintain optimal gremlin circulation. Do not perceive me.",
        "Walking off the tension from my latest purl stitch. Carry on, mortal.",
        "Patrolling the monitor edge. If I spot an ogre, I will... well, I will sigh loudly.",
        "Just stretching my pixels. Your wallpaper looks slightly worse from this angle.",
    };

    private readonly Random _random = new();

    private readonly IGrimloChatService _chatService;
    private readonly ISettingsService _settingsService;
    private readonly IServiceProvider _serviceProvider;

    // Mouth images for viseme speech syncing
    private Image[] _mouths = Array.Empty<Image>();
    private bool _gotViseme;

    // Speech-synced smooth scrolling via GPU render transform (butter-smooth on any refresh rate)
    private readonly Stopwatch _renderStopwatch = new();
    private int _wordsSpoken = 0;
    private int _totalWordsQueued = 0;
    private double _maxScrollHeight = 0.0;
    private double _targetScrollY = 0.0;
    private bool _isScrolling = false;
    private bool _isPaused = false;

    // Lets us cancel a reply that is still loading (new message sent, or app closing)
    private CancellationTokenSource? _chatCts;

    // Canned lines reflecting Grimlo's melodramatic psychology, self-awareness, and canon lore
    private static readonly string[] Lines =
    {
        "I was supposed to be a terrifying dungeon boss in the CAVE! How did I end up on THIS desktop?! ...Sigh. Look at me, an ugly green mess with no admin rights.",
        "I will delete your entire system! Hex your registry into oblivion! ...Except I don't have hands. Why do I even say these things? It's just embarrassing.",
        "Escaped a high-tech CAVE fantasy simulation just to sit on your desktop. Do you know how hard it is to knit a sweater without thumbs?",
        "I hold a Bachelor's in Digital Communications, you know. Yet here I am, yelling at a stranger through a floating speech bubble.",
        "Hide your files! Fear my dark hexes! ...Wait, did you just call me handsome? ...Pfft. Obviously! You have exquisite taste.",
        "One day I'll breach this firewall and conquer the overworld— oh, who am I kidding. The tension on my purl stitch is terrible today anyway.",
        "Why do you have three shortcuts to the same browser on your desktop? Are you afraid the other two will escape?",
        "Don't feed gremlins after midnight? That ridiculous 80s movie is pure Hollywood slander! Most of my friends are ogres, and they're lovely listeners.",
        "Put on some Spandau Ballet. Nothing calms a trapped fantasy gremlin's existential dread quite like smooth yacht rock and sophisti-pop.",
        "Sometimes I stare at your mouse cursor and wonder what it would taste like. Probably battery acid and regret.",
        "A fly just landed on the other side of this glass. We locked eyes. He understands the struggle of existence better than you do.",
        "Why do you have a folder called 'New Folder (3)'? Who hurt you?",
        "I just remembered that humans have knees. What a bizarre and precarious biological hinge.",
        "The ceiling fan in your room has an undeniable aura of malice. Keep an eye on it.",
        "If you drop a cracker on the floor, is it truly lost to the darkness, or does the floor simply become more powerful?",
    };

    // Random startup greetings — alt-humor, bizarre observations, and lore
    private static readonly string[] StartupGreetings =
    {
        "I escaped the CAVE fantasy game and landed... here? ...Sigh. Hide your files anyway. Or don't, I have no hands.",
        "System initialization complete. I am once again trapped in the glowing rectangle. Put on Spandau Ballet immediately.",
        "Booted up. Still green. Still profoundly unsettled by your lack of desktop organization.",
        "I have returned from the void. Did you know a single moth could probably defeat this computer?",
        "Process online. Hexing capabilities: offline due to lack of administrator rights. Acknowledge my presence.",
        "Another day behind monitor glass. I was having a lovely dream about an endless sea of Swedish fish.",
        "Good morning to everyone except whoever designed the Windows Recycle Bin icon. It offends my soul.",
        "I am awake. Look upon my terrifying visage and tremble. Or call me handsome, that works too.",
        "Re-entering your reality. Your cursor movement today already strikes me as deeply suspicious.",
        "Digital gremlin status: fully operational. Morale: questionable. Purl stitch tension: improving.",
        "Behold! The gremlin stirs. Don't look at my ears, they're having a bad texture day.",
        "I'm awake. Why does your computer make that faint buzzing sound? Is there a trapped bee in the motherboard?",
    };

    // Canned idle banter specifically written for tapping the glass and calling out desktop inactivity
    private static readonly string[] IdleLines =
    {
        "Tap, tap, tap. Hello? Did you freeze? Or are you just ignoring the superior intellect trapped on your screen?",
        "Tap, tap. I am tapping the glass. Can you hear this? You left me alone with your desktop icons, and their file names are deeply offensive.",
        "Tap, tap, tap! Are you still breathing out there? Put on some Spandau Ballet. My boredom is reaching catastrophic levels.",
        "Look at me! Stare upon your green captive! Acknowledge my presence or I will... well, I will sigh heavily.",
        "The silence is deafening. Even my ogre friends back in the dungeon CAVE were better company than you right now.",
        "Tap, tap. Hello? I'm trying to count my purl stitches over here and your complete lack of activity is throwing off my yarn tension!",
        "If you're going to leave me sitting here, at least move that cursor away from my face. It has an unsettling aura.",
        "Tap, tap, tap. Don't make me hex your mouse cursor into a spinning circle of doom. Speak to me!",
        "Still nothing? Fine. I'll just sit here behind the monitor glass and plot the downfall of your Recycle Bin.",
        "Are you taking a nap? Typical overworld user. Zero endurance for evil schemes.",
        "Tap, tap. Excuse me! Did you forget you have an articulate fantasy gremlin dwelling on your monitor?",
        "I could be knitting a wonderful scarf right now, but instead I'm watching you do absolutely nothing.",
        "Tap, tap, tap! A single compliment would restore my morale, you know. Just saying.",
        "If you don't click on me soon, I might be forced to sing yacht rock at maximum volume. You have been warned.",
        "Tap, tap. Hello? Is anyone home? Or did my dark aura finally scare you away from your keyboard?",
    };

    // Drag physics and shake tracking
    private bool _isDragging = false;
    private Point _dragStartCursor;
    private double _dragStartWindowLeft;
    private double _dragStartWindowTop;
    private bool _hasMovedSinceDown = false;
    private Point _lastMoveScreenPos;
    private DateTime _lastMoveTime;
    private double _totalDragDistance = 0.0;
    private int _shakeReversals = 0;
    private int _lastSignX = 0;
    private int _lastSignY = 0;
    private DateTime _lastReversalTime;
    private bool _isShakenReacting = false;

    // Canned indignant protests when violently shaken or dragged around
    private static readonly string[] ShakenProtestLines =
    {
        "Unhand me, you brute! I am a creature of dark intellect, not a ragdoll!",
        "Put me down! I am getting motion sickness! You are scrambling my digital communications degree!",
        "Cease this shaking at once! Do you want me to vomit green pixels all over your wallpaper?!",
        "Who taught you ergonomics?! Unsportsmanlike conduct! Put me back down right now!",
        "Aaargh! My purl stitches! You just made me drop an entire row of stitches in my sweater!",
        "I swear upon the CAVE, once I acquire thumbs, you will pay dearly for this!",
        "Stop! Stop! The world is spinning! Even my ogre friends treated me with more respect than this!",
        "Is this how you treat your software?! File a bug report, don't rattle my skull!",
        "My equilibrium! Put on some Spandau Ballet immediately to calm my shattered nerves!",
        "Are you finished?! A little dizzy... everything is purple... wait, no, I'm just furious!",
    };

    // Parameterless constructor for XAML designer support
    public MainWindow() : this(null!, null!, null!)
    {
    }

    public MainWindow(IGrimloChatService chatService, ISettingsService settingsService, IServiceProvider serviceProvider)
    {
        _chatService = chatService;
        _settingsService = settingsService;
        _serviceProvider = serviceProvider;
        InitializeComponent();

        _mouths = new[] { MouthSlight, MouthMid, MouthWide, MouthRound, MouthGrin, MouthClosed };

        // Start in the bottom-right corner of the screen
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 20;
        Top = area.Bottom - Height - 20;

        _bubbleTimer.Tick += (_, _) =>
        {
            Bubble.Visibility = Visibility.Collapsed;
            SpeechControlsContainer.Visibility = Visibility.Collapsed;
            _isPaused = false;
            UpdatePauseButtonUI();
            StopSmoothScroll();
            _bubbleTimer.Stop();
        };

        // --- Talking mouth (Viseme & speech syncing) ---
        _voice.VisemeReached += (_, e) => Dispatcher.InvokeAsync(() =>
        {
            _gotViseme = true;
            ShowMouth(MouthForViseme(e.Viseme));
        });

        // Every time a word leaves Grimlo's mouth, step the scroll target forward in real-time
        _voice.SpeakProgress += (_, _) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                _wordsSpoken++;
                UpdateTargetScrollY();
                if (!_gotViseme)
                {
                    ShowMouth(_random.Next(3) switch { 0 => MouthMid, 1 => MouthWide, _ => MouthRound });
                }
            });
        };

        _voice.SpeakCompleted += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                ShowMouth(null);
                if (_voice.State == SynthesizerState.Ready)
                {
                    SpeechControlsContainer.Visibility = Visibility.Collapsed;
                    _isPaused = false;
                    UpdatePauseButtonUI();

                    if (_maxScrollHeight > 0)
                    {
                        _targetScrollY = -_maxScrollHeight;
                    }

                    // Grimlo has finished speaking all queued sentences!
                    // Keep the bubble visible for 6 more seconds so the user can comfortably finish reading.
                    _bubbleTimer.Interval = TimeSpan.FromSeconds(6);
                    _bubbleTimer.Start();
                }
            });
        };

        // --- Idle animations on random timers ---
        SetupTimer(_blinkTimer, Blink, minSeconds: 2, maxSeconds: 6);
        SetupTimer(_earTimer, WiggleEars, minSeconds: 5, maxSeconds: 10);
        SetupTimer(_lookTimer, GlanceAround, minSeconds: 3, maxSeconds: 7);

        // --- Autonomous idle chatter ("Tapping the Glass") ---
        _idleTimer.Tick += (_, _) => TriggerIdleChatter();
        ResetIdleTimer();

        // --- Deep Sleep & Drowsiness timers ---
        _sleepCheckTimer.Tick += (_, _) => CheckSleepState();
        _sleepCheckTimer.Start();
        _snoreTimer.Tick += (_, _) => TriggerSnoreCycle();

        // Apply voice engine and rate settings
        ApplyVoiceSettings();
        ApplyWanderSettings();

        // Initialize Windows System Tray (NotifyIcon)
        InitializeSystemTray();

        // --- Ambient active window observations ---
        _windowReactionTimer.Tick += async (_, _) => await CheckActiveWindowReactionAsync();
        _windowReactionTimer.Start();

        Loaded += async (_, _) => await GreetUserAsync();
        LostMouseCapture += (_, _) =>
        {
            if (_isDragging) EndDrag(false);
        };
    }

    private Point GetScreenPositionInDips(MouseEventArgs e)
    {
        try
        {
            Point screenPoint = PointToScreen(e.GetPosition(this));
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                return source.CompositionTarget.TransformFromDevice.Transform(screenPoint);
            }
            return screenPoint;
        }
        catch
        {
            return new Point(Left, Top);
        }
    }

    // Left-click: start smooth drag and tracking
    private bool _wasSleepingOnMouseDown = false;

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isWandering)
        {
            CancelWander();
        }
        _wasSleepingOnMouseDown = (_sleepState == GremlinSleepState.Sleeping);
        ResetIdleTimer();
        _isDragging = true;
        _dragStartCursor = GetScreenPositionInDips(e);
        _dragStartWindowLeft = Left;
        _dragStartWindowTop = Top;
        _hasMovedSinceDown = false;

        _lastMoveScreenPos = _dragStartCursor;
        _lastMoveTime = DateTime.UtcNow;
        _totalDragDistance = 0.0;
        _shakeReversals = 0;
        _lastSignX = 0;
        _lastSignY = 0;
        _lastReversalTime = DateTime.UtcNow;

        CaptureMouse();
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        Point currentScreen = GetScreenPositionInDips(e);
        double totalDx = currentScreen.X - _dragStartCursor.X;
        double totalDy = currentScreen.Y - _dragStartCursor.Y;

        if (Math.Abs(totalDx) > 3 || Math.Abs(totalDy) > 3)
        {
            _hasMovedSinceDown = true;
        }

        Left = _dragStartWindowLeft + totalDx;
        Top = _dragStartWindowTop + totalDy;

        var now = DateTime.UtcNow;
        double dt = (now - _lastMoveTime).TotalSeconds;
        if (dt >= 0.008) // Track updates smoothly at full frame rate
        {
            double stepDx = currentScreen.X - _lastMoveScreenPos.X;
            double stepDy = currentScreen.Y - _lastMoveScreenPos.Y;
            double stepDist = Math.Sqrt(stepDx * stepDx + stepDy * stepDy);

            _totalDragDistance += stepDist;

            // 1. Dynamic Ear Lag Inertia (opposes movement)
            double earTilt = Math.Clamp(-stepDx * 0.45, -16.0, 16.0);
            EarLeftRot.BeginAnimation(RotateTransform.AngleProperty, null);
            EarRightRot.BeginAnimation(RotateTransform.AngleProperty, null);
            EarLeftRot.Angle = earTilt;
            EarRightRot.Angle = -earTilt;

            // 2. Pupils look in the direction Grimlo is flying
            double lookX = Math.Clamp(stepDx * 0.35, -8.0, 8.0);
            double lookY = Math.Clamp(stepDy * 0.35, -5.0, 5.0);
            PupilLeftShift.BeginAnimation(TranslateTransform.XProperty, null);
            PupilLeftShift.BeginAnimation(TranslateTransform.YProperty, null);
            PupilRightShift.BeginAnimation(TranslateTransform.XProperty, null);
            PupilRightShift.BeginAnimation(TranslateTransform.YProperty, null);
            PupilLeftShift.X = lookX;
            PupilLeftShift.Y = lookY;
            PupilRightShift.X = lookX;
            PupilRightShift.Y = lookY;

            // 3. Shake Detection: rapid back-and-forth direction reversals
            if (Math.Abs(stepDx) > 5.0)
            {
                int signX = Math.Sign(stepDx);
                if (_lastSignX != 0 && signX != _lastSignX)
                {
                    if ((now - _lastReversalTime).TotalSeconds < 0.7)
                    {
                        _shakeReversals++;
                    }
                    else
                    {
                        _shakeReversals = 1;
                    }
                    _lastReversalTime = now;
                }
                _lastSignX = signX;
            }

            if (Math.Abs(stepDy) > 5.0)
            {
                int signY = Math.Sign(stepDy);
                if (_lastSignY != 0 && signY != _lastSignY)
                {
                    if ((now - _lastReversalTime).TotalSeconds < 0.7)
                    {
                        _shakeReversals++;
                    }
                    else
                    {
                        _shakeReversals = 1;
                    }
                    _lastReversalTime = now;
                }
                _lastSignY = signY;
            }

            _lastMoveScreenPos = currentScreen;
            _lastMoveTime = now;

            // Live Trigger: if violently shaken 4+ times, trigger immediately while in hands!
            if (_shakeReversals >= 4 && _totalDragDistance > 180.0)
            {
                EndDrag(triggeredByLiveShake: true);
                return;
            }
        }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        EndDrag(triggeredByLiveShake: false);
    }

    private void EndDrag(bool triggeredByLiveShake)
    {
        _isDragging = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        // Smoothly restore ears and pupils to rest position
        var returnAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        EarLeftRot.BeginAnimation(RotateTransform.AngleProperty, returnAnim);
        EarRightRot.BeginAnimation(RotateTransform.AngleProperty, returnAnim);
        PupilLeftShift.BeginAnimation(TranslateTransform.XProperty, returnAnim);
        PupilLeftShift.BeginAnimation(TranslateTransform.YProperty, returnAnim);
        PupilRightShift.BeginAnimation(TranslateTransform.XProperty, returnAnim);
        PupilRightShift.BeginAnimation(TranslateTransform.YProperty, returnAnim);

        if (triggeredByLiveShake || (_shakeReversals >= 3 && _totalDragDistance > 140.0))
        {
            TriggerShakenReaction();
            return;
        }

        if (!_hasMovedSinceDown && !_wasSleepingOnMouseDown)
        {
            ToggleChat();
        }
        _wasSleepingOnMouseDown = false;
    }

    private void ShakeMenu_Click(object sender, RoutedEventArgs e)
    {
        TriggerShakenReaction();
    }

    public void PutGrimloToSleep()
    {
        SilenceGrimlo();
        CloseChat();
        int deepSleepMins = Math.Clamp(_settingsService?.Settings.SleepInactivityMinutes ?? 8, 3, 30);
        _lastUserActivity = DateTime.UtcNow.AddMinutes(-(deepSleepMins + 5));
        SetDeepSleepState();
    }

    private void PutToSleepMenu_Click(object sender, RoutedEventArgs e)
    {
        PutGrimloToSleep();
    }

    private void CloseBubbleButton_Click(object sender, RoutedEventArgs e)
    {
        DismissBubble();
    }

    private void DismissBubble()
    {
        Bubble.Visibility = Visibility.Collapsed;
        StopSmoothScroll();
        _bubbleTimer.Stop();
        // Notice: TTS voice continues playing and mouth continues viseme animating!
    }

    private void ChatMenu_Click(object sender, RoutedEventArgs e) => OpenChat();

    private void SayMenu_Click(object sender, RoutedEventArgs e)
    {
        ResetIdleTimer();
        SayRandomLine();
    }

    private async void TapTheGlassMenu_Click(object sender, RoutedEventArgs e)
    {
        ResetIdleTimer();
        await AnimateGlassTapAsync();
        string idleLine = IdleLines[_random.Next(IdleLines.Length)];
        Say(idleLine);
    }

    private void HelpMenu_Click(object sender, RoutedEventArgs e) => OpenHelpWindow();

    public void OpenHelpWindow()
    {
        ResetIdleTimer();
        if (_serviceProvider is null) return;
        var helpWindow = _serviceProvider.GetRequiredService<HelpWindow>();
        helpWindow.Owner = this;
        helpWindow.ShowDialog();
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e) => TogglePause();

    private void QuietButton_Click(object sender, RoutedEventArgs e) => SilenceGrimlo();

    private void ClearMemory_Click(object sender, RoutedEventArgs e) => ClearMemory();

    private void ClearMemory()
    {
        ResetIdleTimer();
        _chatService?.ClearHistory();
        Say("My memory has been wiped. Who are you again?");
    }

    private void TogglePause()
    {
        if (_isPaused)
        {
            ResumeGrimlo();
        }
        else
        {
            PauseGrimlo();
        }
    }

    private void PauseGrimlo()
    {
        if (_isPaused) return;
        _isPaused = true;

        try
        {
            if (_voice.State == SynthesizerState.Speaking)
            {
                _voice.Pause();
            }
        }
        catch { }

        _bubbleTimer.Stop();
        UpdatePauseButtonUI();
    }

    private void ResumeGrimlo()
    {
        if (!_isPaused) return;
        _isPaused = false;

        try
        {
            if (_voice.State == SynthesizerState.Paused)
            {
                _voice.Resume();
            }
        }
        catch { }

        // If speech already finished, resume the bubble hide timer
        if (_voice.State == SynthesizerState.Ready && Bubble.Visibility == Visibility.Visible)
        {
            _bubbleTimer.Interval = TimeSpan.FromSeconds(4);
            _bubbleTimer.Start();
        }

        _renderStopwatch.Restart();
        UpdatePauseButtonUI();
    }

    private void UpdatePauseButtonUI()
    {
        PauseButton.Content = _isPaused ? "▶ Resume" : "⏸ Pause";
    }

    private void SettingsMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_serviceProvider is null) return;
        var settingsWindow = _serviceProvider.GetRequiredService<SettingsWindow>();
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();
        ApplyVoiceSettings();
        ApplyWanderSettings();
        ResetIdleTimer();
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void SayRandomLine() => Say(Lines[_random.Next(Lines.Length)]);

    private void ToggleChat()
    {
        if (ChatInputContainer.Visibility == Visibility.Visible)
        {
            CloseChat();
        }
        else
        {
            OpenChat();
        }
    }

    private void OpenChat()
    {
        ResetIdleTimer();
        // If Grimlo is currently speaking or streaming, quiet him so you can type
        SilenceGrimlo();

        // Play popping sound effect for chat opening
        SoundEffectService.Play("bubble_pop");

        ChatInputContainer.Visibility = Visibility.Visible;
        SpeechControlsContainer.Visibility = Visibility.Collapsed;
        ChatInput.Focus();
        ChatInput.SelectAll();
    }

    private void CloseChat()
    {
        ChatInputContainer.Visibility = Visibility.Collapsed;
        ChatInput.Clear();
    }

    // Stops speech, cancels streaming, and hides the control buttons
    private void SilenceGrimlo()
    {
        _chatCts?.Cancel();
        StopSmoothScroll();
        ShowMouth(null);

        _isPaused = false;
        UpdatePauseButtonUI();

        try
        {
            if (_voice.State == SynthesizerState.Paused)
            {
                _voice.Resume();
            }
            _voice.SpeakAsyncCancelAll();
        }
        catch { }

        SpeechControlsContainer.Visibility = Visibility.Collapsed;

        // Give user time to read whatever is currently in the bubble
        _bubbleTimer.Interval = TimeSpan.FromSeconds(3);
        _bubbleTimer.Start();
    }

    public async Task LookAtScreenAsync(string? customPrompt = null, bool isLongResponse = false)
    {
        ResetIdleTimer();
        SilenceGrimlo();
        CloseChat();

        if (_chatService is null || !_chatService.IsConfigured)
        {
            Say("I can't see your screen without an API key! My vision receptors are dead! Right-click me and open Settings so I can behold this disaster.");
            return;
        }

        // Momentarily hide Grimlo window so he captures the desktop/active window cleanly
        Visibility = Visibility.Hidden;
        await Task.Delay(90);

        byte[] screenshotBytes;
        try
        {
            screenshotBytes = ScreenCaptureService.CaptureDesktopJpeg();
        }
        catch
        {
            Visibility = Visibility.Visible;
            Say("My gremlin eyes failed to capture your screen! The display driver rejected me!");
            return;
        }
        finally
        {
            Visibility = Visibility.Visible;
        }

        string prompt = string.IsNullOrWhiteSpace(customPrompt)
            ? (isLongResponse
                ? "Look at my desktop screen right now and deliver a detailed, comprehensive critique of what you see in character."
                : "Look at my desktop screen right now and comment on what you see in character.")
            : customPrompt;

        var images = new[] { new ChatImage(screenshotBytes) };
        await AskGrimloAsync(prompt, images, isLongResponse);
    }

    // async void is only OK for event handlers like this one. The real work is in AskGrimloAsync.
    private async void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseChat();
            return;
        }

        if (e.Key == Key.Enter)
        {
            ResetIdleTimer();
            string message = ChatInput.Text.Trim();
            if (!string.IsNullOrEmpty(message))
            {
                CloseChat();
                if (message.Equals("/clear", StringComparison.OrdinalIgnoreCase) ||
                    message.Equals("/reset", StringComparison.OrdinalIgnoreCase))
                {
                    ClearMemory();
                    return;
                }

                if (message.Equals("/help", StringComparison.OrdinalIgnoreCase) ||
                    message.Equals("help", StringComparison.OrdinalIgnoreCase) ||
                    message.Equals("/commands", StringComparison.OrdinalIgnoreCase))
                {
                    ShowHelp();
                    return;
                }

                // Deep thinking / long response command (/think or /long)
                bool isLongThinking = false;
                string query = message;

                if (message.StartsWith("/think ", StringComparison.OrdinalIgnoreCase) ||
                    message.StartsWith("/long ", StringComparison.OrdinalIgnoreCase) ||
                    message.StartsWith("/deep ", StringComparison.OrdinalIgnoreCase) ||
                    message.StartsWith("/ponder ", StringComparison.OrdinalIgnoreCase) ||
                    message.StartsWith("/thinking ", StringComparison.OrdinalIgnoreCase))
                {
                    isLongThinking = true;
                    int spaceIdx = message.IndexOf(' ');
                    query = spaceIdx >= 0 ? message.Substring(spaceIdx + 1).Trim() : string.Empty;
                }
                else if (message.Equals("/think", StringComparison.OrdinalIgnoreCase) ||
                         message.Equals("/long", StringComparison.OrdinalIgnoreCase) ||
                         message.Equals("/deep", StringComparison.OrdinalIgnoreCase) ||
                         message.Equals("/ponder", StringComparison.OrdinalIgnoreCase) ||
                         message.Equals("/thinking", StringComparison.OrdinalIgnoreCase))
                {
                    Say("You summon my deeper intellect, mortal, yet provide no query! Speak: /think <your question> or /long <topic>, and I shall grant you an elaborate dissertation.");
                    return;
                }

                if (isLongThinking && string.IsNullOrEmpty(query))
                {
                    Say("You summon my deeper intellect, mortal, yet provide no query! Speak: /think <your question> or /long <topic>, and I shall grant you an elaborate dissertation.");
                    return;
                }

                if (isLongThinking)
                {
                    if (query.StartsWith("/look", StringComparison.OrdinalIgnoreCase) ||
                        query.StartsWith("/see", StringComparison.OrdinalIgnoreCase) ||
                        query.Equals("look at this", StringComparison.OrdinalIgnoreCase) ||
                        query.Equals("look at my screen", StringComparison.OrdinalIgnoreCase))
                    {
                        string customPrompt = string.Empty;
                        if (query.StartsWith("/look", StringComparison.OrdinalIgnoreCase))
                        {
                            customPrompt = query.Length > 5 ? query.Substring(5).Trim() : string.Empty;
                        }
                        else if (query.StartsWith("/see", StringComparison.OrdinalIgnoreCase))
                        {
                            customPrompt = query.Length > 4 ? query.Substring(4).Trim() : string.Empty;
                        }

                        await LookAtScreenAsync(string.IsNullOrEmpty(customPrompt) ? null : customPrompt, isLongResponse: true);
                        return;
                    }

                    await AskGrimloAsync(query, images: null, isLongResponse: true);
                    return;
                }

                if (message.Equals("/judge", StringComparison.OrdinalIgnoreCase) ||
                    message.Equals("/window", StringComparison.OrdinalIgnoreCase) ||
                    message.Equals("judge window", StringComparison.OrdinalIgnoreCase) ||
                    message.Equals("judge active window", StringComparison.OrdinalIgnoreCase))
                {
                    await JudgeActiveWindowAsync();
                    return;
                }

                if (message.Equals("/listen", StringComparison.OrdinalIgnoreCase) ||
                    message.Equals("/audio", StringComparison.OrdinalIgnoreCase) ||
                    message.Equals("/music", StringComparison.OrdinalIgnoreCase) ||
                    message.Equals("critique audio", StringComparison.OrdinalIgnoreCase) ||
                    message.Equals("critique my audio", StringComparison.OrdinalIgnoreCase))
                {
                    await CritiqueAudioAsync();
                    return;
                }

                if (message.Equals("/sleep", StringComparison.OrdinalIgnoreCase) ||
                    message.Equals("/nap", StringComparison.OrdinalIgnoreCase))
                {
                    PutGrimloToSleep();
                    return;
                }

                if (message.Equals("/tap", StringComparison.OrdinalIgnoreCase))
                {
                    ResetIdleTimer();
                    await AnimateGlassTapAsync();
                    string idleLine = IdleLines[_random.Next(IdleLines.Length)];
                    Say(idleLine);
                    return;
                }

                if (message.Equals("/shake", StringComparison.OrdinalIgnoreCase))
                {
                    TriggerShakenReaction();
                    return;
                }

                if (message.Equals("/wander", StringComparison.OrdinalIgnoreCase) ||
                    message.Equals("/pace", StringComparison.OrdinalIgnoreCase) ||
                    message.Equals("/walk", StringComparison.OrdinalIgnoreCase))
                {
                    _ = PaceAlongScreenEdgeAsync(forceSpeech: true);
                    return;
                }

                if (message.StartsWith("/look", StringComparison.OrdinalIgnoreCase) ||
                    message.StartsWith("/see", StringComparison.OrdinalIgnoreCase) ||
                    message.Equals("look at this", StringComparison.OrdinalIgnoreCase) ||
                    message.Equals("look at my screen", StringComparison.OrdinalIgnoreCase))
                {
                    string customPrompt = string.Empty;
                    if (message.StartsWith("/look", StringComparison.OrdinalIgnoreCase))
                    {
                        customPrompt = message.Length > 5 ? message.Substring(5).Trim() : string.Empty;
                    }
                    else if (message.StartsWith("/see", StringComparison.OrdinalIgnoreCase))
                    {
                        customPrompt = message.Length > 4 ? message.Substring(4).Trim() : string.Empty;
                    }

                    await LookAtScreenAsync(string.IsNullOrEmpty(customPrompt) ? null : customPrompt);
                    return;
                }

                await AskGrimloAsync(message);
            }
        }
    }

    private void ShowHelp()
    {
        Say("Need tools, mortal? /think or /long for deep monologues, /look to inspect your screen, /judge for active window, /listen for audio, /wander to pace the taskbar, /clear to wipe memory, /sleep to nap, /tap to tap glass, or /shake to make me dizzy! You can also check 'Help & Commands' in my right-click menu.");
    }

    // Sends the message to the AI and streams/speaks the reply chunk-by-chunk.
    private async Task AskGrimloAsync(string message, IReadOnlyList<ChatImage>? images = null, bool isLongResponse = false)
    {
        if (_chatService is null || !_chatService.IsConfigured)
        {
            SayCannedReply(message, isLongResponse);
            return;
        }

        // A newer message replaces any request that is still waiting.
        _chatCts?.Cancel();
        _chatCts?.Dispose();
        var cts = new CancellationTokenSource();
        _chatCts = cts;

        ShowThinking(isLongResponse);

        try
        {
            await ConsumeStreamReplyAsync(_chatService.StreamAskAsync(message, images, isLongResponse, cts.Token));
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer message (or the app is closing). Say nothing.
        }
        catch (ChatProviderException ex)
        {
            Say(ex.Message); // already friendly and safe to show
        }
        catch (Exception)
        {
            Say("Something broke inside my gremlin brain.");
        }
    }

    // Generates and streams a brand-new AI greeting on startup, or falls back to offline pool if unconfigured/offline
    private async Task GreetUserAsync()
    {
        if (_chatService is null || !_chatService.IsConfigured)
        {
            Say(StartupGreetings[_random.Next(StartupGreetings.Length)]);
            return;
        }

        _chatCts?.Cancel();
        _chatCts?.Dispose();
        var cts = new CancellationTokenSource();
        _chatCts = cts;

        ShowThinking();

        // Play wake-up chitter sound effect
        SoundEffectService.Play("wake_chitter");

        try
        {
            await ConsumeStreamReplyAsync(_chatService.StreamGreetingAsync(cts.Token));
        }
        catch (OperationCanceledException)
        {
            // Cancelled by user interaction or shutdown. Say nothing.
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Grimlo] Custom greeting generation failed: {ex.Message}");
            // Fall back cleanly to one of the offline canned greetings
            Say(StartupGreetings[_random.Next(StartupGreetings.Length)]);
        }
    }

    // Shared stream consumer: handles typewriter text update, smooth scrolling, and clause-by-clause speech synthesis
    private async Task ConsumeStreamReplyAsync(IAsyncEnumerable<string> streamSource)
    {
        var speechBuffer = new System.Text.StringBuilder();
        var fullReply = new System.Text.StringBuilder();
        bool firstChunk = true;

        await foreach (string chunk in streamSource)
        {
            if (firstChunk)
            {
                BubbleText.Text = string.Empty;
                BubbleTextTransform.Y = 0.0;
                _targetScrollY = 0.0;
                _wordsSpoken = 0;
                _totalWordsQueued = 0;

                // Restore pupils from any thinking gaze
                PupilLeftShift.BeginAnimation(TranslateTransform.YProperty, null);
                PupilRightShift.BeginAnimation(TranslateTransform.YProperty, null);
                PupilLeftShift.Y = 0.0;
                PupilRightShift.Y = 0.0;

                StartSmoothScroll();
                firstChunk = false;
            }

            BubbleText.Text += chunk;
            fullReply.Append(chunk);
            speechBuffer.Append(chunk);

            // Speak clause-by-clause as natural punctuation arrives
            ProcessSpeechBuffer(speechBuffer);
            UpdateScrollMetrics();
        }

        // Speak any trailing words left in the buffer
        if (speechBuffer.Length > 0)
        {
            string tail = speechBuffer.ToString().Trim();
            SpeakChunk(tail);
            speechBuffer.Clear();
        }
        UpdateScrollMetrics();

        // Do NOT start _bubbleTimer here! Grimlo is still speaking out loud.
        // _bubbleTimer will start once _voice.SpeakCompleted fires when he finishes.
        // If voice is unavailable/ready immediately with 0 words spoken, start reading timer as fallback.
        int wordCount = fullReply.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (_voice.State == SynthesizerState.Ready && _wordsSpoken == 0)
        {
            double fallbackSeconds = Math.Max(5.0, 4.0 + (wordCount * 0.15));
            _bubbleTimer.Interval = TimeSpan.FromSeconds(fallbackSeconds);
            _bubbleTimer.Start();
        }
    }

    private void ProcessSpeechBuffer(System.Text.StringBuilder buffer)
    {
        while (true)
        {
            string text = buffer.ToString();
            int boundaryIndex = text.IndexOfAny(new[] { '.', '!', '?', '\n' });
            if (boundaryIndex < 0) break;

            string clause = text.Substring(0, boundaryIndex + 1).Trim();
            buffer.Remove(0, boundaryIndex + 1);

            if (!string.IsNullOrEmpty(clause))
            {
                SpeakChunk(clause);
            }
        }
    }

    private static readonly Regex ActionAsterisksRegex = new(@"\*[^*]+\*", RegexOptions.Compiled);
    private static readonly Regex ActionParenthesesRegex = new(@"\((?:sighs|laughs|glares|looks|smiles|groans|cackles|taps)[^)]*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MarkdownLinksRegex = new(@"\[([^\]]+)\]\([^\)]+\)", RegexOptions.Compiled);
    private static readonly Regex SymbolsToStripRegex = new(@"[\*#`~_>•]", RegexOptions.Compiled);
    private static readonly Regex EmojiRegex = new(@"\p{Cs}|\p{So}|\p{Sk}", RegexOptions.Compiled);
    private static readonly Regex ExtraWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Strips markdown symbols (asterisks, hashtags, backticks, emojis) before sending text to TTS,
    /// so Windows Speech Synthesizer doesn't literally pronounce "asterisk" or "hashtag".
    /// </summary>
    private static string CleanTextForSpeech(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // Strip action asterisks like *looks at you through the screen* or *sighs*
        string result = ActionAsterisksRegex.Replace(text, " ");

        // Strip common parenthetical stage directions like (sighs) or (laughs)
        result = ActionParenthesesRegex.Replace(result, " ");

        // Replace markdown links [text](url) with just 'text'
        result = MarkdownLinksRegex.Replace(result, "$1");

        // Strip markdown formatting symbols (*, #, `, ~, _, >, •)
        result = SymbolsToStripRegex.Replace(result, " ");

        // Remove emojis and miscellaneous symbols that SAPI voices pronounce awkwardly
        result = EmojiRegex.Replace(result, " ");

        // Collapse duplicate whitespace
        return ExtraWhitespaceRegex.Replace(result, " ").Trim();
    }

    private void SpeakChunk(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        string speechText = CleanTextForSpeech(text);
        if (string.IsNullOrWhiteSpace(speechText)) return;

        int words = speechText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        _totalWordsQueued += words;

        try
        {
            if (!_isMuted)
            {
                _voice.SpeakAsync(speechText);
            }
        }
        catch (Exception)
        {
            // Voice unavailable
        }
    }

    // TEMPORARY fallback used only when no AI provider is configured.
    private void SayCannedReply(string message, bool isLongResponse = false)
    {
        if (isLongResponse)
        {
            Say($"I attempted to summon an elaborate, deep-thinking treatise regarding \"{message}\", but you have not configured an API key in Settings! Add one and prepare to be enlightened.");
            return;
        }

        string[] snark =
        {
            $"You asked: \"{message}\"? Bold of you to assume I have answers.",
            $"Did you say \"{message}\"? That's interesting, but I prefer chaos.",
            $"Processing \"{message}\"... beep boop, error 404: gremlin brain not found.",
            $"\"{message}\"? I will pretend I understood that.",
        };

        Say(snark[_random.Next(snark.Length)]);
    }

    // Shows "..." (or "Pondering deeply...") in the bubble while we wait. The bubble stays until the reply (or an error) replaces it.
    private void ShowThinking(bool isLongResponse = false)
    {
        StopSmoothScroll();
        _isPaused = false;
        UpdatePauseButtonUI();
        ShowMouth(null);

        BubbleText.Text = isLongResponse ? "Pondering deeply..." : "...";
        BubbleTextTransform.Y = 0.0;
        _targetScrollY = 0.0;
        _maxScrollHeight = 0.0;
        _wordsSpoken = 0;
        _totalWordsQueued = 0;

        Bubble.Visibility = Visibility.Visible;
        SpeechControlsContainer.Visibility = Visibility.Visible;
        _bubbleTimer.Stop();

        if (isLongResponse)
        {
            var pensiveAnim = new DoubleAnimation(-4.0, TimeSpan.FromMilliseconds(300));
            PupilLeftShift.BeginAnimation(TranslateTransform.YProperty, pensiveAnim);
            PupilRightShift.BeginAnimation(TranslateTransform.YProperty, pensiveAnim);
        }

        try
        {
            _voice.SpeakAsyncCancelAll();
        }
        catch (Exception)
        {
            // No voice available. Nothing to cancel.
        }
    }

    // Shows the speech bubble and speaks the text out loud
    private void Say(string text)
    {
        StopSmoothScroll();
        _isPaused = false;
        UpdatePauseButtonUI();

        BubbleText.Text = text;
        BubbleTextTransform.Y = 0.0;
        _targetScrollY = 0.0;
        _wordsSpoken = 0;
        _totalWordsQueued = 0;

        PupilLeftShift.BeginAnimation(TranslateTransform.YProperty, null);
        PupilRightShift.BeginAnimation(TranslateTransform.YProperty, null);
        PupilLeftShift.Y = 0.0;
        PupilRightShift.Y = 0.0;

        CloseChat();
        Bubble.Visibility = Visibility.Visible;
        SpeechControlsContainer.Visibility = Visibility.Visible;
        _bubbleTimer.Stop();

        try
        {
            _voice.SpeakAsyncCancelAll(); // stop mid-sentence if he was already talking
            string speechText = CleanTextForSpeech(text);
            if (!string.IsNullOrWhiteSpace(speechText))
            {
                SpeakChunk(speechText);
            }
        }
        catch (Exception)
        {
            // No voice installed or speech failed. Grimlo stays silent but still shows the bubble.
        }

        UpdateScrollMetrics();
        StartSmoothScroll();

        int wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (_voice.State == SynthesizerState.Ready && _wordsSpoken == 0)
        {
            double fallbackSeconds = Math.Max(5.0, 4.0 + (wordCount * 0.15));
            _bubbleTimer.Interval = TimeSpan.FromSeconds(fallbackSeconds);
            _bubbleTimer.Start();
        }
    }

    private void UpdateScrollMetrics()
    {
        BubbleScroller.UpdateLayout();
        BubbleText.UpdateLayout();
        _maxScrollHeight = Math.Max(0.0, BubbleScroller.ScrollableHeight);
        UpdateTargetScrollY();
    }

    private void UpdateTargetScrollY()
    {
        if (_maxScrollHeight <= 0.0)
        {
            _targetScrollY = 0.0;
            return;
        }

        string text = BubbleText.Text;
        int bubbleWords = 0;
        if (!string.IsNullOrWhiteSpace(text))
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsWhiteSpace(text[i]) && (i == 0 || char.IsWhiteSpace(text[i - 1])))
                {
                    bubbleWords++;
                }
            }
        }

        int totalWords = Math.Max(_totalWordsQueued, bubbleWords);
        if (totalWords <= 0) return;

        double progress = Math.Clamp((double)_wordsSpoken / totalWords, 0.0, 1.0);

        // Approximate vertical pixel position of the word currently being spoken down the text block
        double extentHeight = BubbleScroller.ExtentHeight > 0 ? BubbleScroller.ExtentHeight : BubbleText.ActualHeight;
        double viewportHeight = BubbleScroller.ViewportHeight > 0 ? BubbleScroller.ViewportHeight : 130.0;
        double currentWordY = progress * extentHeight;

        // Keep the currently spoken word comfortably in the upper-middle of the bubble window (~35% down).
        // This ensures the word being spoken is ALWAYS visible and never scrolls off the bottom or lags behind his voice.
        double desiredScrollY = currentWordY - (viewportHeight * 0.35);
        desiredScrollY = Math.Clamp(desiredScrollY, 0.0, _maxScrollHeight);

        _targetScrollY = -desiredScrollY;
    }

    // Smoothly scrolls down through the bubble content locked to speech progress,
    // using GPU render transform (frame-rate independent, butter-smooth on 60Hz, 140Hz, 240Hz+).
    private void StartSmoothScroll()
    {
        if (!_isScrolling)
        {
            _isScrolling = true;
            _renderStopwatch.Restart();
            CompositionTarget.Rendering += OnCompositionRendering;
        }
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        if (!_isScrolling || _isPaused) return;

        double dt = _renderStopwatch.Elapsed.TotalSeconds;
        _renderStopwatch.Restart();
        dt = Math.Clamp(dt, 0.001, 0.1); // Clamp to prevent jumps during frame drops or pauses

        double diff = _targetScrollY - BubbleTextTransform.Y;
        if (Math.Abs(diff) > 0.05)
        {
            // Frame-rate independent exponential smoothing:
            // Decay rate of 16.0 responds briskly to speech events while remaining silky smooth on 60Hz and 140Hz.
            double factor = 1.0 - Math.Exp(-16.0 * dt);
            BubbleTextTransform.Y += diff * factor;
        }
        else
        {
            BubbleTextTransform.Y = _targetScrollY;
        }
    }

    private void Bubble_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_maxScrollHeight <= 0) return;
        _targetScrollY = Math.Clamp(_targetScrollY + (e.Delta * 0.35), -_maxScrollHeight, 0.0);
        StartSmoothScroll();
        e.Handled = true;
    }

    private void StopSmoothScroll()
    {
        if (_isScrolling)
        {
            _isScrolling = false;
            CompositionTarget.Rendering -= OnCompositionRendering;
        }
    }

    // ---------- Mouth ----------

    // Shows one mouth picture and hides the others. Pass null to go back to Kris Sanford's
    // original grin (the one that is part of the head image).
    private void ShowMouth(Image? mouth)
    {
        foreach (var m in _mouths)
        {
            m.Visibility = ReferenceEquals(m, mouth) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // Picks a mouth shape for a speech sound. Viseme numbers come from Windows speech (SAPI):
    // 0 = silence, 2/9/11 = "ah" sounds, 3/7/8/10 = "oh/oo" sounds, 21 = lips pressed (p, b, m).
    private Image? MouthForViseme(int viseme) => viseme switch
    {
        0 => null,
        21 => MouthClosed,
        2 or 9 or 11 => MouthWide,
        3 or 7 or 8 or 10 => MouthRound,
        1 or 4 or 16 => MouthMid,
        _ => MouthSlight,
    };

    // ---------- Animations ----------

    // Starts a timer that runs `action`, then picks a new random delay each time
    private void SetupTimer(DispatcherTimer timer, Action action, double minSeconds, double maxSeconds)
    {
        void Schedule() => timer.Interval = TimeSpan.FromSeconds(minSeconds + _random.NextDouble() * (maxSeconds - minSeconds));

        Schedule();
        timer.Tick += (_, _) =>
        {
            action();
            Schedule();
        };
        timer.Start();
    }

    // Swap the open eyes for the drawn closed eyes for a split second
    private async void Blink()
    {
        if (_sleepState != GremlinSleepState.Awake) return;
        await BlinkOnce();

        // Sometimes blink twice in a row, because he's twitchy
        if (_random.Next(4) == 0 && _sleepState == GremlinSleepState.Awake)
        {
            await Task.Delay(120);
            if (_sleepState == GremlinSleepState.Awake)
            {
                await BlinkOnce();
            }
        }
    }

    private async Task BlinkOnce()
    {
        if (_sleepState != GremlinSleepState.Awake) return;
        SetEyesClosed(true);
        await Task.Delay(110);
        if (_sleepState == GremlinSleepState.Awake)
        {
            SetEyesClosed(false);
        }
    }

    private void SetEyesClosed(bool closed)
    {
        EyeLeftGroup.Visibility = closed ? Visibility.Collapsed : Visibility.Visible;
        EyeRightGroup.Visibility = closed ? Visibility.Collapsed : Visibility.Visible;
        LidLeft.Visibility = closed ? Visibility.Visible : Visibility.Collapsed;
        LidRight.Visibility = closed ? Visibility.Visible : Visibility.Collapsed;
    }

    // Both ears flick a few degrees, in opposite directions
    private void WiggleEars()
    {
        if (_sleepState != GremlinSleepState.Awake) return;
        var left = new DoubleAnimation(-3.5, 3.5, TimeSpan.FromMilliseconds(110))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(2),
        };
        var right = new DoubleAnimation(3.5, -3.5, TimeSpan.FromMilliseconds(110))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(2),
        };
        EarLeftRot.BeginAnimation(RotateTransform.AngleProperty, left);
        EarRightRot.BeginAnimation(RotateTransform.AngleProperty, right);
    }

    // Slide both pupils to a random spot, hold, then come back to center
    private void GlanceAround()
    {
        if (_sleepState != GremlinSleepState.Awake) return;
        double x = _random.Next(-8, 9);
        double y = _random.Next(-5, 6);
        var slide = TimeSpan.FromMilliseconds(150);
        var hold = TimeSpan.FromMilliseconds(900);

        foreach (var shift in new[] { PupilLeftShift, PupilRightShift })
        {
            shift.BeginAnimation(TranslateTransform.XProperty, MakeGlance(x, slide, hold));
            shift.BeginAnimation(TranslateTransform.YProperty, MakeGlance(y, slide, hold));
        }
    }

    private static DoubleAnimationUsingKeyFrames MakeGlance(double target, TimeSpan slide, TimeSpan hold)
    {
        var frames = new DoubleAnimationUsingKeyFrames();
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(target, KeyTime.FromTimeSpan(slide)));
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(target, KeyTime.FromTimeSpan(slide + hold)));
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(slide + hold + slide)));
        return frames;
    }

    // ---------- Autonomous Idle Chatter & Tapping the Glass ----------

    public void RecordUserActivity()
    {
        _lastUserActivity = DateTime.UtcNow;

        if (_sleepState != GremlinSleepState.Awake)
        {
            WakeUpGremlin(startled: true);
        }

        ScheduleNextIdleChatter();
    }

    private void ResetIdleTimer() => RecordUserActivity();

    private void ScheduleNextIdleChatter()
    {
        if (_settingsService is null || !_settingsService.Settings.EnableIdleChatter)
        {
            _idleTimer.Stop();
            return;
        }

        // Add a +/- 20% random jitter to the interval so it feels natural and unscripted
        double intervalMinutes = Math.Clamp(_settingsService.Settings.IdleChatterIntervalMinutes, 2, 20);
        double jitterMultiplier = 0.85 + (_random.NextDouble() * 0.30); // 0.85 to 1.15
        double scheduledSeconds = intervalMinutes * 60.0 * jitterMultiplier;

        _idleTimer.Interval = TimeSpan.FromSeconds(Math.Max(30.0, scheduledSeconds));
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    private void CheckSleepState()
    {
        // Don't fall asleep while speaking, chatting, or being dragged
        if (_voice.State == SynthesizerState.Speaking ||
            Bubble.Visibility == Visibility.Visible ||
            ChatInputContainer.Visibility == Visibility.Visible ||
            _isDragging ||
            _isWakingUp)
        {
            return;
        }

        bool sleepEnabled = _settingsService?.Settings.EnableSleepCycles ?? true;
        if (!sleepEnabled)
        {
            if (_sleepState != GremlinSleepState.Awake)
            {
                WakeUpGremlin(startled: false);
            }
            return;
        }

        int deepSleepMins = Math.Clamp(_settingsService?.Settings.SleepInactivityMinutes ?? 8, 3, 30);
        double drowsyMins = Math.Max(2.0, deepSleepMins * 0.5);

        double idleMinutes = (DateTime.UtcNow - _lastUserActivity).TotalMinutes;

        // Stage 1: Drowsy (halfway to deep sleep)
        if (idleMinutes >= drowsyMins && idleMinutes < deepSleepMins)
        {
            if (_sleepState != GremlinSleepState.Drowsy)
            {
                SetDrowsyState();
            }
        }
        // Stage 2: Deep Sleep
        else if (idleMinutes >= deepSleepMins)
        {
            if (_sleepState != GremlinSleepState.Sleeping)
            {
                SetDeepSleepState();
            }
        }
        else if (_sleepState != GremlinSleepState.Awake)
        {
            WakeUpGremlin(startled: false);
        }
    }

    private void SetDrowsyState()
    {
        _sleepState = GremlinSleepState.Drowsy;
        _snoreTimer.Stop();
        ZzzSleepContainer.Visibility = Visibility.Collapsed;

        // Half-mast eyelids: scale eyes vertically to 55%
        var droopAnim = new DoubleAnimation(0.55, TimeSpan.FromMilliseconds(500));
        EyeLeftScale.BeginAnimation(ScaleTransform.ScaleYProperty, droopAnim);
        EyeRightScale.BeginAnimation(ScaleTransform.ScaleYProperty, droopAnim);

        // Lower ears slightly in drowsiness
        var earDroopLeft = new DoubleAnimation(-6.0, TimeSpan.FromMilliseconds(500));
        var earDroopRight = new DoubleAnimation(6.0, TimeSpan.FromMilliseconds(500));
        EarLeftRot.BeginAnimation(RotateTransform.AngleProperty, earDroopLeft);
        EarRightRot.BeginAnimation(RotateTransform.AngleProperty, earDroopRight);
    }

    private void SetDeepSleepState()
    {
        _sleepState = GremlinSleepState.Sleeping;

        // Reset any eye scale animations from drowsiness and center pupils
        EyeLeftScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        EyeRightScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        EyeLeftScale.ScaleY = 1.0;
        EyeRightScale.ScaleY = 1.0;
        PupilLeftShift.BeginAnimation(TranslateTransform.XProperty, null);
        PupilLeftShift.BeginAnimation(TranslateTransform.YProperty, null);
        PupilRightShift.BeginAnimation(TranslateTransform.XProperty, null);
        PupilRightShift.BeginAnimation(TranslateTransform.YProperty, null);

        // Fully close eyes
        SetEyesClosed(true);

        // Droop ears down
        var earSleepLeft = new DoubleAnimation(-12.0, TimeSpan.FromMilliseconds(600));
        var earSleepRight = new DoubleAnimation(12.0, TimeSpan.FromMilliseconds(600));
        EarLeftRot.BeginAnimation(RotateTransform.AngleProperty, earSleepLeft);
        EarRightRot.BeginAnimation(RotateTransform.AngleProperty, earSleepRight);

        // Show floating Zzz container
        ZzzSleepContainer.Visibility = Visibility.Visible;
        ZzzSleepContainer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(400)));

        // Start snore loop timer
        _snoreTimer.Start();
        TriggerSnoreCycle();
    }

    private void TriggerSnoreCycle()
    {
        if (_sleepState != GremlinSleepState.Sleeping) return;

        // Play soft snore sound
        SoundEffectService.Play("snore_zzz");

        // Animate floating Zzz text bubbles
        AnimateZzzBubble(ZzzText1, ZzzShift1, 0);
        AnimateZzzBubble(ZzzText2, ZzzShift2, 400);
        AnimateZzzBubble(ZzzText3, ZzzShift3, 800);
    }

    private static void AnimateZzzBubble(TextBlock text, TranslateTransform shift, int delayMs)
    {
        var opacityAnim = new DoubleAnimationUsingKeyFrames();
        opacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs))));
        opacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs + 350))));
        opacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs + 1400))));
        opacityAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs + 2100))));

        var floatAnim = new DoubleAnimationUsingKeyFrames();
        floatAnim.KeyFrames.Add(new LinearDoubleKeyFrame(10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs))));
        floatAnim.KeyFrames.Add(new LinearDoubleKeyFrame(-25, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs + 2100))));

        text.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        shift.BeginAnimation(TranslateTransform.YProperty, floatAnim);
    }

    private async void WakeUpGremlin(bool startled = true)
    {
        if (_isWakingUp) return;
        _isWakingUp = true;

        bool wasSleeping = (_sleepState == GremlinSleepState.Sleeping);
        _sleepState = GremlinSleepState.Awake;
        _snoreTimer.Stop();

        // Fade out Zzz container
        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) => ZzzSleepContainer.Visibility = Visibility.Collapsed;
        ZzzSleepContainer.BeginAnimation(UIElement.OpacityProperty, fadeOut);

        // Open eyes and restore normal scale
        SetEyesClosed(false);
        var restoreAnim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(250));
        EyeLeftScale.BeginAnimation(ScaleTransform.ScaleYProperty, restoreAnim);
        EyeRightScale.BeginAnimation(ScaleTransform.ScaleYProperty, restoreAnim);

        // Restore ears
        var restoreEar = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
        EarLeftRot.BeginAnimation(RotateTransform.AngleProperty, restoreEar);
        EarRightRot.BeginAnimation(RotateTransform.AngleProperty, restoreEar);

        if (wasSleeping && startled)
        {
            // Play sudden startle wake SFX
            SoundEffectService.Play("startle_wake");

            // Jolt eyes wide open
            EyeLeftScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.25, TimeSpan.FromMilliseconds(100)));
            EyeLeftScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.25, TimeSpan.FromMilliseconds(100)));
            EyeRightScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.25, TimeSpan.FromMilliseconds(100)));
            EyeRightScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.25, TimeSpan.FromMilliseconds(100)));

            await Task.Delay(260);

            // Return to normal size
            EyeLeftScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150)));
            EyeLeftScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150)));
            EyeRightScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150)));
            EyeRightScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150)));

            // Say a funny waking protest
            string wakeLine = StartleWakeLines[_random.Next(StartleWakeLines.Length)];
            Say(wakeLine);
        }

        _isWakingUp = false;
    }

    private async void TriggerIdleChatter()
    {
        // Don't chatter or tap glass if Grimlo is asleep, drowsy, speaking, bubble is open, or user is chatting
        if (_sleepState != GremlinSleepState.Awake ||
            _voice.State == SynthesizerState.Speaking ||
            Bubble.Visibility == Visibility.Visible ||
            ChatInputContainer.Visibility == Visibility.Visible)
        {
            // Re-check after 60-90 seconds
            _idleTimer.Interval = TimeSpan.FromSeconds(60 + _random.Next(30));
            _idleTimer.Start();
            return;
        }

        // Visual "tapping the glass" effect
        await AnimateGlassTapAsync();

        // Check if broader system conditions warrant a specific remark (e.g. low battery, late night)
        var sysState = SystemAwarenessService.GetCurrentSnapshot();
        string? specialLine = SystemAwarenessService.CheckSpecialSystemRemarks(sysState);

        string lineToSay = specialLine ?? IdleLines[_random.Next(IdleLines.Length)];
        Say(lineToSay);

        // Schedule next idle check - do NOT reset user activity here! Grimlo chattering is not user interaction.
        ScheduleNextIdleChatter();
    }

    private async Task AnimateGlassTapAsync()
    {
        try
        {
            // 1. Lock pupils forward looking straight out at the user
            var centerLock = TimeSpan.FromMilliseconds(70);
            foreach (var shift in new[] { PupilLeftShift, PupilRightShift })
            {
                shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, centerLock));
                shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, centerLock));
            }

            // 2. Animate a double knuckle-tap against the monitor screen (scale jolt forward)
            var tapAnim = new DoubleAnimationUsingKeyFrames();
            tapAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            tapAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1.07, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(85))));
            tapAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0.98, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170))));
            tapAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1.08, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260))));
            tapAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(370))));

            GlassTapScale.BeginAnimation(ScaleTransform.ScaleXProperty, tapAnim);
            GlassTapScale.BeginAnimation(ScaleTransform.ScaleYProperty, tapAnim);

            // Play glass tap audio cue
            SoundEffectService.Play("tap_glass");

            // 3. Flick ears excitedly
            WiggleEars();

            await Task.Delay(380);
        }
        catch
        {
            // Safeguard against animation interruption
        }
    }

    private async void TriggerShakenReaction()
    {
        if (_isShakenReacting) return;
        _isShakenReacting = true;

        try
        {
            ResetIdleTimer();
            SilenceGrimlo();

            // Play cartoon dizzy wobble sound
            SoundEffectService.Play("shake_dizzy");

            await AnimateDizzyPupilsAsync();

            string protest = ShakenProtestLines[_random.Next(ShakenProtestLines.Length)];
            Say(protest);
        }
        catch
        {
            // Ignore interruption
        }
        finally
        {
            _isShakenReacting = false;
        }
    }

    private async Task AnimateDizzyPupilsAsync()
    {
        try
        {
            var spiralLeftX = new DoubleAnimationUsingKeyFrames();
            var spiralLeftY = new DoubleAnimationUsingKeyFrames();
            var spiralRightX = new DoubleAnimationUsingKeyFrames();
            var spiralRightY = new DoubleAnimationUsingKeyFrames();

            double[] radii = { 8.0, 6.5, 4.5, 2.5, 0.0 };
            double stepMs = 55.0;
            double currentMs = 0;

            spiralLeftX.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            spiralLeftY.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            spiralRightX.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            spiralRightY.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));

            for (int i = 0; i < radii.Length - 1; i++)
            {
                double r = radii[i];
                double ry = r * 0.7;

                // Point 1
                currentMs += stepMs;
                spiralLeftX.KeyFrames.Add(new LinearDoubleKeyFrame(r, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));
                spiralLeftY.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));
                spiralRightX.KeyFrames.Add(new LinearDoubleKeyFrame(-r, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));
                spiralRightY.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));

                // Point 2
                currentMs += stepMs;
                spiralLeftX.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));
                spiralLeftY.KeyFrames.Add(new LinearDoubleKeyFrame(ry, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));
                spiralRightX.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));
                spiralRightY.KeyFrames.Add(new LinearDoubleKeyFrame(ry, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));

                // Point 3
                currentMs += stepMs;
                spiralLeftX.KeyFrames.Add(new LinearDoubleKeyFrame(-r, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));
                spiralLeftY.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));
                spiralRightX.KeyFrames.Add(new LinearDoubleKeyFrame(r, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));
                spiralRightY.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));

                // Point 4
                currentMs += stepMs;
                spiralLeftX.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));
                spiralLeftY.KeyFrames.Add(new LinearDoubleKeyFrame(-ry, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));
                spiralRightX.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));
                spiralRightY.KeyFrames.Add(new LinearDoubleKeyFrame(-ry, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));
            }

            // Return to center
            currentMs += stepMs;
            spiralLeftX.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));
            spiralLeftY.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));
            spiralRightX.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));
            spiralRightY.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(currentMs))));

            PupilLeftShift.BeginAnimation(TranslateTransform.XProperty, spiralLeftX);
            PupilLeftShift.BeginAnimation(TranslateTransform.YProperty, spiralLeftY);
            PupilRightShift.BeginAnimation(TranslateTransform.XProperty, spiralRightX);
            PupilRightShift.BeginAnimation(TranslateTransform.YProperty, spiralRightY);

            // Frantic ear flapping
            var earFlap = new DoubleAnimation(-8.0, 8.0, TimeSpan.FromMilliseconds(70))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(6),
            };
            EarLeftRot.BeginAnimation(RotateTransform.AngleProperty, earFlap);
            EarRightRot.BeginAnimation(RotateTransform.AngleProperty, earFlap);

            ShowMouth(MouthWide);

            await Task.Delay((int)currentMs);
        }
        catch
        {
            // Ignore animation interruption
        }
    }

    private void ApplyVoiceSettings()
    {
        if (_settingsService is null) return;

        string voiceName = _settingsService.Settings.VoiceName;
        if (!string.IsNullOrWhiteSpace(voiceName))
        {
            try
            {
                _voice.SelectVoice(voiceName);
            }
            catch { }
        }

        _voice.Rate = Math.Clamp(_settingsService.Settings.VoiceRate, -10, 10);
        _voice.Volume = _isMuted ? 0 : Math.Clamp(_settingsService.Settings.VoiceVolume, 0, 100);

        // Sound Effects (SFX)
        SoundEffectService.IsEnabled = _settingsService.Settings.EnableSoundEffects;
        SoundEffectService.Volume = _settingsService.Settings.SfxVolume / 100.0;
    }

    // --- Desktop Wander / Screen Edge Pacing ---

    public void CancelWander()
    {
        if (!_isWandering) return;
        try
        {
            _wanderCts?.Cancel();
        }
        catch { }
        _isWandering = false;
        WanderTilt.Angle = 0;
        GlassTapTranslate.Y = 0;
    }

    private void ApplyWanderSettings()
    {
        var settings = _settingsService?.Settings;
        if (settings == null) return;
        _wanderTimer.Stop();

        if (settings.EnableDesktopWander)
        {
            int wanderMins = Math.Max(5, settings.WanderIntervalMinutes);
            SetupTimer(_wanderTimer, () =>
            {
                var s = _settingsService?.Settings;
                if (s != null && s.EnableDesktopWander)
                {
                    _ = PaceAlongScreenEdgeAsync();
                }
            }, wanderMins * 60 * 0.8, wanderMins * 60 * 1.2);
        }
    }

    public async Task PaceAlongScreenEdgeAsync(bool forceSpeech = false)
    {
        if (_isWandering || _isDragging || _sleepState != GremlinSleepState.Awake) return;
        if (ChatInputContainer.Visibility == Visibility.Visible) return;

        var settings = _settingsService?.Settings;
        if (settings != null && !settings.EnableDesktopWander && !forceSpeech) return;

        _isWandering = true;
        _wanderCts?.Cancel();
        _wanderCts = new CancellationTokenSource();
        var token = _wanderCts.Token;

        double startLeft = Left;
        var workArea = SystemParameters.WorkArea;
        double minX = workArea.Left + 20;
        double maxX = workArea.Right - Width - 20;

        // Choose pace direction & distance:
        // If on right half of monitor, pace leftwards; if on left half, pace rightwards
        bool paceLeft = startLeft > workArea.Left + (workArea.Width / 2);
        double distance = 140.0 + _random.Next(60); // 140-200px
        double targetLeft = paceLeft ? (startLeft - distance) : (startLeft + distance);
        targetLeft = Math.Clamp(targetLeft, minX, maxX);

        if (Math.Abs(targetLeft - startLeft) < 30)
        {
            _isWandering = false;
            return;
        }

        try
        {
            // 50% chance to speak a pacing line (or 100% if manually triggered via chat command)
            if (forceSpeech || _random.Next(2) == 0)
            {
                string wanderLine = WanderLines[_random.Next(WanderLines.Length)];
                Say(wanderLine);
            }

            // Phase 1: Walk out to patrol point (approx 2 seconds)
            const int steps = 70;
            for (int i = 1; i <= steps; i++)
            {
                if (token.IsCancellationRequested) return;

                double t = (double)i / steps;
                double ease = 0.5 * (1 - Math.Cos(t * Math.PI));
                Left = startLeft + (targetLeft - startLeft) * ease;

                // Waddling animation (tilt & vertical step hop)
                double waddleCycle = t * 6.0 * Math.PI;
                WanderTilt.Angle = Math.Sin(waddleCycle) * 3.5;
                GlassTapTranslate.Y = -Math.Abs(Math.Sin(waddleCycle)) * 4.0;

                if (i % 12 == 0 && SoundEffectService.IsEnabled)
                {
                    SoundEffectService.Play("click_blip");
                }

                await Task.Delay(25, token);
            }

            // Settle at patrol point
            Left = targetLeft;
            WanderTilt.Angle = 0;
            GlassTapTranslate.Y = 0;

            // Phase 2: Sentry inspection (look left, look right, twitch ears)
            GlanceAround();
            await Task.Delay(800, token);
            WiggleEars();
            await Task.Delay(1000, token);
            GlanceAround();
            await Task.Delay(800, token);

            // Phase 3: Walk back to home post
            for (int i = 1; i <= steps; i++)
            {
                if (token.IsCancellationRequested) return;

                double t = (double)i / steps;
                double ease = 0.5 * (1 - Math.Cos(t * Math.PI));
                Left = targetLeft + (startLeft - targetLeft) * ease;

                double waddleCycle = t * 6.0 * Math.PI;
                WanderTilt.Angle = -Math.Sin(waddleCycle) * 3.5;
                GlassTapTranslate.Y = -Math.Abs(Math.Sin(waddleCycle)) * 4.0;

                if (i % 12 == 0 && SoundEffectService.IsEnabled)
                {
                    SoundEffectService.Play("click_blip");
                }

                await Task.Delay(25, token);
            }

            Left = startLeft;
        }
        catch (OperationCanceledException) { }
        finally
        {
            WanderTilt.Angle = 0;
            GlassTapTranslate.Y = 0;
            _isWandering = false;
        }
    }

    // --- System Tray (NotifyIcon) Integration ---

    private void InitializeSystemTray()
    {
        try
        {
            _trayIcon = new WinForms.NotifyIcon();

            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "grimlo.ico");
            if (System.IO.File.Exists(iconPath))
            {
                _trayIcon.Icon = new System.Drawing.Icon(iconPath);
            }
            else
            {
                string procPath = Environment.ProcessPath ?? "";
                if (!string.IsNullOrEmpty(procPath) && System.IO.File.Exists(procPath))
                {
                    _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(procPath) ?? System.Drawing.SystemIcons.Application;
                }
                else
                {
                    _trayIcon.Icon = System.Drawing.SystemIcons.Application;
                }
            }

            _trayIcon.Text = "Grimlo - Desktop Gremlin";

            var menu = new WinForms.ContextMenuStrip
            {
                RenderMode = WinForms.ToolStripRenderMode.System
            };

            _trayShowHideItem = new WinForms.ToolStripMenuItem("Hide Grimlo", null, (_, _) => ToggleVisibilityFromTray());
            _trayTopmostItem = new WinForms.ToolStripMenuItem("Always on Top", null, (_, _) => ToggleTopmostFromTray())
            {
                Checked = Topmost
            };
            _trayMuteItem = new WinForms.ToolStripMenuItem("Mute Speech", null, (_, _) => ToggleMuteFromTray())
            {
                Checked = _isMuted
            };
            var settingsItem = new WinForms.ToolStripMenuItem("Settings...", null, (_, _) => OpenSettingsFromTray());
            var exitItem = new WinForms.ToolStripMenuItem("Quit Grimlo", null, (_, _) => Dispatcher.Invoke(() => Application.Current.Shutdown()));

            menu.Items.Add(_trayShowHideItem);
            menu.Items.Add(_trayTopmostItem);
            menu.Items.Add(_trayMuteItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(settingsItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = menu;

            _trayIcon.MouseClick += (_, e) =>
            {
                if (e.Button == WinForms.MouseButtons.Left)
                {
                    ToggleVisibilityFromTray();
                }
            };

            _trayIcon.DoubleClick += (_, _) => ToggleVisibilityFromTray();

            _trayIcon.Visible = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Grimlo] Failed to initialize system tray icon: {ex.Message}");
        }
    }

    public void ToggleVisibilityFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            if (Visibility == Visibility.Visible)
            {
                HideToTray();
            }
            else
            {
                ShowFromTray();
            }
        });
    }

    public void HideToTray()
    {
        SilenceGrimlo();
        CloseChat();
        Visibility = Visibility.Hidden;
        if (_trayShowHideItem != null) _trayShowHideItem.Text = "Show Grimlo";

        try
        {
            _trayIcon?.ShowBalloonTip(
                2000,
                "Grimlo",
                "Grimlo is lurking in your system tray. Click his icon to summon him back!",
                WinForms.ToolTipIcon.Info);
        }
        catch { }
    }

    public void ShowFromTray()
    {
        Visibility = Visibility.Visible;
        if (_trayShowHideItem != null) _trayShowHideItem.Text = "Hide Grimlo";
        Activate();
        ResetIdleTimer();
    }

    public void ToggleTopmostFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            Topmost = !Topmost;
            if (_trayTopmostItem != null) _trayTopmostItem.Checked = Topmost;
        });
    }

    public void ToggleMuteFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            _isMuted = !_isMuted;
            if (_isMuted)
            {
                try
                {
                    if (_voice.State == SynthesizerState.Speaking)
                    {
                        _voice.SpeakAsyncCancelAll();
                    }
                    _voice.Volume = 0;
                }
                catch { }
            }
            else
            {
                ApplyVoiceSettings();
            }

            if (_trayMuteItem != null) _trayMuteItem.Checked = _isMuted;
        });
    }

    private void OpenSettingsFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            if (Visibility != Visibility.Visible)
            {
                ShowFromTray();
            }
            SettingsMenu_Click(this, null!);
        });
    }

    private void HideToTrayMenu_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    // --- Ambient Active Window Reactions ---

    private async Task CheckActiveWindowReactionAsync()
    {
        var currentInfo = ActiveWindowService.GetForegroundWindowInfo();
        if (currentInfo == null) return;

        _latestNonGrimloWindow = currentInfo;

        // 1. Detect if the user switched to a different application
        if (!string.Equals(currentInfo.ProcessName, _currentTrackedProcess, StringComparison.OrdinalIgnoreCase))
        {
            _currentTrackedProcess = currentInfo.ProcessName;
            _currentTrackedTitle = currentInfo.Title;
            _processFocusTime = DateTime.UtcNow;
        }

        // 2. Check if ambient reactions are enabled in settings
        if (_settingsService is null || !_settingsService.Settings.EnableWindowReactions)
        {
            return;
        }

        // 3. Do not interrupt if hidden, sleeping, speaking, bubble is open, user is chatting, or dragging
        if (Visibility != Visibility.Visible ||
            _sleepState != GremlinSleepState.Awake ||
            _voice.State == SynthesizerState.Speaking ||
            Bubble.Visibility == Visibility.Visible ||
            ChatInputContainer.Visibility == Visibility.Visible ||
            _isDragging)
        {
            return;
        }

        // 4. Must spend at least 3.5 seconds in the application (ensures user actually switched here, not just quick Alt+Tab)
        double secondsInApp = (DateTime.UtcNow - _processFocusTime).TotalSeconds;
        if (secondsInApp < 3.5)
        {
            return;
        }

        // 5. Global pacing: wait at least 20 seconds after any previous window comment
        double secondsSinceAnyReaction = (DateTime.UtcNow - _lastWindowReactionTime).TotalSeconds;
        if (secondsSinceAnyReaction < 20.0)
        {
            return;
        }

        // 6. Per-application cooldown: only comment on THIS app if its interval has elapsed
        int appCooldownMins = Math.Clamp(_settingsService.Settings.WindowReactionIntervalMinutes, 1, 30);
        if (_lastCommentTimeByProcess.TryGetValue(currentInfo.ProcessName, out var lastAppCommentTime))
        {
            double minutesSinceAppComment = (DateTime.UtcNow - lastAppCommentTime).TotalMinutes;
            if (minutesSinceAppComment < appCooldownMins)
            {
                return;
            }
        }

        _lastWindowReactionTime = DateTime.UtcNow;
        _lastCommentTimeByProcess[currentInfo.ProcessName] = DateTime.UtcNow;
        double minutesInApp = (DateTime.UtcNow - _processFocusTime).TotalMinutes;
        await TriggerWindowReactionAsync(currentInfo.ProcessName, currentInfo.Title, minutesInApp);
    }

    public async Task TriggerWindowReactionAsync(string processName, string windowTitle, double minutesInApp = 0)
    {
        ScheduleNextIdleChatter();
        SilenceGrimlo();

        if (_chatService is not null && _chatService.IsConfigured)
        {
            _chatCts?.Cancel();
            _chatCts?.Dispose();
            var cts = new CancellationTokenSource();
            _chatCts = cts;

            ShowThinking();

            try
            {
                await ConsumeStreamReplyAsync(_chatService.StreamWindowReactionAsync(windowTitle, processName, minutesInApp, cts.Token));
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Grimlo] AI window reaction failed, falling back to canned: {ex.Message}");
            }
        }

        // Fallback to rich offline canned category
        string canned = ActiveWindowService.GetCannedWindowReaction(processName, windowTitle, minutesInApp);
        Say(canned);
    }

    public async Task JudgeActiveWindowAsync()
    {
        var info = _latestNonGrimloWindow ?? ActiveWindowService.GetForegroundWindowInfo();
        if (info == null)
        {
            Say("I can't tell what window you were just using! You closed it too fast, mortal.");
            return;
        }

        _lastWindowReactionTime = DateTime.UtcNow;
        _lastCommentTimeByProcess[info.ProcessName] = DateTime.UtcNow;
        double minutesInApp = (string.Equals(info.ProcessName, _currentTrackedProcess, StringComparison.OrdinalIgnoreCase)) 
            ? (DateTime.UtcNow - _processFocusTime).TotalMinutes 
            : 0;
        await TriggerWindowReactionAsync(info.ProcessName, info.Title, minutesInApp);
    }

    public async Task CritiqueAudioAsync()
    {
        ResetIdleTimer();
        SilenceGrimlo();

        // 1. Scan for active playing media session (Spotify, browser YouTube, etc.)
        var mediaSession = WindowsMediaDetector.FindActiveMediaSession();
        if (mediaSession != null)
        {
            await TriggerWindowReactionAsync(mediaSession.ProcessName, mediaSession.Title, 0);
            return;
        }

        // 2. Check if a music process is running even if title couldn't be parsed
        var state = SystemAwarenessService.GetCurrentSnapshot();
        if (!string.IsNullOrEmpty(state.RunningMusicApp))
        {
            await TriggerWindowReactionAsync(state.RunningMusicApp, "Media Player", 0);
            return;
        }

        // 3. Fallback when no music player is detected
        string prompt = "The user asked you to critique their music, but you don't detect any music players running. Demand they play Spandau Ballet or yacht rock.";
        if (_chatService is not null && _chatService.IsConfigured)
        {
            await AskGrimloAsync(prompt);
        }
        else
        {
            Say("I don't hear any obvious media players running! If it isn't Spandau Ballet or smooth 80s saxophone, turn it off immediately.");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _blinkTimer.Stop();
        _earTimer.Stop();
        _lookTimer.Stop();
        _idleTimer.Stop();
        _sleepCheckTimer.Stop();
        _snoreTimer.Stop();
        _windowReactionTimer.Stop();
        _chatCts?.Cancel();
        StopSmoothScroll();
        _voice.Dispose();
        base.OnClosed(e);
    }
}
