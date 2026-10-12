using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Grimlo.Settings;

namespace Grimlo.Chat;

/// <summary>
/// Default implementation of IGrimloChatService.
/// Uses IHttpClientFactory for resilient socket/DNS handling,
/// maintains multi-turn conversation memory, and reads credentials from ISettingsService.
/// </summary>
public class GrimloChatService : IGrimloChatService
{
    public const int MaxHistoryMessages = 20;

    private readonly IHttpClientFactory _httpFactory;
    private readonly ISettingsService _settingsService;
    private readonly List<ChatMessage> _history = new();
    private readonly object _lock = new();

    public GrimloChatService(IHttpClientFactory httpFactory, ISettingsService settingsService)
    {
        _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public bool IsConfigured
    {
        get
        {
            string provider = _settingsService.Settings.ActiveProvider;
            return _settingsService.HasApiKey(provider);
        }
    }

    public IReadOnlyList<ChatMessage> History
    {
        get
        {
            lock (_lock)
            {
                return _history.ToList();
            }
        }
    }

    public void ClearHistory()
    {
        lock (_lock)
        {
            _history.Clear();
        }
    }

    private (IChatProvider Provider, string ApiKey) ResolveProvider()
    {
        string providerName = _settingsService.Settings.ActiveProvider.Trim().ToLowerInvariant();
        string? apiKey = _settingsService.GetApiKey(providerName);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ChatProviderException($"I don't have an API key for {providerName.ToUpperInvariant()} yet. Right-click me and open Settings to add one!");
        }

        string? model = providerName switch
        {
            "claude" => _settingsService.Settings.ClaudeModel,
            "openai" => _settingsService.Settings.OpenAiModel,
            _ => _settingsService.Settings.GeminiModel,
        };

        // Allow temporary environment variable override if one was explicitly passed
        string? envModel = Environment.GetEnvironmentVariable("GRIMLO_MODEL");
        if (!string.IsNullOrWhiteSpace(envModel))
        {
            model = envModel;
        }

        HttpClient http = _httpFactory.CreateClient("GrimloChat");

        IChatProvider provider = providerName switch
        {
            "claude" => new ClaudeProvider(http, model),
            "openai" => new OpenAiProvider(http, model),
            _ => new GeminiProvider(http, model), // Defaults to Gemini
        };

        return (provider, apiKey);
    }

