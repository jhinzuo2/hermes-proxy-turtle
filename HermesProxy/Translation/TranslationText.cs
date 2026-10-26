using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace HermesProxy.Translation
{
    // Text helpers for chat translation: language detection, the Chinese chat-slang
    // preprocessing ported from the 1.12 addon, and protection of WoW markup (links,
    // colours, textures, raid icons) so the translation API never sees or mangles it.
    public static class TranslationText
    {
        // |cffRRGGBB|H...|h[Name]|h|r links, bare |H links, |T textures|t, |A atlases|a,
        // lone colour codes, and {raid icon} tokens.
        static readonly Regex ProtectedToken = new(
            @"(?:\|c[0-9a-fA-F]{8})?\|H[^|]*\|h.*?\|h(?:\|r)?" +
            @"|\|T[^|]*\|t|\|A[^|]*\|a" +
            @"|\|c[0-9a-fA-F]{8}|\|r" +
            @"|\{[A-Za-z0-9]{1,12}\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Placeholders look like URLs because Google and Edge leave URLs untouched.
        static readonly Regex Placeholder = new(@"https?\s*:\s*/\s*/\s*ph\s*\.\s*wt\s*/\s*(\d+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static bool IsCjk(char c)
        {
            return (c >= '一' && c <= '鿿') ||   // CJK Unified Ideographs
                   (c >= '㐀' && c <= '䶿') ||   // Extension A
                   (c >= '豈' && c <= '﫿');     // Compatibility Ideographs
        }

        public static bool ContainsChinese(string text)
        {
            foreach (char c in text)
                if (IsCjk(c))
                    return true;
            return false;
        }

        public static bool ContainsLatinLetter(string text)
        {
            foreach (char c in text)
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                    return true;
            return false;
        }

        // A message only goes out for EN->CN translation if it has English in it and no
        // Chinese at all (typing Chinese yourself means you want it sent as typed).
        public static bool LooksEnglish(string text)
        {
            // Links can carry Chinese item names; only the typed text counts.
            string typed = StripProtected(text);
            return ContainsLatinLetter(typed) && !ContainsChinese(typed);
        }

        public static string PlaceholderFor(int number) => " http://ph.wt/" + number + " ";

        public static string StripProtected(string text)
        {
            return ProtectedToken.Replace(text, " ");
        }

        // Replaces protected markup with numbered placeholders. Returns the text to send
        // to the translator; tokens holds the originals for Restore.
        public static string Protect(string text, out List<string> tokens)
        {
            var found = new List<string>();
            string result = ProtectedToken.Replace(text, m =>
            {
                found.Add(m.Value);
                // Space-padded so the API never glues the placeholder to adjacent CJK.
                return PlaceholderFor(found.Count);
            });
            tokens = found;
            return result;
        }

        public static string Restore(string translated, List<string> tokens)
        {
            if (tokens.Count == 0)
                return translated;

            var used = new bool[tokens.Count];
            string result = Placeholder.Replace(translated, m =>
            {
                int index = int.Parse(m.Groups[1].Value) - 1;
                if (index < 0 || index >= tokens.Count)
                    return m.Value;
                used[index] = true;
                return tokens[index];
            });

            // A placeholder the API dropped: append its link rather than lose it.
            var sb = new StringBuilder(result);
            for (int i = 0; i < tokens.Count; i++)
                if (!used[i] && tokens[i].StartsWith("|", StringComparison.Ordinal) && tokens[i].Contains("|H"))
                    sb.Append(' ').Append(tokens[i]);

            return CollapseSpaces(sb.ToString());
        }

        static readonly Regex MultiSpace = new(@" {2,}", RegexOptions.Compiled);

        static string CollapseSpaces(string text)
        {
            return MultiSpace.Replace(text, " ").Trim();
        }

        // ---- Chinese chat slang (ported from PreprocessIncoming in the 1.12 addon) ----

        const string NotAlnumBefore = @"(?<![A-Za-z0-9])";
        const string NotAlnumAfter = @"(?![A-Za-z0-9])";

        static readonly (Regex, string)[] IncomingRules =
        {
            // Sentence terminators: keep the API from splitting into separate segments.
            (new Regex("。", RegexOptions.Compiled), ". "),
            (new Regex("！", RegexOptions.Compiled), "! "),
            (new Regex("？", RegexOptions.Compiled), "? "),
            // Currency: 10G = 10 gold, 50Y = 50 silver (not when followed by a letter: "YY", "GM").
            (new Regex(@"(\d+)G(?![A-Za-z])", RegexOptions.Compiled), "$1g"),
            (new Regex(@"(\d+)Y(?![A-Za-z])", RegexOptions.Compiled), "$1s"),
            // Number slang, isolated numbers only.
            (new Regex(NotAlnumBefore + "110" + NotAlnumAfter, RegexOptions.Compiled), "patrol"),
            (new Regex(NotAlnumBefore + "88" + NotAlnumAfter, RegexOptions.Compiled), "bye"),
            (new Regex(NotAlnumBefore + "999" + NotAlnumAfter, RegexOptions.Compiled), "res me"),
            (new Regex(NotAlnumBefore + "11" + NotAlnumAfter, RegexOptions.Compiled), "yes"),
        };

        public static string PreprocessIncoming(string text)
        {
            foreach (var (regex, replacement) in IncomingRules)
                text = regex.Replace(text, replacement);
            return text;
        }

        static readonly Regex OutGold = new(@"(\d+)g(?![A-Za-z])", RegexOptions.Compiled);
        static readonly Regex OutSilver = new(@"(\d+)s(?![A-Za-z])", RegexOptions.Compiled);
        static readonly Regex HasGold = new(@"\d+[gG]", RegexOptions.Compiled);

        // EN currency notation to the CN server's: 10g -> 10G, and 50s -> 50Y only when a
        // gold amount is present (so "3s cast" stays as is).
        public static string PreprocessOutgoing(string text)
        {
            text = OutGold.Replace(text, "$1G");
            if (HasGold.IsMatch(text))
                text = OutSilver.Replace(text, "$1Y");
            return text;
        }

        // Splits text into pieces of at most maxBytes UTF-8 bytes without cutting through a
        // character or a protected token (item links etc.), preferring to break at spaces.
        public static List<string> SplitByUtf8Bytes(string text, int maxBytes)
        {
            var parts = new List<string>();
            if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
            {
                parts.Add(text);
                return parts;
            }

            // Units = protected tokens (indivisible) and single characters in between.
            var units = new List<string>();
            int pos = 0;
            foreach (Match m in ProtectedToken.Matches(text))
            {
                for (int i = pos; i < m.Index; i++)
                {
                    if (char.IsHighSurrogate(text[i]) && i + 1 < m.Index)
                    {
                        units.Add(text.Substring(i, 2));
                        i++;
                    }
                    else
                        units.Add(text[i].ToString());
                }
                units.Add(m.Value);
                pos = m.Index + m.Length;
            }
            for (int i = pos; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length)
                {
                    units.Add(text.Substring(i, 2));
                    i++;
                }
                else
                    units.Add(text[i].ToString());
            }

            var current = new StringBuilder();
            int currentBytes = 0;
            int lastSpaceAt = -1; // index in current where a space was appended
            foreach (string unit in units)
            {
                int unitBytes = Encoding.UTF8.GetByteCount(unit);
                if (currentBytes + unitBytes > maxBytes && current.Length > 0)
                {
                    string chunk = current.ToString();
                    string carry = "";
                    if (lastSpaceAt > 0 && lastSpaceAt > chunk.Length / 2)
                    {
                        carry = chunk.Substring(lastSpaceAt + 1);
                        chunk = chunk.Substring(0, lastSpaceAt);
                    }
                    parts.Add(chunk.TrimEnd());
                    current.Clear().Append(carry);
                    currentBytes = Encoding.UTF8.GetByteCount(carry);
                    lastSpaceAt = -1;
                }
                if (unit == " ")
                    lastSpaceAt = current.Length;
                current.Append(unit);
                currentBytes += unitBytes;
            }
            if (current.Length > 0)
                parts.Add(current.ToString().Trim());

            parts.RemoveAll(string.IsNullOrEmpty);
            return parts;
        }
    }
}
