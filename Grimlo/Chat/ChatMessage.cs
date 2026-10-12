using System.Collections.Generic;

namespace Grimlo.Chat;

/// <summary>
/// Defines who sent a message in the conversation.
/// </summary>
public enum ChatRole
{
    User,
    Assistant,
    System
}

/// <summary>
/// Represents an image attached to a chat message (e.g. desktop screenshot).
/// </summary>
public record ChatImage(byte[] Data, string MimeType = "image/jpeg");

/// <summary>
/// Represents a single turn in a multi-turn conversation.
/// </summary>
public record ChatMessage(ChatRole Role, string Content, IReadOnlyList<ChatImage>? Images = null);
