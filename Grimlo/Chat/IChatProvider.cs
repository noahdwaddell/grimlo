using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Grimlo.Chat;

/// <summary>
/// Anything that can turn a conversation history into a reply from an AI.
/// Claude, Gemini and OpenAI each implement this, so the rest of the app
/// never needs to know which one it is talking to.
/// </summary>
public interface IChatProvider
{
    /// <summary>Friendly name, used in error messages ("Claude", "Gemini", ...).</summary>
    string Name { get; }

    /// <summary>
    /// Sends a multi-turn conversation and returns the AI's reply as plain text.
    /// </summary>
    Task<string> GetReplyAsync(IReadOnlyList<ChatMessage> messages, string apiKey, CancellationToken cancellationToken = default, int maxTokens = 150);

    /// <summary>
    /// Sends a multi-turn conversation and streams the AI's reply text chunk-by-chunk.
    /// </summary>
    IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> messages, string apiKey, CancellationToken cancellationToken = default, int maxTokens = 150);

    /// <summary>Convenience overload for sending a single user message.</summary>
    Task<string> GetReplyAsync(string userMessage, string apiKey, CancellationToken cancellationToken = default, int maxTokens = 150)
        => GetReplyAsync(new[] { new ChatMessage(ChatRole.User, userMessage) }, apiKey, cancellationToken, maxTokens);

    /// <summary>Convenience overload for streaming a single user message.</summary>
    IAsyncEnumerable<string> StreamReplyAsync(string userMessage, string apiKey, CancellationToken cancellationToken = default, int maxTokens = 150)
        => StreamReplyAsync(new[] { new ChatMessage(ChatRole.User, userMessage) }, apiKey, cancellationToken, maxTokens);
}
