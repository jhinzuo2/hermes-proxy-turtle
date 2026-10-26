using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Framework.Logging;
using HermesProxy.Enums;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;

namespace HermesProxy.Translation
{
    // Per-connection translation state: what the WoWTranslate addon asked for, and the
    // ordered queues that hold chat while it is being translated.
    //
    // Addon bridge: the addon whispers itself addon messages with prefix "WT". The proxy
    // answers them and never forwards them to the server. Requests start with "?",
    // answers with "!":
    //   ?HELLO                 -> !HELLO|<version>|<backend>
    //   ?CFG|k=v;k=v...        -> !CFG|ok
    //   ?NAME|<name>           -> !NAME|<name>|<english>   (no answer if it can't)
    //   ?STATUS                -> !STATUS|<text>
    //   ?CLEARCACHE            -> !CLEARCACHE|ok
    public sealed class TranslationSession
    {
        public const string AddonPrefix = "WT";

        // Chat type groups, as the addon names them in ?CFG.
        static readonly Dictionary<char, ChatMessageTypeModern[]> TypeGroups = new()
        {
            ['s'] = new[] { ChatMessageTypeModern.Say },
            ['y'] = new[] { ChatMessageTypeModern.Yell },
            ['w'] = new[] { ChatMessageTypeModern.Whisper, ChatMessageTypeModern.WhisperInform },
            ['p'] = new[] { ChatMessageTypeModern.Party, ChatMessageTypeModern.PartyLeader },
            ['r'] = new[] { ChatMessageTypeModern.Raid, ChatMessageTypeModern.RaidLeader, ChatMessageTypeModern.RaidWarning },
            ['g'] = new[] { ChatMessageTypeModern.Guild },
            ['o'] = new[] { ChatMessageTypeModern.Officer },
            ['b'] = new[] { ChatMessageTypeModern.Battleground, ChatMessageTypeModern.BattlegroundLeader },
            ['c'] = new[] { ChatMessageTypeModern.Channel },
            ['e'] = new[] { ChatMessageTypeModern.Emote },
        };

        readonly GlobalSessionData _session;
        readonly OrderedQueue _toClient = new();
        readonly OrderedQueue _toServer = new();
        readonly object _lock = new();

        // Defaults until the addon sends ?CFG: incoming on for player chat, outgoing off.
        bool _on = true;
        bool _incoming = true;
        bool _outgoing = false;
        bool _showOriginal = false;
        HashSet<ChatMessageTypeModern> _inTypes = ExpandTypes("sywprgobc");
        HashSet<ChatMessageTypeModern> _outTypes = ExpandTypes("sywprgobc");
        HashSet<string> _inChannelsOff = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> _outChannelsOff = new(StringComparer.OrdinalIgnoreCase);

        // What we sent for you: the Chinese the server will echo back -> the English you typed.
        readonly LinkedList<KeyValuePair<string, string>> _sentEcho = new();
        const int SentEchoMax = 100;

        public TranslationSession(GlobalSessionData session)
        {
            _session = session;
        }

        static HashSet<ChatMessageTypeModern> ExpandTypes(string codes)
        {
            var set = new HashSet<ChatMessageTypeModern>();
            foreach (char c in codes)
                if (TypeGroups.TryGetValue(c, out var types))
                    foreach (var t in types)
                        set.Add(t);
            return set;
        }

