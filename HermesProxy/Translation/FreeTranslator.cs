using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Framework.Logging;

namespace HermesProxy.Translation
{
    // Keyless translation backends, ported from the 1.12 DLL's translator_core.cpp:
    // gtx -> tw-ob -> dict-chrome-ex -> edge-translate, in that order. A backend that
    // answers HTTP 429 is skipped for a cool-down period instead of being hammered.
    public sealed class FreeTranslator
    {
        public static readonly string[] BackendNames = { "gtx", "tw-ob", "dict-chrome-ex", "edge-translate" };

        static readonly TimeSpan RateLimitCooldown = TimeSpan.FromSeconds(60);
        static readonly TimeSpan EdgeTokenLifetime = TimeSpan.FromMinutes(8);

        readonly HttpClient _http;
        volatile string _pinnedBackend; // null = auto chain; switchable live from the addon
        readonly SemaphoreSlim _concurrency = new(4);
        readonly Dictionary<string, DateTime> _cooldownUntil = new();
        readonly object _lock = new();

        string _edgeToken;
        DateTime _edgeTokenExpires;

        public string LastError { get; private set; } = "";
        public string LastBackend { get; private set; } = "";

        public FreeTranslator(string backend, TimeSpan requestTimeout)
        {
            backend = (backend ?? "").Trim().ToLowerInvariant();
            _pinnedBackend = Array.IndexOf(BackendNames, backend) >= 0 ? backend : null;

            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            };
            _http = new HttpClient(handler) { Timeout = requestTimeout };
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        }

        public string Mode => _pinnedBackend ?? "auto";

        // "auto" or one of BackendNames. Clears the rate-limit pauses so the new choice is tried at once.
        public bool SetBackend(string backend)
        {
            backend = (backend ?? "").Trim().ToLowerInvariant();
            if (backend != "auto" && Array.IndexOf(BackendNames, backend) < 0)
                return false;
            _pinnedBackend = backend == "auto" ? null : backend;
            lock (_lock)
                _cooldownUntil.Clear();
            return true;
        }