    public async Task<string> AskAsync(
        string message,
        IReadOnlyList<ChatImage>? images = null,
        bool isLongResponse = false,
        CancellationToken cancellationToken = default)
    {
        var (provider, apiKey) = ResolveProvider();

        List<ChatMessage> snapshot;
        lock (_lock)
        {
            _history.Add(new ChatMessage(ChatRole.User, message, images));
            while (_history.Count > MaxHistoryMessages)
            {
                _history.RemoveAt(0);
            }
            snapshot = _history.ToList();
        }

        string systemPrompt = GrimloPersonality.BuildSystemPrompt(_settingsService.Settings, isLongResponse);

        var messagesToSend = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, systemPrompt)
        };

        // Only include images on the latest turn to save bandwidth and token budget
        for (int i = 0; i < snapshot.Count; i++)
        {
            var msg = snapshot[i];
            if (i < snapshot.Count - 1 && msg.Images != null && msg.Images.Count > 0)
            {
                messagesToSend.Add(new ChatMessage(msg.Role, msg.Content, null));
            }
            else
            {
                messagesToSend.Add(msg);
            }
        }

        try
        {
            int maxTokens = isLongResponse ? 2048 : 150;
            string reply = await provider.GetReplyAsync(messagesToSend, apiKey, cancellationToken, maxTokens);
            lock (_lock)
            {
                _history.Add(new ChatMessage(ChatRole.Assistant, reply));
                while (_history.Count > MaxHistoryMessages)
                {
                    _history.RemoveAt(0);
                }
            }
            return reply;
        }
        catch
        {
            // If request failed, remove unanswered user message so history isn't broken
            lock (_lock)
            {
                if (_history.Count > 0 && _history[^1].Role == ChatRole.User)
                {
                    _history.RemoveAt(_history.Count - 1);
                }
            }
            throw;
        }
    }

    public async IAsyncEnumerable<string> StreamAskAsync(
        string message,
        IReadOnlyList<ChatImage>? images = null,
        bool isLongResponse = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (provider, apiKey) = ResolveProvider();

        List<ChatMessage> snapshot;
        lock (_lock)
        {
            _history.Add(new ChatMessage(ChatRole.User, message, images));
            while (_history.Count > MaxHistoryMessages)
            {
                _history.RemoveAt(0);
            }
            snapshot = _history.ToList();
        }

        string systemPrompt = GrimloPersonality.BuildSystemPrompt(_settingsService.Settings, isLongResponse);

        var messagesToSend = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, systemPrompt)
        };

        // Only include images on the latest turn to save bandwidth and token budget
        for (int i = 0; i < snapshot.Count; i++)
        {
            var msg = snapshot[i];
            if (i < snapshot.Count - 1 && msg.Images != null && msg.Images.Count > 0)
            {
                messagesToSend.Add(new ChatMessage(msg.Role, msg.Content, null));
            }
            else
            {
                messagesToSend.Add(msg);
            }
        }

        var fullReply = new StringBuilder();
        bool failed = false;

        IAsyncEnumerator<string> enumerator;
        try
        {
            int maxTokens = isLongResponse ? 2048 : 150;
            enumerator = provider.StreamReplyAsync(messagesToSend, apiKey, cancellationToken, maxTokens).GetAsyncEnumerator(cancellationToken);
        }
        catch
        {
            lock (_lock)
            {
                if (_history.Count > 0 && _history[^1].Role == ChatRole.User)
                {
                    _history.RemoveAt(_history.Count - 1);
                }
            }
            throw;
        }

        try
        {
            while (true)
            {
                string chunk;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        break;
                    }
                    chunk = enumerator.Current;
                }
                catch
                {
                    failed = true;
                    throw;
                }

                fullReply.Append(chunk);
                yield return chunk;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();

            if (failed || cancellationToken.IsCancellationRequested)
            {
                lock (_lock)
                {
                    if (_history.Count > 0 && _history[^1].Role == ChatRole.User)
                    {
                        _history.RemoveAt(_history.Count - 1);
                    }
                }
            }
            else if (fullReply.Length > 0)
            {
                lock (_lock)
                {
                    _history.Add(new ChatMessage(ChatRole.Assistant, fullReply.ToString()));
                    while (_history.Count > MaxHistoryMessages)
                    {
                        _history.RemoveAt(0);
                    }
                }
            }
        }
    }

    private static readonly string[] GreetingAngles =
    {
        "You just woke up and materialized behind the monitor glass on the user's desktop. Make a dry, bizarre observation about their desktop icons, cursor, or the computer hardware.",
        "You just booted up into Windows after a strange dream about Swedish fish or your ogre friends in the CAVE. Comment on waking up trapped here.",
        "You just appeared on screen. Deliver a grand, dramatic villainous threat against the user or their files, but instantly derail yourself with a non-sequitur or pathetic deflation.",
        "You just materialized on the user's desktop screen. Make an unabashed, deadpan alt-humor observation about human biology, knees, or why they are staring at a screen.",
        "You just booted up and notice complete silence. Demand they put on Spandau Ballet or yacht rock to calm your existential dread.",
        "You just woke up behind the screen and your stitch tension on a knitted sweater is completely ruined. Complain to the user.",
        "You just materialized on the monitor screen. Lament your lack of administrator privileges and physical hands in an articulate, weary manner.",
        "You just appeared on the desktop. Solicit immediate flattery or compliments from the user to restore your delicate ego."
    };

    public async IAsyncEnumerable<string> StreamGreetingAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (provider, apiKey) = ResolveProvider();

        string angle = GreetingAngles[Random.Shared.Next(GreetingAngles.Length)];
        string greetingPrompt =
            $"[Startup Greeting Context: {angle}] " +
            "Deliver a brand new, punchy opening greeting to the user in 1 to 2 short sentences. " +
            "Direct spoken dialogue only. Strictly NO asterisks, NO stage directions, NO roleplay emotes.";

        string systemPrompt = GrimloPersonality.BuildSystemPrompt(_settingsService.Settings);

        var messagesToSend = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, greetingPrompt)
        };

        var fullReply = new StringBuilder();
        bool failed = false;

        IAsyncEnumerator<string> enumerator;
        try
        {
            enumerator = provider.StreamReplyAsync(messagesToSend, apiKey, cancellationToken).GetAsyncEnumerator(cancellationToken);
        }
        catch
        {
            throw;
        }

        try
        {
            while (true)
            {
                string chunk;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        break;
                    }
                    chunk = enumerator.Current;
                }
                catch
                {
                    failed = true;
                    throw;
                }

                fullReply.Append(chunk);
                yield return chunk;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();

            if (!failed && !cancellationToken.IsCancellationRequested && fullReply.Length > 0)
            {
                lock (_lock)
                {
                    _history.Clear();
                    _history.Add(new ChatMessage(ChatRole.User, "[Grimlo boots up on the desktop]"));
                    _history.Add(new ChatMessage(ChatRole.Assistant, fullReply.ToString()));
                }
            }
        }
    }

    public async IAsyncEnumerable<string> StreamWindowReactionAsync(
        string windowTitle,
        string processName,
        double minutesInApp = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (provider, apiKey) = ResolveProvider();

        string appLabel = !string.IsNullOrWhiteSpace(processName) ? processName : "application";
        string titleLabel = !string.IsNullOrWhiteSpace(windowTitle) ? $" (Window title: '{windowTitle}')" : string.Empty;

        string contextIntro = minutesInApp >= 3.0
            ? $"[Ambient Reaction Context: The user has been working continuously inside '{appLabel}'{titleLabel} for about {Math.Round(minutesInApp)} minutes.] "
            : $"[Ambient Reaction Context: You glance out through the monitor glass and notice the user is actively using '{appLabel}'{titleLabel}.] ";

        string instruction = minutesInApp >= 3.0
            ? "Make a fresh, new 1 to 2 sentence observation or critique about their long marathon session, their posture, staring at the screen, or a surreal tangent. Do NOT repeat anything you said previously. "
            : "Make a quick, punchy, 1 to 2 sentence observation or critique about what they are doing. ";

        string prompt =
            contextIntro +
            instruction +
            "Stay in character (escaped CAVE fantasy gremlin, internet alt-humor, dry absurdist observation, or melodramatic bluster). " +
            "Direct spoken dialogue only. Strictly NO asterisks, NO stage directions, and do NOT repeat catchphrases like 'visual hierarchy'.";

        List<ChatMessage> snapshot;
        lock (_lock)
        {
            _history.Add(new ChatMessage(ChatRole.User, minutesInApp >= 3.0
                ? $"[User is still active in {appLabel}{titleLabel} ({Math.Round(minutesInApp)} mins)]"
                : $"[User switched to {appLabel}{titleLabel}]"));

            while (_history.Count > MaxHistoryMessages)
            {
                _history.RemoveAt(0);
            }
            snapshot = _history.ToList();
        }

        string systemPrompt = GrimloPersonality.BuildSystemPrompt(_settingsService.Settings);

        var messagesToSend = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, systemPrompt)
        };

        // Include conversation history so the model knows what it previously said and avoids repetition
        for (int i = 0; i < snapshot.Count - 1; i++)
        {
            messagesToSend.Add(new ChatMessage(snapshot[i].Role, snapshot[i].Content, null));
        }
        messagesToSend.Add(new ChatMessage(ChatRole.User, prompt));

        var fullReply = new StringBuilder();
        bool failed = false;

        IAsyncEnumerator<string> enumerator;
        try
        {
            enumerator = provider.StreamReplyAsync(messagesToSend, apiKey, cancellationToken).GetAsyncEnumerator(cancellationToken);
        }
        catch
        {
            lock (_lock)
            {
                if (_history.Count > 0 && _history[^1].Role == ChatRole.User)
                {
                    _history.RemoveAt(_history.Count - 1);
                }
            }
            throw;
        }

        try
        {
            while (true)
            {
                string chunk;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        break;
                    }
                    chunk = enumerator.Current;
                }
                catch
                {
                    failed = true;
                    throw;
                }

                fullReply.Append(chunk);
                yield return chunk;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();

            if (failed || cancellationToken.IsCancellationRequested)
            {
                lock (_lock)
                {
                    if (_history.Count > 0 && _history[^1].Role == ChatRole.User)
                    {
                        _history.RemoveAt(_history.Count - 1);
                    }
                }
            }
            else if (fullReply.Length > 0)
            {
                lock (_lock)
                {
                    _history.Add(new ChatMessage(ChatRole.Assistant, fullReply.ToString()));
                    while (_history.Count > MaxHistoryMessages)
                    {
                        _history.RemoveAt(0);
                    }
                }
            }
        }
    }
}