        static HashSet<string> ParseChannelList(string value)
        {
            return new HashSet<string>(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
        }

        // ---- Incoming (server -> client) ----

        bool WantsIncoming(ChatMessageTypeModern type, string channelName)
        {
            lock (_lock)
            {
                if (!_on || !_incoming || !_inTypes.Contains(type))
                    return false;
                if (type == ChatMessageTypeModern.Channel && _inChannelsOff.Contains(BaseChannelName(channelName)))
                    return false;
                return true;
            }
        }

        // "World - City" style names: match on the part the addon shows.
        static string BaseChannelName(string channelName)
        {
            if (string.IsNullOrEmpty(channelName))
                return "";
            int dash = channelName.IndexOf(" - ", StringComparison.Ordinal);
            return dash > 0 ? channelName.Substring(0, dash) : channelName;
        }

        // Called for every chat packet heading to the client. Sends it now or after
        // translation; chat keeps its order either way.
        // sendNow is used when nothing is waiting (same thread, normal send path); queued
        // packets go out through SendToClient from whichever thread finishes them.
        public void QueueIncoming(ChatPkt chat, ChatMessageTypeModern type, bool fromSelf, Action<ChatPkt> sendNow)
        {
            string original = chat.ChatText ?? "";
            Task<string> work = null;
            // A whisper inform carries the recipient as sender, but the text is ours.
            bool ownMessage = fromSelf || type == ChatMessageTypeModern.WhisperInform;

            if (ownMessage && TryGetSentEcho(original, out string typed))
            {
                // Our own message coming back: show what you typed, not a back-translation.
                bool showOriginal;
                lock (_lock)
                    showOriginal = _showOriginal;
                work = Task.FromResult(showOriginal ? typed + " (" + original + ")" : typed);
            }
            else if (!ownMessage && WantsIncoming(type, chat.Channel) && TranslationText.ContainsChinese(original))
            {
                work = TranslateIncomingAsync(original);
            }

            if (work == null && _toClient.IsEmpty)
            {
                sendNow(chat);
                return;
            }

            _toClient.Enqueue(work ?? Task.FromResult<string>(null), result =>
            {
                if (result != null)
                    chat.ChatText = result;
                SendToClient(_session, chat);
            });
        }

        async Task<string> TranslateIncomingAsync(string original)
        {
            string translated = await TranslationService.TranslateMessageAsync(original, TranslationDirection.ChineseToEnglish).ConfigureAwait(false);
            if (translated == null)
                return null;
            bool showOriginal;
            lock (_lock)
                showOriginal = _showOriginal;
            return showOriginal ? translated + " (" + original + ")" : translated;
        }

        // ---- Outgoing (client -> server) ----

        bool WantsOutgoing(ChatMessageTypeModern type, string channelName)
        {
            lock (_lock)
            {
                if (!_on || !_outgoing || !_outTypes.Contains(type))
                    return false;
                if (type == ChatMessageTypeModern.Channel && _outChannelsOff.Contains(BaseChannelName(channelName)))
                    return false;
                return true;
            }
        }

        // Called for chat you send. send() is given the text to pass to the server (the
        // Chinese translation, or your original text if translation is off or failed).
        public void QueueOutgoing(ChatMessageTypeModern type, string channelName, string text, Action<string> send)
        {
            Task<string> work = null;
            if (WantsOutgoing(type, channelName) && TranslationText.LooksEnglish(text))
                work = TranslationService.TranslateMessageAsync(text, TranslationDirection.EnglishToChinese);

            if (work == null && _toServer.IsEmpty)
            {
                send(text);
                return;
            }

            _toServer.Enqueue(work ?? Task.FromResult<string>(null), result =>
            {
                if (result == null)
                {
                    send(text);
                    return;
                }
                // The legacy server takes at most 255 bytes per message; Chinese is 3 bytes a character.
                foreach (string part in TranslationText.SplitByUtf8Bytes(result, 255))
                {
                    RememberSentEcho(part, part == result ? text : null);
                    send(part);
                }
            });
        }

        void RememberSentEcho(string sent, string typed)
        {
            if (typed == null)
                return; // split messages: let them show as sent
            lock (_sentEcho)
            {
                _sentEcho.AddLast(new KeyValuePair<string, string>(sent, typed));
                while (_sentEcho.Count > SentEchoMax)
                    _sentEcho.RemoveFirst();
            }
        }

        bool TryGetSentEcho(string echoed, out string typed)
        {
            lock (_sentEcho)
            {
                for (var node = _sentEcho.First; node != null; node = node.Next)
                {
                    if (node.Value.Key == echoed)
                    {
                        typed = node.Value.Value;
                        return true;
                    }
                }
            }
            typed = null;
            return false;
        }

        // ---- Addon bridge ----

        // Returns true if the message was for us (and must not go to the server).
        public bool HandleAddonMessage(string prefix, string text)
        {
            if (prefix != AddonPrefix || string.IsNullOrEmpty(text) || text[0] != '?')
                return prefix == AddonPrefix && TranslationService.IsEnabled;

            string[] parts = text.Substring(1).Split('|');
            string op = parts[0].ToUpperInvariant();
            switch (op)
            {
                case "HELLO":
                    Reply("!HELLO|" + TranslationService.Version + "|" + TranslationService.BackendMode);
                    break;
                case "CFG":
                    ApplyConfig(parts.Length > 1 ? string.Join("|", parts.Skip(1)) : "");
                    Reply("!CFG|ok");
                    break;
                case "NAME":
                    if (parts.Length > 1)
                        ReplyNameAsync(parts[1]);
                    break;
                case "STATUS":
                    Reply("!STATUS|" + StatusLine());
                    break;
                case "CLEARCACHE":
                    TranslationService.ClearCache();
                    Reply("!CLEARCACHE|ok");
                    break;
            }
            return true;
        }

        string StatusLine()
        {
            string last = TranslationService.LastBackend;
            string error = TranslationService.LastError;
            return $"backend {TranslationService.BackendMode}" + (last.Length > 0 ? $" (last ok: {last})" : "") +
                   $", cached {TranslationService.CacheCount}, {TranslationService.Stats}" +
                   (error.Length > 0 ? $", last error: {error}" : "");
        }

        // k=v pairs separated by ';':
        //   on, in, out, orig = 0/1; it, ot = chat type codes (see TypeGroups);
        //   xi, xo = comma-separated channel names to skip (incoming / outgoing).
        void ApplyConfig(string config)
        {
            lock (_lock)
            {
                foreach (string pair in config.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    string key = pair.Substring(0, eq).Trim();
                    string value = pair.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "on": _on = value == "1"; break;
                        case "in": _incoming = value == "1"; break;
                        case "out": _outgoing = value == "1"; break;
                        case "orig": _showOriginal = value == "1"; break;
                        case "it": _inTypes = ExpandTypes(value); break;
                        case "ot": _outTypes = ExpandTypes(value); break;
                        case "xi": _inChannelsOff = ParseChannelList(value); break;
                        case "xo": _outChannelsOff = ParseChannelList(value); break;
                    }
                }
                Log.Print(LogType.Debug, $"[Translate] Addon config: on={_on} in={_incoming} out={_outgoing} orig={_showOriginal}");
            }
        }

