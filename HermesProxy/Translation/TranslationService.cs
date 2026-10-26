using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Framework;
using Framework.Logging;

namespace HermesProxy.Translation
{
    public enum TranslationDirection
    {
        ChineseToEnglish,
        EnglishToChinese,
    }

    // Process-wide translation pipeline: preprocessing, markup protection, glossary,
    // cache, then the free backends. Turned on with Translation.Enabled in HermesProxy.config.
    public static class TranslationService
    {
        public const string Version = "1";

        static FreeTranslator _translator;
        static TranslationCache _cache;
        static Glossary _zhToEn;
        static Glossary _enToZh;
        static TimeSpan _timeout;

        public static bool IsEnabled { get; private set; }

        public static int CacheCount => _cache?.Count ?? 0;
        public static string BackendMode => _translator?.Mode ?? "off";
        public static string LastBackend => _translator?.LastBackend ?? "";
        public static string LastError => _translator?.LastError ?? "";

        static long _apiCalls, _cacheHits, _glossaryHits, _failures;
        public static string Stats =>
            $"api={Interlocked.Read(ref _apiCalls)} cache={Interlocked.Read(ref _cacheHits)} glossary={Interlocked.Read(ref _glossaryHits)} failed={Interlocked.Read(ref _failures)}";

        public static void Initialize()
        {
            if (!Settings.TranslationEnabled)
            {
                IsEnabled = false;
                return;
            }

            _timeout = TimeSpan.FromMilliseconds(Math.Max(500, Settings.TranslationTimeoutMs));
            _translator = new FreeTranslator(Settings.TranslationBackend, _timeout);
            _zhToEn = Glossary.LoadEmbedded("glossary-zh-en.tsv", 3, ignoreCase: false);
            _enToZh = Glossary.LoadEmbedded("glossary-en-zh.tsv", 4, ignoreCase: true);

            string cachePath = string.IsNullOrWhiteSpace(Settings.TranslationCacheFile)
                ? null
                : Path.GetFullPath(Settings.TranslationCacheFile);
            _cache = new TranslationCache(cachePath);

            IsEnabled = true;
            Log.Print(LogType.Server, $"[Translate] Chat translation ON (backend {_translator.Mode}, timeout {_timeout.TotalMilliseconds}ms, " +
                                      $"glossary {_zhToEn.Count} CN->EN / {_enToZh.Count} EN->CN)");
        }

        public static void ClearCache() => _cache?.Clear();

        // Switches the translation service for the whole proxy (all sessions), effective immediately.
        public static bool SetBackend(string backend)
        {
            if (_translator == null || !_translator.SetBackend(backend))
                return false;
            Log.Print(LogType.Server, $"[Translate] Backend switched to {_translator.Mode}");
            return true;
        }

        static readonly Regex PlaceholderSplit = new(@"( http://ph\.wt/\d+ )", RegexOptions.Compiled);

        // Glossary substitution on the text between placeholders only, so terms like "wt"
        // can never fire inside a placeholder URL. With tokens given, terms become
        // placeholders themselves (restored to the glossary text afterwards): EN->CN needs
        // that, because Google returns English with Chinese pasted into it untranslated.
        static string ApplyGlossaryPartial(Glossary glossary, string protectedText, List<string> tokens, out bool changed)
        {
            Func<string, string> emit = null;
            if (tokens != null)
                emit = replacement =>
                {
                    tokens.Add(replacement);
                    return TranslationText.PlaceholderFor(tokens.Count);
                };

            changed = false;
            string[] pieces = PlaceholderSplit.Split(protectedText);
            var sb = new StringBuilder();
            foreach (string piece in pieces)
            {
                if (PlaceholderSplit.IsMatch(piece))
                {
                    sb.Append(piece);
                    continue;
                }
                string replaced = glossary.ReplacePartial(piece, emit);
                if (replaced != null)
                {
                    changed = true;
                    sb.Append(replaced);
                }
                else
                    sb.Append(piece);
            }
            return sb.ToString();
        }

