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

/// <summary>Talks to Google's Gemini "generateContent" / "streamGenerateContent" API.</summary>
public class GeminiProvider : IChatProvider
{
    // Latest active fast model for Google AI Studio / Gemini API (near-instant responses)
    public const string DefaultModel = "gemini-flash-lite-latest";

    private readonly HttpClient _http;
    private readonly string _model;

    public GeminiProvider(HttpClient http, string? model = null)
    {
        _http = http;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
    }

    public string Name => "Gemini";

    public async IAsyncEnumerable<string> StreamReplyAsync(
        IReadOnlyList<ChatMessage> messages,
        string apiKey,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        int maxTokens = 150)
    {
        var contentsList = new List<object>();
        object? systemInstruction = null;

        foreach (var msg in messages)
        {
            if (string.IsNullOrWhiteSpace(msg.Content) && (msg.Images == null || msg.Images.Count == 0)) continue;

            if (msg.Role == ChatRole.System)
            {
                systemInstruction = new { parts = new[] { new { text = msg.Content } } };
                continue;
            }

            string role = msg.Role == ChatRole.Assistant ? "model" : "user";
            var parts = new List<object>();

            if (msg.Images != null && msg.Images.Count > 0)
            {
                foreach (var img in msg.Images)
                {
                    parts.Add(new
                    {
                        inline_data = new
                        {
                            mime_type = img.MimeType,
                            data = Convert.ToBase64String(img.Data)
                        }
                    });
                }
            }

            if (!string.IsNullOrWhiteSpace(msg.Content))
            {
                parts.Add(new { text = msg.Content });
            }

            if (parts.Count > 0)
            {
                contentsList.Add(new
                {
                    role,
                    parts = parts.ToArray()
                });
            }
        }

        if (contentsList.Count == 0)
        {
            contentsList.Add(new
            {
                role = "user",
                parts = new[] { new { text = "Hello" } }
            });
        }

        object generationConfig;
        if (maxTokens > 150 && (_model.Contains("3.8", StringComparison.OrdinalIgnoreCase) ||
                                _model.Contains("2.5", StringComparison.OrdinalIgnoreCase) ||
                                _model.Contains("thinking", StringComparison.OrdinalIgnoreCase)))
        {
            generationConfig = new
            {
                maxOutputTokens = maxTokens,
                thinkingConfig = new { thinkingBudget = 1024 }
            };
        }
        else
        {
            generationConfig = new { maxOutputTokens = maxTokens };
        }

        object body = systemInstruction is not null
            ? new { system_instruction = systemInstruction, contents = contentsList, generationConfig }
            : new { contents = contentsList, generationConfig };

        // If the model already has "models/", don't duplicate it.
        string modelPath = _model.StartsWith("models/") ? _model : $"models/{_model}";
        string url = $"https://generativelanguage.googleapis.com/v1beta/{modelPath}:streamGenerateContent?alt=sse";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

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
                    if (doc.RootElement.TryGetProperty("candidates", out JsonElement candidates) &&
                        candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0 &&
                        candidates[0].TryGetProperty("content", out JsonElement content) &&
                        content.TryGetProperty("parts", out JsonElement parts) && parts.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement part in parts.EnumerateArray())
                        {
                            bool isThought = part.TryGetProperty("thought", out JsonElement th) && th.ValueKind == JsonValueKind.True;
                            if (!isThought && part.TryGetProperty("text", out JsonElement t))
                            {
                                string? text = t.GetString();
                                if (!string.IsNullOrEmpty(text))
                                {
                                    yield return text;
                                }
                            }
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
            throw new ChatProviderException($"{Name} gave me an empty answer (maybe its safety filter blocked it).");
        }

        return sb.ToString().Trim();
    }
}
