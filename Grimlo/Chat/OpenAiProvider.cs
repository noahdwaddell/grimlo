using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Grimlo.Chat;

/// <summary>Talks to OpenAI's "chat completions" API.</summary>
public class OpenAiProvider : IChatProvider
{
    public const string DefaultModel = "gpt-4o-mini";

    private readonly HttpClient _http;
    private readonly string _model;

    public OpenAiProvider(HttpClient http, string? model = null)
    {
        _http = http;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
    }

    public string Name => "OpenAI";

    public async IAsyncEnumerable<string> StreamReplyAsync(
        IReadOnlyList<ChatMessage> messages,
        string apiKey,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        int maxTokens = 150)
    {
        var openAiMessages = new List<object>();

        foreach (var msg in messages)
        {
            if (string.IsNullOrWhiteSpace(msg.Content) && (msg.Images == null || msg.Images.Count == 0)) continue;

            string role = msg.Role switch
            {
                ChatRole.Assistant => "assistant",
                ChatRole.System => "system",
                _ => "user"
            };

            if (msg.Images != null && msg.Images.Count > 0)
            {
                var contentBlocks = new List<object>();
                if (!string.IsNullOrWhiteSpace(msg.Content))
                {
                    contentBlocks.Add(new
                    {
                        type = "text",
                        text = msg.Content
                    });
                }
                foreach (var img in msg.Images)
                {
                    contentBlocks.Add(new
                    {
                        type = "image_url",
                        image_url = new
                        {
                            url = $"data:{img.MimeType};base64,{Convert.ToBase64String(img.Data)}",
                            detail = "low"
                        }
                    });
                }
                openAiMessages.Add(new { role, content = contentBlocks });
            }
            else
            {
                openAiMessages.Add(new
                {
                    role,
                    content = msg.Content
                });
            }
        }

        if (openAiMessages.Count == 0)
        {
            openAiMessages.Add(new { role = "user", content = "Hello" });
        }

        var body = new
        {
            model = _model,
            stream = true,
            max_tokens = maxTokens,
            messages = openAiMessages,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
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
                    // Delta shape: { "choices": [ { "delta": { "content": "..." } } ] }
                    if (doc.RootElement.TryGetProperty("choices", out JsonElement choices) &&
                        choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0 &&
                        choices[0].TryGetProperty("delta", out JsonElement delta) &&
                        delta.TryGetProperty("content", out JsonElement contentElem) &&
                        contentElem.ValueKind == JsonValueKind.String)
                    {
                        string? text = contentElem.GetString();
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
