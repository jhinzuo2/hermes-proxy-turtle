using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace HermesProxy.Translation
{
    // WoW term glossary (ported from the 1.12 addon's WoWTranslate_Glossary.lua and
    // WoWTranslate_OutGlossary.lua). Exact hits skip the API entirely; partial hits are
    // substituted into the text before it is sent, so "老克" becomes "Kel'Thuzad"
    // instead of "Old gram".
    public sealed class Glossary
    {
        readonly Dictionary<string, string> _terms;
        readonly string[] _keysLongestFirst;
        readonly int _boundaryMaxBytes;
        readonly StringComparison _comparison;

        // boundaryMaxBytes: ASCII keys up to this many bytes must sit at a word boundary
        // ("av", "sm" must not fire inside other words). 3 for CN->EN, 4 for EN->CN, as in 1.12.
        Glossary(Dictionary<string, string> terms, int boundaryMaxBytes, bool ignoreCase)
        {
            _terms = terms;
            _boundaryMaxBytes = boundaryMaxBytes;
            _comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            _keysLongestFirst = terms.Keys.OrderByDescending(k => Encoding.UTF8.GetByteCount(k)).ToArray();
        }

        public int Count => _terms.Count;

        // ignoreCase: English keys ("LFG", "lfg", "Lfg" all match).
        public static Glossary LoadEmbedded(string fileName, int boundaryMaxBytes, bool ignoreCase)
        {
            var terms = new Dictionary<string, string>(ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var assembly = Assembly.GetExecutingAssembly();
            string resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(fileName, StringComparison.Ordinal));
            if (resource != null)
            {
                using var stream = assembly.GetManifestResourceStream(resource);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0 || line[0] == '#')
                        continue;
                    int tab = line.IndexOf('\t');
                    if (tab <= 0)
                        continue;
                    terms[line.Substring(0, tab)] = line.Substring(tab + 1);
                }
            }
            return new Glossary(terms, boundaryMaxBytes, ignoreCase);
        }

        public bool TryExact(string text, out string replacement)
        {
            return _terms.TryGetValue(text.Trim(), out replacement);
        }

        static bool IsAsciiAlnum(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
        }

        static bool IsPureAscii(string s)
        {
            foreach (char c in s)
                if (c > 127)
                    return false;
            return true;
        }

        // Replaces every non-overlapping term occurrence, longest terms first, scanning only
        // the original text (never earlier replacements). emit turns a term's replacement
        // into the text to insert (default: the replacement itself). Returns null when
        // nothing matched.
        public string ReplacePartial(string text, Func<string, string> emit = null)
        {
            var matches = new List<(int Start, int End, string Replacement)>();

            foreach (string key in _keysLongestFirst)
            {
                bool requireBoundary = IsPureAscii(key) && Encoding.UTF8.GetByteCount(key) <= _boundaryMaxBytes;
                int pos = 0;
                while (pos < text.Length)
                {
                    int start = text.IndexOf(key, pos, _comparison);
                    if (start < 0)
                        break;
                    int end = start + key.Length; // exclusive

                    bool ok = true;
                    if (requireBoundary)
                    {
                        if ((start > 0 && IsAsciiAlnum(text[start - 1])) || (end < text.Length && IsAsciiAlnum(text[end])))
                            ok = false;
                    }
                    if (ok)
                    {
                        foreach (var m in matches)
                        {
                            if (start < m.End && end > m.Start)
                            {
                                ok = false;
                                break;
                            }
                        }
                    }
                    if (ok)
                        matches.Add((start, end, _terms[key]));

                    pos = end;
                }
            }

            if (matches.Count == 0)
                return null;

            matches.Sort((a, b) => a.Start.CompareTo(b.Start));
            var sb = new StringBuilder();
            int last = 0;
            foreach (var m in matches)
            {
                sb.Append(text, last, m.Start - last);
                sb.Append(emit != null ? emit(m.Replacement) : m.Replacement);
                last = m.End;
            }
            sb.Append(text, last, text.Length - last);
            return sb.ToString();
        }
    }
}