        // sourceLang/targetLang: "zh" or "en". Returns null on failure (LastError says why).
        public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken token)
        {
            await _concurrency.WaitAsync(token).ConfigureAwait(false);
            try
            {
                string pinned = _pinnedBackend;
                foreach (string backend in BackendNames)
                {
                    if (pinned != null && backend != pinned)
                        continue;
                    if (IsCoolingDown(backend))
                        continue;

                    try
                    {
                        string result = await CallBackendAsync(backend, text, sourceLang, targetLang, token).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(result))
                        {
                            LastBackend = backend;
                            LastError = "";
                            return result.Trim();
                        }
                        LastError = backend + ": empty translation";
                    }
                    catch (RateLimitedException)
                    {
                        LastError = backend + ": rate limited (HTTP 429)";
                        lock (_lock)
                            _cooldownUntil[backend] = DateTime.UtcNow + RateLimitCooldown;
                        Log.Print(LogType.Warn, $"[Translate] {backend} rate limited, pausing it for {RateLimitCooldown.TotalSeconds}s");
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        LastError = backend + ": " + ex.Message;
                        Log.Print(LogType.Debug, $"[Translate] {backend} failed: {ex.Message}");
                    }
                }
                return null;
            }
            finally
            {
                _concurrency.Release();
            }
        }

        bool IsCoolingDown(string backend)
        {
            lock (_lock)
                return _cooldownUntil.TryGetValue(backend, out var until) && DateTime.UtcNow < until;
        }

        sealed class RateLimitedException : Exception { }

        Task<string> CallBackendAsync(string backend, string text, string sl, string tl, CancellationToken token)
        {
            switch (backend)
            {
                case "gtx": return GoogleSingleAsync("gtx", text, sl, tl, token);
                case "tw-ob": return GoogleSingleAsync("tw-ob", text, sl, tl, token);
                case "dict-chrome-ex": return DictChromeExAsync(text, sl, tl, token);
                case "edge-translate": return EdgeAsync(text, sl, tl, token);
                default: throw new ArgumentException("unknown backend " + backend);
            }
        }

        static string GoogleLang(string lang) => lang == "zh" ? "zh-CN" : lang;
        static string MicrosoftLang(string lang) => lang == "zh" ? "zh-Hans" : lang;

        async Task<string> GetStringChecked(HttpRequestMessage request, CancellationToken token)
        {
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode == (HttpStatusCode)429)
                throw new RateLimitedException();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("HTTP " + (int)response.StatusCode);
            return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        }

        // translate.googleapis.com/translate_a/single, response [[["translated","source",...],...],...]
        async Task<string> GoogleSingleAsync(string client, string text, string sl, string tl, CancellationToken token)
        {
            string url = "https://translate.googleapis.com/translate_a/single?client=" + client +
                         "&sl=" + GoogleLang(sl) + "&tl=" + GoogleLang(tl) + "&dt=t&q=" + Uri.EscapeDataString(text);
            string body = await GetStringChecked(new HttpRequestMessage(HttpMethod.Get, url), token).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0 || root[0].ValueKind != JsonValueKind.Array)
                return null;

            var sb = new StringBuilder();
            foreach (var sentence in root[0].EnumerateArray())
            {
                if (sentence.ValueKind == JsonValueKind.Array && sentence.GetArrayLength() > 0 &&
                    sentence[0].ValueKind == JsonValueKind.String)
                    sb.Append(sentence[0].GetString());
            }
            return sb.ToString();
        }

        // clients5.google.com dict-chrome-ex, response ["translated","detected_lang"]
        // (or [["translated","detected_lang"]] on some deployments).
        async Task<string> DictChromeExAsync(string text, string sl, string tl, CancellationToken token)
        {
            string url = "https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=" + GoogleLang(sl) +
                         "&tl=" + GoogleLang(tl) + "&q=" + Uri.EscapeDataString(text);
            string body = await GetStringChecked(new HttpRequestMessage(HttpMethod.Get, url), token).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return null;
            var first = root[0];
            if (first.ValueKind == JsonValueKind.String)
                return first.GetString();
            if (first.ValueKind == JsonValueKind.Array && first.GetArrayLength() > 0 && first[0].ValueKind == JsonValueKind.String)
                return first[0].GetString();
            return null;
        }

        // Microsoft Translator v3 with the anonymous token Edge's built-in translate uses.
        async Task<string> EdgeAsync(string text, string sl, string tl, CancellationToken token)
        {
            string bearer = await GetEdgeTokenAsync(token).ConfigureAwait(false);
            string url = "https://api.cognitive.microsofttranslator.com/translate?from=" + MicrosoftLang(sl) +
                         "&to=" + MicrosoftLang(tl) + "&api-version=3.0";

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
            string payload = "[{\"Text\":" + JsonEncodedString(text) + "}]";
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            string body;
            try
            {
                body = await GetStringChecked(request, token).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ex.Message == "HTTP 401")
            {
                lock (_lock)
                    _edgeToken = null;
                throw;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 &&
                root[0].TryGetProperty("translations", out var translations) &&
                translations.ValueKind == JsonValueKind.Array && translations.GetArrayLength() > 0 &&
                translations[0].TryGetProperty("text", out var t))
                return t.GetString();
            return null;
        }

        async Task<string> GetEdgeTokenAsync(CancellationToken token)
        {
            lock (_lock)
            {
                if (_edgeToken != null && DateTime.UtcNow < _edgeTokenExpires)
                    return _edgeToken;
            }
            string fresh = (await GetStringChecked(new HttpRequestMessage(HttpMethod.Get, "https://edge.microsoft.com/translate/auth"), token)
                .ConfigureAwait(false)).Trim();
            if (fresh.Length == 0)
                throw new HttpRequestException("edge auth returned an empty token");
            lock (_lock)
            {
                _edgeToken = fresh;
                _edgeTokenExpires = DateTime.UtcNow + EdgeTokenLifetime;
            }
            return fresh;
        }

        static string JsonEncodedString(string value)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