        // Translates a whole chat message (links and markup preserved). Resolves to null
        // when there is nothing to translate or the translation failed; callers then use
        // the original text.
        public static async Task<string> TranslateMessageAsync(string text, TranslationDirection direction)
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(text))
                return null;

            bool toEnglish = direction == TranslationDirection.ChineseToEnglish;
            string dirKey = toEnglish ? "zh>en" : "en>zh";
            Glossary glossary = toEnglish ? _zhToEn : _enToZh;

            try
            {
                if (toEnglish ? !TranslationText.ContainsChinese(text) : !TranslationText.LooksEnglish(text))
                    return null;

                // Whole message is a glossary term (e.g. "MC", "老克").
                if (glossary.TryExact(text, out string exact))
                {
                    Interlocked.Increment(ref _glossaryHits);
                    return exact;
                }

                string pre = toEnglish ? TranslationText.PreprocessIncoming(text) : TranslationText.PreprocessOutgoing(text);
                string protectedText = TranslationText.Protect(pre, out List<string> tokens);

                // Glossary first even on a cache hit: EN->CN glossary terms add placeholder
                // tokens that the cached text refers to.
                string request = ApplyGlossaryPartial(glossary, protectedText, toEnglish ? null : tokens, out bool glossaryChanged);

                if (_cache.TryGet(dirKey, protectedText, out string cached))
                {
                    Interlocked.Increment(ref _cacheHits);
                    return TranslationText.Restore(cached, tokens);
                }

                // The glossary may already have done the whole job.
                bool stillNeedsApi = toEnglish
                    ? TranslationText.ContainsChinese(request)
                    : TranslationText.ContainsLatinLetter(Regex.Replace(request, @"http://ph\.wt/\d+", ""));
                string translated;
                if (!stillNeedsApi && glossaryChanged)
                {
                    Interlocked.Increment(ref _glossaryHits);
                    translated = request;
                }
                else
                {
                    using var cts = new CancellationTokenSource(_timeout);
                    Interlocked.Increment(ref _apiCalls);
                    translated = await _translator.TranslateAsync(request, toEnglish ? "zh" : "en", toEnglish ? "en" : "zh", cts.Token)
                        .ConfigureAwait(false);
                    if (translated == null)
                    {
                        Interlocked.Increment(ref _failures);
                        Log.Print(LogType.Debug, $"[Translate] {dirKey} failed: {_translator.LastError}");
                        return null;
                    }
                }

                _cache.Add(dirKey, protectedText, translated);
                return TranslationText.Restore(translated, tokens);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _failures);
                Log.Print(LogType.Debug, $"[Translate] {dirKey} timed out after {_timeout.TotalMilliseconds}ms");
                return null;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failures);
                Log.Print(LogType.Warn, $"[Translate] {dirKey} error: {ex.Message}");
                return null;
            }
        }

        // Player/guild names: CN -> EN without the chat-slang rules (a name "88" stays "88").
        public static async Task<string> TranslateNameAsync(string name)
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(name) || !TranslationText.ContainsChinese(name))
                return null;
            const string dirKey = "name";
            if (_cache.TryGet(dirKey, name, out string cached))
            {
                Interlocked.Increment(ref _cacheHits);
                return cached;
            }
            try
            {
                using var cts = new CancellationTokenSource(_timeout);
                Interlocked.Increment(ref _apiCalls);
                string translated = await _translator.TranslateAsync(name, "zh", "en", cts.Token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(translated))
                {
                    Interlocked.Increment(ref _failures);
                    return null;
                }
                translated = CapitalizeWords(translated.Trim().TrimEnd('.'));
                _cache.Add(dirKey, name, translated);
                return translated;
            }
            catch (Exception)
            {
                Interlocked.Increment(ref _failures);
                return null;
            }
        }

        static string CapitalizeWords(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool start = true;
            foreach (char c in s)
            {
                sb.Append(start ? char.ToUpperInvariant(c) : c);
                start = c == ' ' || c == '-';
            }
            return sb.ToString();
        }
    }
}
