using HermesProxy.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using Framework.Logging;
using Framework.Networking;
using HermesProxy;
using HermesProxy.Configuration;

namespace Framework
{
    public static class Settings
    {
        public static byte[] ClientSeed;
        public static ClientVersionBuild ClientBuild;
        public static ClientVersionBuild ServerBuild;
        public static string ServerAddress;
        public static int ServerPort;
        public static string ReportedOS;
        public static string ReportedPlatform;
        public static string ExternalAddress;
        public static int RestPort;
        public static int BNetPort;
        public static int RealmPort;
        public static int InstancePort;
        public static bool DebugOutput;
        public static bool PacketsLog;
        public static int ServerSpellDelay;
        public static int ClientSpellDelay;
        public static int LogonBuild;
        // TEMP DIAGNOSTIC: comma-separated creature entry ids whose CreateObject is dropped before
        // being sent to the client (binary-search a client-crashing creature). Empty = send all.
        public static System.Collections.Generic.HashSet<uint> SkipCreatureEntries = new();
        // TEMP DIAGNOSTIC: log every creature/gameobject CreateObject with its display id.
        public static bool LogCreatureCreates;
        // Drop GameObject creates whose entry is >= this value (0 = off). Capycraft's custom/event
        // GameObjects (entry >= ~50000) reference models the 1.14 client's content lacks and crash it.
        public static uint DropGameObjectEntriesAbove;
        // TEMP DIAGNOSTIC: drop every GameObject create (confirm whether a GO is the crash trigger).
        public static bool DropAllGameObjects;
        // TEMP DIAGNOSTIC: drop every creature (Unit) create.
        public static bool DropAllUnits;
        // Drop GameObject creates whose DisplayID is in this set (narrow the crashing display).
        public static System.Collections.Generic.HashSet<uint> DropGameObjectDisplays = new();
        // Drop GameObject creates whose resolved entry is 0 (proxy couldn't resolve the template).
        public static bool DropZeroEntryGameObjects;
        // Drop GameObject creates with no resolvable display (GameObjectData null or DisplayID null/0).
        // These are unrenderable and null-deref the client (ERROR #132, reading 0x10). Default ON.
        public static bool DropNullDisplayGameObjects;
        // Turn the server's "air swim" (Swimming flag forced on our own player while airborne, e.g. the
        // Caverns of Time drake) into real client flight, and report it back to the server as swimming.
        public static bool AirSwimAsFlight;
        // Show gear items the client has no data for (e.g. Capycraft transmog items like #171311) as a
        // client-known item with the same display, looked up from the server's item template.
        public static bool ResolveUnknownVisibleItems;
        // Creature display the client has no model for (ERROR #132, reading 0x10) is shown as this one
        // instead (default 3167 = Stormwind City Guard). 0 = off.
        public static uint CreatureDisplayFallback;

