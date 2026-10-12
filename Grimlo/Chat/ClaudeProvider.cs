using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Grimlo.Chat;

/// <summary>Talks to Anthropic's Claude "Messages" API.</summary>
public class ClaudeProvider : IChatProvider
{
    public const string DefaultModel = "claude-sonnet-4-5";

    private readonly HttpClient _http;
    private readonly string _model;

    public ClaudeProvider(HttpClient http, string? model = null)
    {
        _http = http;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
    }

    public string Name => "Claude";

    public async IAsyncEnumerable<string> StreamReplyAsync(
        IReadOnlyList<ChatMessage> messages,
        string apiKey,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        int maxTokens = 150)
    {
        var claudeMessages = new List<object>();
        string? systemPrompt = null;

        foreach (var msg in messages)
        {
            if (string.IsNullOrWhiteSpace(msg.Content) && (msg.Images == null || msg.Images.Count == 0)) continue;

            if (msg.Role == ChatRole.System)
            {
                systemPrompt = msg.Content;
                continue;
            }

            string role = msg.Role == ChatRole.Assistant ? "assistant" : "user";

            if (msg.Images != null && msg.Images.Count > 0)
            {
                var contentBlocks = new List<object>();
                foreach (var img in msg.Images)
                {
                    contentBlocks.Add(new
                    {
                        type = "image",
                        source = new
                        {
                            type = "base64",
                            media_type = img.MimeType,
                            data = Convert.ToBase64String(img.Data)
                        }
                    });
                }
                if (!string.IsNullOrWhiteSpace(msg.Content))
                {
                    contentBlocks.Add(new
                    {
                        type = "text",
                        text = msg.Content
                    });
                }
                claudeMessages.Add(new { role, content = contentBlocks });
            }
            else
            {
                claudeMessages.Add(new
                {
                    role,
                    content = msg.Content
                });
            }
        }

        if (claudeMessages.Count == 0)
        {
            claudeMessages.Add(new { role = "user", content = "Hello" });
        }

        var bodyDict = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["max_tokens"] = maxTokens,
            ["stream"] = true,
            ["messages"] = claudeMessages
        };

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            bodyDict["system"] = systemPrompt;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(JsonSerializer.Serialize(bodyDict), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await ChatHttp.SendForStreamAsync(_http, request, Name, cancellationToken);
        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (line.StartsWith("data: "))
            {
                string json = line.Substring(6).Trim();
                if (string.IsNullOrEmpty(json) || json == "[DONE]") continue;

                JsonDocument? doc = null;
                try
                {
                    doc = JsonDocument.Parse(json);
                }
                catch (JsonException)
                {
                    continue;
                }

                using (doc)
                {
                    // Delta shape: { "type": "content_block_delta", "delta": { "type": "text_delta", "text": "..." } }
                    if (doc.RootElement.TryGetProperty("delta", out JsonElement delta) &&
                        delta.TryGetProperty("text", out JsonElement textElem))
                    {
                        string? text = textElem.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            yield return text;
                        }
                    }
                }
            }
        }
    }

    public async Task<string> GetReplyAsync(
        IReadOnlyList<ChatMessage> messages,
        string apiKey,
        CancellationToken cancellationToken = default,
        int maxTokens = 150)
    {
        var sb = new StringBuilder();
        await foreach (string chunk in StreamReplyAsync(messages, apiKey, cancellationToken, maxTokens))
        {
            sb.Append(chunk);
        }

        if (sb.Length == 0)
        {
            throw new ChatProviderException($"{Name} gave me an empty answer.");
        }

        return sb.ToString().Trim();
    }
}
