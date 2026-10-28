using Framework.IO;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HermesProxy.World.Objects
{
    public class HotfixRecord
    {
        public uint HotfixId;
        public uint UniqueId;
        public DB2Hash TableHash;
        public uint RecordId;
        public HotfixStatus Status;
        public ByteBuffer HotfixContent = new();

        // The client keeps fetched hotfixes by (HotfixId, unique id) and only requests announced ones it doesn't
        // have. The announce used to send the table hash where the records carry UniqueId, so nothing ever matched
        // and every re-announce (QueryHandler.ScheduleHotfixAnnounce) made the client fetch and reload the whole
        // list: a freeze of a few seconds each time and the client's memory climbing. A content hash in both places
        // lets it fetch only new or changed records.
        public uint ContentUniqueId()
        {
            uint hash = 2166136261;
            void Mix(uint value)
            {
                for (int i = 0; i < 4; i++)
                {
                    hash ^= (value >> (i * 8)) & 0xFF;
                    hash *= 16777619;
                }
            }
            Mix(UniqueId);
            Mix((uint)TableHash);
            Mix(RecordId);
            Mix((uint)Status);
            foreach (byte b in HotfixContent.GetData())
            {
                hash ^= b;
                hash *= 16777619;
            }
            return hash;
        }

        public void WriteAvailable(WorldPacket data)
        {
            data.WriteUInt32(HotfixId);
            data.WriteUInt32(ContentUniqueId());
        }
        public void WriteHotFixMessageContent(WorldPacket data)
        {
            data.WriteUInt32(HotfixId);
            data.WriteUInt32(ContentUniqueId());
            data.WriteUInt32((uint)TableHash);
            data.WriteUInt32(RecordId);
            data.WriteUInt32(HotfixContent.GetSize());
            data.WriteBits((byte)Status, 3);
            data.FlushBits();
        }
    }
}