        public static bool LoadAndVerifyFrom(ConfigurationParser config)
        {
            ClientSeed = config.GetByteArray("ClientSeed", "179D3DC3235629D07113A9B3867F97A7".ParseAsByteArray());
            ClientBuild = config.GetEnum("ClientBuild", ClientVersionBuild.V2_5_2_40892);
            var serverBuildStr = config.GetString("ServerBuild", "auto");
            if (serverBuildStr == "auto")
                ServerBuild = VersionChecker.GetBestLegacyVersion(ClientBuild);
            else
                ServerBuild = config.GetEnum("ServerBuild", ClientVersionBuild.Zero);
            ServerAddress = config.GetString("ServerAddress", "127.0.0.1");
            ServerPort = config.GetInt("ServerPort", 3724);
            ReportedOS = config.GetString("ReportedOS", "OSX");
            ReportedPlatform = config.GetString("ReportedPlatform", "x86");
            ExternalAddress = config.GetString("ExternalAddress", "127.0.0.1");
            RestPort = config.GetInt("RestPort", 8081);
            BNetPort = config.GetInt("BNetPort", 1119);
            RealmPort = config.GetInt("RealmPort", 8084);
            InstancePort = config.GetInt("InstancePort", 8086);
            DebugOutput = config.GetBoolean("DebugOutput", false);
            PacketsLog = config.GetBoolean("PacketsLog", true);
            ServerSpellDelay = config.GetInt("ServerSpellDelay", 0);
            ClientSpellDelay = config.GetInt("ClientSpellDelay", 0);
            // Build number sent ONLY in the realmd LOGON_CHALLENGE. 0 = use ServerBuild.
            // TurtleWoW-derived realmd (Capycraft) rejects 5875 and wants e.g. 7272, while the
            // world server still expects the real ServerBuild in CMSG_AUTH_SESSION.
            LogonBuild = config.GetInt("LogonBuild", 0);
            SkipCreatureEntries = new System.Collections.Generic.HashSet<uint>();
            foreach (var part in config.GetString("SkipCreatureEntries", "").Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
                if (uint.TryParse(part, out var e)) SkipCreatureEntries.Add(e);
            LogCreatureCreates = config.GetBoolean("LogCreatureCreates", false);
            DropGameObjectEntriesAbove = (uint)config.GetInt("DropGameObjectEntriesAbove", 0);
            DropAllGameObjects = config.GetBoolean("DropAllGameObjects", false);
            DropAllUnits = config.GetBoolean("DropAllUnits", false);
            DropGameObjectDisplays = new System.Collections.Generic.HashSet<uint>();
            foreach (var part in config.GetString("DropGameObjectDisplays", "").Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
                if (uint.TryParse(part, out var d)) DropGameObjectDisplays.Add(d);
            DropZeroEntryGameObjects = config.GetBoolean("DropZeroEntryGameObjects", false);
            DropNullDisplayGameObjects = config.GetBoolean("DropNullDisplayGameObjects", true);
            AirSwimAsFlight = config.GetBoolean("AirSwimAsFlight", true);
            ResolveUnknownVisibleItems = config.GetBoolean("ResolveUnknownVisibleItems", true);
            CreatureDisplayFallback = (uint)config.GetInt("CreatureDisplayFallback", 3167);

            return VerifyConfig();
        }
        
        private static bool VerifyConfig()
        {
            if (ClientSeed.Length != 16)
            {
                Log.Print(LogType.Server, "ClientSeed must have byte length of 16 (32 characters)");
                return false;
            }

            if (!VersionChecker.IsSupportedModernVersion(ClientBuild))
            {
                Log.Print(LogType.Server, $"Unsupported ClientBuild '{ClientBuild}'");
                return false;
            }

            if (!VersionChecker.IsSupportedLegacyVersion(ServerBuild))
            {
                Log.Print(LogType.Server, $"Unsupported ServerBuild '{ServerBuild}', use 'auto' to select best");
                return false;
            }

            if (!IsValidPortNumber(RestPort))
            {
                Log.Print(LogType.Server, $"Specified battle.net port ({RestPort}) out of allowed range (1-65535)");
                return false;
            }

            if (!IsValidPortNumber(ServerPort))
            {
                Log.Print(LogType.Server, $"Specified battle.net port ({BNetPort}) out of allowed range (1-65535)");
                return false;
            }

            if (!IsValidPortNumber(BNetPort))
            {
                Log.Print(LogType.Server, $"Specified battle.net port ({BNetPort}) out of allowed range (1-65535)");
                return false;
            }

            if (!IsValidPortNumber(RealmPort))
            {
                Log.Print(LogType.Server, $"Specified battle.net port ({RealmPort}) out of allowed range (1-65535)");
                return false;
            }

            if (!IsValidPortNumber(InstancePort))
            {
                Log.Print(LogType.Server, $"Specified battle.net port ({InstancePort}) out of allowed range (1-65535)");
                return false;
            }

            if (ServerSpellDelay < 0)
            {
                Log.Print(LogType.Server, "ServerSpellDelay must be larger than or equal to 0");
                return false;
            }

            if (ClientSpellDelay < 0)
            {
                Log.Print(LogType.Server, "ClientSpellDelay must be larger than or equal to 0");
                return false;
            }

            bool IsValidPortNumber(int someNumber)
            {
                return someNumber > IPEndPoint.MinPort && someNumber < IPEndPoint.MaxPort;
            }

            return true;
        }
    }
}
