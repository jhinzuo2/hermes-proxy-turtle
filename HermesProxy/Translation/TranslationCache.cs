using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Framework.Logging;

namespace HermesProxy.Translation
{
    // Translations already seen are free and instant. Kept in memory and appended to a
    // tab-separated file next to the proxy so they survive restarts.
    public sealed class TranslationCache
    {
        const int MaxEntries = 50000;

        readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);
        readonly object _lock = new();
        readonly string _path;
        StreamWriter _writer;

        public TranslationCache(string path)
        {
            _path = path;
            Load();
        }

        public int Count
        {
            get { lock (_lock) return _entries.Count; }
        }

        static string Key(string direction, string text) => direction + "\u0001" + text;

        public bool TryGet(string direction, string text, out string translation)
        {
            lock (_lock)
                return _entries.TryGetValue(Key(direction, text), out translation);
        }

        public void Add(string direction, string text, string translation)
        {
            lock (_lock)
            {
                if (_entries.Count >= MaxEntries)
                    _entries.Clear(); // crude, but the file is the long-term store anyway
                string key = Key(direction, text);
                if (_entries.TryGetValue(key, out var existing) && existing == translation)
                    return;
                _entries[key] = translation;

                if (_path == null)
                    return;
                try
                {
                    _writer ??= new StreamWriter(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
                    _writer.WriteLine(Escape(direction) + "\t" + Escape(text) + "\t" + Escape(translation));
                }
                catch (Exception ex)
                {
                    Log.Print(LogType.Warn, $"[Translate] Could not write cache file {_path}: {ex.Message}");
                    _writer = null;
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
                _writer?.Dispose();
                _writer = null;
                if (_path != null && File.Exists(_path))
                    File.Delete(_path);
            }
        }

        void Load()
        {
            if (_path == null || !File.Exists(_path))
                return;
            try
            {
                foreach (string line in File.ReadLines(_path, Encoding.UTF8))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length != 3)
                        continue;
                    _entries[Key(Unescape(parts[0]), Unescape(parts[1]))] = Unescape(parts[2]);
                    if (_entries.Count >= MaxEntries)
                        break;
                }
                Log.Print(LogType.Server, $"[Translate] Loaded {_entries.Count} cached translations from {_path}");
            }
            catch (Exception ex)
            {
                Log.Print(LogType.Warn, $"[Translate] Could not read cache file {_path}: {ex.Message}");
            }
        }

        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");

        static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0)
                return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    sb.Append(s[i] switch { 't' => '\t', 'n' => '\n', 'r' => '\r', _ => s[i] });
                }
                else
                    sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