        async void ReplyNameAsync(string name)
        {
            try
            {
                string english = await TranslationService.TranslateNameAsync(name).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(english))
                    Reply("!NAME|" + name + "|" + english.Replace("|", ""));
            }
            catch (Exception ex)
            {
                Log.Print(LogType.Debug, $"[Translate] Name reply failed: {ex.Message}");
            }
        }

        // Sends an addon message to our own client as a whisper from ourselves.
        void Reply(string text)
        {
            var self = _session.GameState.CurrentPlayerGuid;
            if (self == null)
                return;
            string selfName = _session.GameState.GetPlayerName(self);
            var chat = new ChatPkt(_session, ChatMessageTypeModern.Whisper, text, (uint)Language.AddonBfA,
                self, selfName, self, selfName, "", ChatFlags.None, AddonPrefix);
            SendToClient(_session, chat);
        }

        // Sends from any thread. Packets for a session whose socket is gone are dropped
        // quietly instead of tripping the disconnect path a second time.
        public static void SendToClient(GlobalSessionData session, ChatPkt chat)
        {
            var socket = session.InstanceSocket;
            if (socket == null || !socket.IsOpen())
                return;
            socket.SendPacket(chat);
        }

        // Keeps chat in arrival order: an entry is sent only when it and everything ahead
        // of it is done (translated, timed out or failed).
        sealed class OrderedQueue
        {
            readonly Queue<(Task<string> Work, Action<string> Send)> _queue = new();
            readonly object _lock = new();

            public bool IsEmpty
            {
                get { lock (_lock) return _queue.Count == 0; }
            }

            public void Enqueue(Task<string> work, Action<string> send)
            {
                lock (_lock)
                    _queue.Enqueue((work, send));
                if (work.IsCompleted)
                    Drain();
                else
                    work.ContinueWith(_ => Drain(), TaskScheduler.Default);
            }

            void Drain()
            {
                lock (_lock)
                {
                    while (_queue.Count > 0 && _queue.Peek().Work.IsCompleted)
                    {
                        var (work, send) = _queue.Dequeue();
                        string result = work.Status == TaskStatus.RanToCompletion ? work.Result : null;
                        try
                        {
                            send(result);
                        }
                        catch (Exception ex)
                        {
                            Log.Print(LogType.Error, $"[Translate] Sending queued chat failed: {ex.Message}");
                        }
                    }
                }
            }
        }
    }
}
