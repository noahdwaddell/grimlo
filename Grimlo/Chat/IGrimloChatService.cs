using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Grimlo.Chat;

/// <summary>
/// High-level chat service for Grimlo. Coordinates provider selection,
/// conversation history memory, API credentials, and query execution.
/// </summary>
public interface IGrimloChatService
{
    /// <summary>True if an API key is currently present to handle requests.</summary>
    bool IsConfigured { get; }

    /// <summary>Active multi-turn conversation history remembered by Grimlo.</summary>
    IReadOnlyList<ChatMessage> History { get; }

    /// <summary>Clears the current conversation memory.</summary>
    void ClearHistory();

    /// <summary>Sends the user's message with active conversation history to the provider and returns the reply.</summary>
    Task<string> AskAsync(string message, IReadOnlyList<ChatImage>? images = null, bool isLongResponse = false, CancellationToken cancellationToken = default);

    /// <summary>Sends the user's message with active conversation history to the provider and streams the reply chunks.</summary>
    IAsyncEnumerable<string> StreamAskAsync(string message, IReadOnlyList<ChatImage>? images = null, bool isLongResponse = false, CancellationToken cancellationToken = default);

    /// <summary>Generates and streams a fresh, custom AI opening greeting on startup.</summary>
    IAsyncEnumerable<string> StreamGreetingAsync(CancellationToken cancellationToken = default);

    /// <summary>Generates and streams an ambient reaction/comment regarding the user's active window.</summary>
    IAsyncEnumerable<string> StreamWindowReactionAsync(string windowTitle, string processName, double minutesInApp = 0, CancellationToken cancellationToken = default);
}
