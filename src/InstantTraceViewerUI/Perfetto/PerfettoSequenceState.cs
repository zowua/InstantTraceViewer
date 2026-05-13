using Perfetto.Protos;

namespace InstantTraceViewerUI.Perfetto
{
    internal static class PerfettoSequenceState
    {
        public static bool IsCleanStateCleared(TracePacket packet)
        {
            return
                packet.IncrementalStateCleared ||
                (packet.SequenceFlags & (uint)TracePacket.Types.SequenceFlags.SeqIncrementalStateCleared) != 0;
        }
    }
}
