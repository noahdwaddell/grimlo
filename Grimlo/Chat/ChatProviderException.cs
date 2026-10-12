using System;

namespace Grimlo.Chat;

/// <summary>
/// Thrown when a provider can't get a reply. The Message is written for the user
/// (it never contains the API key or raw server responses), so the UI can show it directly.
/// </summary>
public class ChatProviderException : Exception
{
    public ChatProviderException(string message) : base(message)
    {
    }
}
