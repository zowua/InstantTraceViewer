using System;
using System.Collections.Generic;
using Perfetto.Protos;

namespace InstantTraceViewerUI.Perfetto
{
    internal class InternedStringManager
    {
        class SequenceData
        {
            // Keys are Iid.
            public Dictionary<ulong, string> InternedEventNames = new();
            public Dictionary<ulong, string> InternedEventCategories = new();
            public Dictionary<ulong, string> InternedDebugAnnotationNames = new();

            public void Clear()
            {
                InternedEventNames.Clear();
                InternedEventCategories.Clear();
                InternedDebugAnnotationNames.Clear();
            }
        }

        // Key is TrustedPacketSequenceId
        private Dictionary<uint, SequenceData> allSequenceData = new Dictionary<uint, SequenceData>();

        public void ProcessPacket(TracePacket packet)
        {
            SequenceData sequenceData = GetOrCreateSequenceData(packet.TrustedPacketSequenceId);
            if (PerfettoSequenceState.IsCleanStateCleared(packet))
            {
                sequenceData.Clear();
            }

            // Track all interned strings.
            if (packet.InternedData == null)
            {
                return;
            }

            foreach (var eventName in packet.InternedData.EventNames)
            {
                if (eventName.HasIid && eventName.HasName)
                {
                    sequenceData.InternedEventNames[eventName.Iid] = eventName.Name;
                }
            }
            foreach (var eventCategory in packet.InternedData.EventCategories)
            {
                if (eventCategory.HasIid && eventCategory.HasName)
                {
                    sequenceData.InternedEventCategories[eventCategory.Iid] = eventCategory.Name;
                }
            }
            foreach (var annotationName in packet.InternedData.DebugAnnotationNames)
            {
                if (annotationName.HasIid && annotationName.HasName)
                {
                    sequenceData.InternedDebugAnnotationNames[annotationName.Iid] = annotationName.Name;
                }
            }
        }

        public string GetInternedEventName(TracePacket packet, ulong eventNameIid)
        {
            string name;
            SequenceData sequenceData = null;
            if (!allSequenceData.TryGetValue(packet.TrustedPacketSequenceId, out sequenceData) ||
                !sequenceData.InternedEventNames.TryGetValue(eventNameIid, out name))
            {
                name = $"Unknown NameIid={eventNameIid}";
            }

            return name;
        }

        public string GetInternedCategoryName(TracePacket packet, ulong categoryNameIid)
        {
            string name;
            SequenceData sequenceData = null;
            if (!allSequenceData.TryGetValue(packet.TrustedPacketSequenceId, out sequenceData) ||
                !sequenceData.InternedEventCategories.TryGetValue(categoryNameIid, out name))
            {
                name = $"Unknown CategoryNameIid={categoryNameIid}";
            }

            return name;
        }

        public string GetInternedDebugAnnotationName(TracePacket packet, ulong debugAnnotationNameIid)
        {
            string name;
            SequenceData sequenceData = null;
            if (!allSequenceData.TryGetValue(packet.TrustedPacketSequenceId, out sequenceData) ||
                !sequenceData.InternedDebugAnnotationNames.TryGetValue(debugAnnotationNameIid, out name))
            {
                name = $"Unknown DebugAnnotationNameIid={debugAnnotationNameIid}";
            }

            return name;
        }

        private SequenceData GetOrCreateSequenceData(uint sequenceId)
        {
            if (!allSequenceData.TryGetValue(sequenceId, out SequenceData sequenceData))
            {
                sequenceData = new SequenceData();
                allSequenceData.Add(sequenceId, sequenceData);
            }

            return sequenceData;
        }
    }
}
