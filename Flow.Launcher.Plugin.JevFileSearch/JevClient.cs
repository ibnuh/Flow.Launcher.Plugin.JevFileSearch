using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin.JevFileSearch
{
    /// <summary>Thin HTTP client for the TypeSafe System One endpoint. Every call is one fan-out request.</summary>
    public static class JevClient
    {
        public const string Endpoint = "https://api.typesafe.ai/v1/systemone";
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(4);

        private static readonly HttpClient Http = CreateHttpClient();

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Flow.Launcher.Plugin.JevFileSearch/0.1");
            return client;
        }

        public sealed class AskResult
        {
            public JevResponse Response;
            public double LatencyMs;
        }

        /// <summary>`TYPESAFE_API_KEY` from the environment first, then the plugin Settings field.</summary>
        public static string ResolveApiKey(Settings settings)
        {
            var env = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
            if (!string.IsNullOrWhiteSpace(env))
                return env.Trim();
            if (settings != null && !string.IsNullOrWhiteSpace(settings.ApiKey))
                return settings.ApiKey.Trim();
            return null;
        }

        public static async Task<AskResult> AskAsync(JevRequest request, string apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("TypeSafe API key is not set.");

            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                linked.CancelAfter(RequestTimeout);
                string json = JsonSerializer.Serialize(request, JsonOptions);
                using (var message = new HttpRequestMessage(HttpMethod.Post, Endpoint))
                {
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    message.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    var sw = Stopwatch.StartNew();
                    using (var response = await Http.SendAsync(message, linked.Token).ConfigureAwait(false))
                    {
                        sw.Stop();
                        if ((int)response.StatusCode != 200)
                            throw new HttpRequestException("Jev returned HTTP " + (int)response.StatusCode);
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var decoded = JsonSerializer.Deserialize<JevResponse>(body, JsonOptions);
                        if (decoded == null)
                            throw new InvalidOperationException("Could not parse Jev response.");
                        return new AskResult { Response = decoded, LatencyMs = sw.Elapsed.TotalMilliseconds };
                    }
                }
            }
        }
    }
}
