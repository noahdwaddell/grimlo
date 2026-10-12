using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Grimlo.Chat;

/// <summary>
/// The sending + error handling that all three providers share, so it only lives in one place.
/// </summary>
internal static class ChatHttp
{
    /// <summary>
    /// Sends the request and returns the parsed JSON reply.
    /// Turns every failure into a ChatProviderException with a friendly message.
    /// </summary>
    public static async Task<JsonDocument> SendForJsonAsync(
        HttpClient http,
        HttpRequestMessage request,
        string providerName,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new ChatProviderException($"I couldn't reach {providerName}. Is your internet down?");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The token was NOT cancelled, so this is the HttpClient timeout, not the user.
            throw new ChatProviderException($"{providerName} took too long to answer.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new ChatProviderException(DescribeError(providerName, response.StatusCode));
            }

            try
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                return JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                throw new ChatProviderException($"{providerName} sent back something I couldn't read.");
            }
        }
    }

    /// <summary>
    /// Sends a streaming request (ResponseHeadersRead) and validates the status code.
    /// Returns the live HttpResponseMessage for line-by-line streaming.
    /// </summary>
    public static async Task<HttpResponseMessage> SendForStreamAsync(
        HttpClient http,
        HttpRequestMessage request,
        string providerName,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new ChatProviderException($"I couldn't reach {providerName}. Is your internet down?");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatProviderException($"{providerName} took too long to answer.");
        }

        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                throw new ChatProviderException(DescribeError(providerName, response.StatusCode));
            }
        }

        return response;
    }

    // Note: we deliberately do NOT include the server's response body here. It can echo request
    // details, and we never want an API key (or anything private) ending up in a speech bubble.
    private static string DescribeError(string providerName, HttpStatusCode status, string? errorBody = null)
    {
        int code = (int)status;
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"{providerName} didn't accept my API key. Check that it's correct.",
            HttpStatusCode.NotFound =>
                $"{providerName} says it doesn't know that model. The model name may be out of date.",
            HttpStatusCode.TooManyRequests =>
                $"{providerName} says I'm talking too much (rate limit or out of credit).",
            >= HttpStatusCode.InternalServerError =>
                $"{providerName} is having a bad day (error {code}). Try again later.",
            _ => $"{providerName} returned an error (HTTP {code}).",
        };
    }
}
