using System;
using System.Collections.Generic;
using System.Linq;
using Perfetto.Protos;

namespace InstantTraceViewerUI.Perfetto
{
    internal class PerfettoClockConverter
    {
        // Perfetto reserves 64..127 for sequence-scoped producer clocks. The same
        // numeric clock id can mean different clocks on different packet sequences.
        private const uint FirstSequenceScopedClockId = (uint)BuiltinClock.MaxId + 1;
        private const uint LastSequenceScopedClockId = 127;

        public PerfettoClockConverter(Trace trace, DateTime? inferredRealtimeOrigin = null)
        {
            ClockSnapshots = trace.Packet
                .Where(p => p.ClockSnapshot != null)
                .Select(p => new ClockSnapshotRecord(
                    p.TrustedPacketSequenceId,
                    p.ClockSnapshot.Clocks
                        .Where(c => c.HasClockId && c.HasTimestamp)
                        .Select(c => new ClockSnapshotClock(NormalizeClockId(c.ClockId), ScaleTimestamp(c.Timestamp, GetUnitMultiplierNs(c))))
                        .ToArray())
                {
                    PrimaryTraceClock = p.ClockSnapshot.HasPrimaryTraceClock ? p.ClockSnapshot.PrimaryTraceClock : BuiltinClock.Unknown
                })
                .Where(s => s.Clocks.Count > 0)
                .ToArray();

            PrimaryTraceClock = ClockSnapshots
                .Select(s => s.PrimaryTraceClock)
                .Where(c => c != BuiltinClock.Unknown)
                .DefaultIfEmpty(BuiltinClock.Boottime)
                .First();

            PacketTimestampData timestampData = GetPacketTimestamps(trace);
            PacketTimestampByPacket = timestampData.PacketTimestampByPacket;
            PacketTimestamp[] packetTimestamps = timestampData.TraceOriginTimestamps;

            EarliestTimestampByClock = packetTimestamps
                .GroupBy(p => NormalizeClockId(p.ClockId))
                .ToDictionary(g => g.Key, g => g.Min(p => p.Timestamp));

            PrimaryTraceClockOrigin = GetPrimaryTraceClockOrigin(packetTimestamps);
            if (!TryConvertTimestampThroughSnapshots((uint)PrimaryTraceClock, (uint)BuiltinClock.Boottime, PrimaryTraceClockOrigin, sequenceId: null, out ulong bootOrigin))
            {
                bootOrigin = EarliestTimestampByClock.TryGetValue((uint)BuiltinClock.Boottime, out ulong earliestBootTimestamp) ?
                    earliestBootTimestamp :
                    PrimaryTraceClockOrigin;
            }

            EarliestBootTimestamp = bootOrigin;
            InferredRealtimeOrigin = inferredRealtimeOrigin;
            UsesSyntheticRealtime = !TryConvertTimestampThroughSnapshots((uint)PrimaryTraceClock, (uint)BuiltinClock.Realtime, PrimaryTraceClockOrigin, sequenceId: null, out _);
        }

        private IReadOnlyCollection<ClockSnapshotRecord> ClockSnapshots { get; init; }
        private Dictionary<TracePacket, PacketTimestamp> PacketTimestampByPacket { get; init; }
        public BuiltinClock PrimaryTraceClock { get; private init; }
        private ulong PrimaryTraceClockOrigin { get; init; }
        public Dictionary<uint, ulong> EarliestTimestampByClock { get; private init; }
        public DateTime? InferredRealtimeOrigin { get; private init; }
        public bool UsesSyntheticRealtime { get; private init; }

        // ui.perfetto.dev appears to use the earliest timestamp as time 0 so match that behavior here. This is in BuiltinClock.Boottime domain.
        public ulong EarliestBootTimestamp { get; private init; }

        public ulong ConvertTimestamp(BuiltinClock fromClockId, BuiltinClock toClockId, ulong fromTimestamp)
            => ConvertTimestamp((uint)fromClockId, (uint)toClockId, fromTimestamp, sequenceId: null);

        private ulong ConvertTimestamp(uint fromClockId, uint toClockId, ulong fromTimestamp, uint? sequenceId)
        {
            fromClockId = NormalizeClockId(fromClockId);
            toClockId = NormalizeClockId(toClockId);

            if (TryConvertTimestampThroughSnapshots(fromClockId, toClockId, fromTimestamp, sequenceId, out ulong convertedTimestamp))
            {
                return convertedTimestamp;
            }

            if (toClockId == (uint)BuiltinClock.Realtime)
            {
                return ConvertToSyntheticRealtime(fromClockId, fromTimestamp, sequenceId);
            }

            System.Diagnostics.Debug.Fail($"No matching clock snapshot from {(BuiltinClock)fromClockId} to {(BuiltinClock)toClockId}");
            return fromTimestamp;
        }

        public DateTime GetPacketRealtimeTimestamp(TracePacket packet, uint? defaultTimestampClockId = null)
        {
            // Some packets like SystemInfo and TraceConfig do not have a timestamp so they will show at the top with the earliest timestamp.
            if (!packet.HasTimestamp)
            {
                return RealTimeClockToDateTime(ConvertTimestamp((uint)PrimaryTraceClock, (uint)BuiltinClock.Realtime, PrimaryTraceClockOrigin, packet.TrustedPacketSequenceId));
            }

            if (PacketTimestampByPacket.TryGetValue(packet, out PacketTimestamp packetTimestamp))
            {
                return RealTimeClockToDateTime(ConvertTimestamp(packetTimestamp.ClockId, (uint)BuiltinClock.Realtime, packetTimestamp.Timestamp, packetTimestamp.SequenceId));
            }

            uint fromClock = GetPacketTimestampClock(packet, defaultTimestampClockId);
            return RealTimeClockToDateTime(ConvertTimestamp(fromClock, (uint)BuiltinClock.Realtime, packet.Timestamp, packet.TrustedPacketSequenceId));
        }

        public static DateTime RealTimeClockToDateTime(ulong timestamp)
        {
            var unixTime = (long)(timestamp / 1000000000);
            var unixTimeFraction = (long)(timestamp % 1000000000); // Fraction of a second in nanoseconds.
            return DateTime.UnixEpoch + TimeSpan.FromTicks(unixTime * TimeSpan.TicksPerSecond) + TimeSpan.FromTicks(unixTimeFraction / 100);
        }

        public static ulong DateTimeToUnixNanoseconds(DateTime dateTime)
        {
            DateTime utcDateTime = dateTime.Kind == DateTimeKind.Utc ? dateTime : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
            TimeSpan unixOffset = utcDateTime - DateTime.UnixEpoch;
            return (ulong)(unixOffset.Ticks * 100);
        }

        private ulong ConvertToSyntheticRealtime(uint fromClockId, ulong fromTimestamp, uint? sequenceId)
        {
            ulong relativeTimestamp;
            if (TryConvertTimestampThroughSnapshots(fromClockId, (uint)PrimaryTraceClock, fromTimestamp, sequenceId, out ulong primaryTimestamp))
            {
                relativeTimestamp = MakeTraceRelativeTimestamp((uint)PrimaryTraceClock, primaryTimestamp);
            }
            else
            {
                relativeTimestamp = MakeTraceRelativeTimestamp(fromClockId, fromTimestamp);
            }

            if (InferredRealtimeOrigin.HasValue)
            {
                return DateTimeToUnixNanoseconds(InferredRealtimeOrigin.Value) + relativeTimestamp;
            }

            return relativeTimestamp;
        }

        private ulong MakeTraceRelativeTimestamp(uint clockId, ulong timestamp)
        {
            ulong origin = GetClockOrigin(clockId);
            return timestamp >= origin ? timestamp - origin : 0;
        }

        private ulong GetClockOrigin(uint clockId)
        {
            clockId = NormalizeClockId(clockId);
            if (clockId == (uint)PrimaryTraceClock)
            {
                return PrimaryTraceClockOrigin;
            }

            if (EarliestTimestampByClock.TryGetValue(clockId, out ulong origin))
            {
                return origin;
            }

            if (clockId == (uint)BuiltinClock.Boottime)
            {
                return EarliestBootTimestamp;
            }

            return 0;
        }

        private ulong GetPrimaryTraceClockOrigin(IReadOnlyCollection<PacketTimestamp> packetTimestamps)
        {
            var originCandidates = packetTimestamps
                .Select(p => TryConvertTimestampThroughSnapshots(p.ClockId, (uint)PrimaryTraceClock, p.Timestamp, p.SequenceId, out ulong convertedTimestamp) ?
                    convertedTimestamp :
                    p.ClockId == (uint)PrimaryTraceClock ? p.Timestamp : (ulong?)null)
                .Where(t => t.HasValue)
                .Select(t => t!.Value)
                .ToArray();

            if (originCandidates.Length > 0)
            {
                return originCandidates.Min();
            }

            return EarliestTimestampByClock.TryGetValue((uint)PrimaryTraceClock, out ulong origin) ? origin : 0;
        }

        private static PacketTimestampData GetPacketTimestamps(Trace trace)
        {
            var packetTimestampByPacket = new Dictionary<TracePacket, PacketTimestamp>(ReferenceEqualityComparer.Instance);
            List<PacketTimestamp> traceOriginTimestamps = new();
            Dictionary<uint, PacketTimestampSequenceState> sequenceStates = new();
            HashSet<uint> invalidSequences = new();
            foreach (TracePacket packet in trace.Packet)
            {
                uint sequenceId = packet.TrustedPacketSequenceId;
                if (packet.FirstPacketOnSequence || PerfettoSequenceState.IsCleanStateCleared(packet))
                {
                    sequenceStates[sequenceId] = new PacketTimestampSequenceState();
                    invalidSequences.Remove(sequenceId);
                }
                else if (packet.PreviousPacketDropped)
                {
                    sequenceStates.Remove(sequenceId);
                    invalidSequences.Add(sequenceId);
                    continue;
                }
                else if (!sequenceStates.ContainsKey(sequenceId))
                {
                    sequenceStates[sequenceId] = new PacketTimestampSequenceState();
                }

                if (invalidSequences.Contains(sequenceId))
                {
                    continue;
                }

                PacketTimestampSequenceState sequenceState = sequenceStates[sequenceId];
                if (packet.HasTimestamp)
                {
                    uint clockId = GetPacketTimestampClock(packet, sequenceState.DefaultTimestampClockId);
                    // The Perfetto SDK can emit CPU track events on an incremental
                    // sequence-local clock while explicit Metal GPU timings use
                    // Monotonic. Rebuild the absolute packet timestamp here so the
                    // later ClockSnapshot conversion can place both on one timeline.
                    ulong timestamp = sequenceState.ResolveTimestamp(clockId, packet.Timestamp);
                    var packetTimestamp = new PacketTimestamp(sequenceId, clockId, timestamp);
                    packetTimestampByPacket[packet] = packetTimestamp;

                    if (ShouldUsePacketTimestampForTraceOrigin(packet))
                    {
                        traceOriginTimestamps.Add(packetTimestamp);
                    }
                }

                if (packet.ClockSnapshot != null)
                {
                    sequenceState.ProcessClockSnapshot(packet.ClockSnapshot);
                }

                if (packet.TracePacketDefaults?.HasTimestampClockId ?? false)
                {
                    sequenceState.DefaultTimestampClockId = packet.TracePacketDefaults.TimestampClockId;
                }
            }

            return new PacketTimestampData(traceOriginTimestamps.ToArray(), packetTimestampByPacket);
        }

        private static bool ShouldUsePacketTimestampForTraceOrigin(TracePacket packet)
        {
            // Use packets that can become visible records as the synthetic origin. ClockSnapshot
            // and service metadata packet timestamps describe packet emission, not trace events.
            return packet.TrackEvent != null ||
                packet.SystemInfo != null ||
                packet.TraceConfig != null ||
                packet.AndroidLog != null ||
                packet.FtraceEvents != null;
        }

        private static uint GetPacketTimestampClock(TracePacket packet, uint? defaultTimestampClockId)
        {
            return NormalizeClockId(packet.HasTimestampClockId ? packet.TimestampClockId : defaultTimestampClockId ?? (uint)BuiltinClock.Boottime);
        }

        private bool TryConvertTimestampThroughSnapshots(uint fromClockId, uint toClockId, ulong fromTimestamp, uint? sequenceId, out ulong convertedTimestamp)
        {
            convertedTimestamp = fromTimestamp;
            return TryConvertTimestampThroughSnapshots(fromClockId, toClockId, fromTimestamp, sequenceId, new HashSet<uint>(), out convertedTimestamp);
        }

        private bool TryConvertTimestampThroughSnapshots(uint fromClockId, uint toClockId, ulong fromTimestamp, uint? sequenceId, HashSet<uint> visited, out ulong convertedTimestamp)
        {
            fromClockId = NormalizeClockId(fromClockId);
            toClockId = NormalizeClockId(toClockId);

            if (fromClockId == toClockId)
            {
                convertedTimestamp = fromTimestamp;
                return true;
            }

            visited.Add(fromClockId);

            if (TryConvertTimestampDirectly(fromClockId, toClockId, fromTimestamp, sequenceId, out convertedTimestamp))
            {
                return true;
            }

            foreach (uint intermediateClockId in GetConnectedClockIds(fromClockId, sequenceId))
            {
                if (visited.Contains(intermediateClockId))
                {
                    continue;
                }

                if (!TryConvertTimestampDirectly(fromClockId, intermediateClockId, fromTimestamp, sequenceId, out ulong intermediateTimestamp))
                {
                    continue;
                }

                if (TryConvertTimestampThroughSnapshots(intermediateClockId, toClockId, intermediateTimestamp, sequenceId, visited, out convertedTimestamp))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryConvertTimestampDirectly(uint fromClockId, uint toClockId, ulong fromTimestamp, uint? sequenceId, out ulong convertedTimestamp)
        {
            Func<ClockSnapshotClock, bool> isFromClock = c => c.ClockId == fromClockId;
            Func<ClockSnapshotClock, bool> isToClock = c => c.ClockId == toClockId;

            var closestFromMatch = ClockSnapshots
                .Where(s => ClockSnapshotAppliesToSequence(s, fromClockId, sequenceId) &&
                    ClockSnapshotAppliesToSequence(s, toClockId, sequenceId) &&
                    s.Clocks.Any(isFromClock) &&
                    s.Clocks.Any(isToClock))
                .OrderBy(s => Math.Abs((long)s.Clocks.Single(isFromClock).Timestamp - (long)fromTimestamp))
                .FirstOrDefault();
            if (closestFromMatch == null)
            {
                convertedTimestamp = fromTimestamp;
                return false;
            }

            ulong fromSnapshotTimestamp = closestFromMatch.Clocks.Single(isFromClock).Timestamp;
            ulong toSnapshotTimestamp = closestFromMatch.Clocks.Single(isToClock).Timestamp;
            convertedTimestamp = (ulong)((long)fromTimestamp + ((long)toSnapshotTimestamp - (long)fromSnapshotTimestamp));
            return true;
        }

        private IEnumerable<uint> GetConnectedClockIds(uint clockId, uint? sequenceId)
        {
            return ClockSnapshots
                .Where(s => ClockSnapshotAppliesToSequence(s, clockId, sequenceId) && s.Clocks.Any(c => c.ClockId == clockId))
                .SelectMany(s => s.Clocks.Where(c => ClockSnapshotAppliesToSequence(s, c.ClockId, sequenceId)))
                .Select(c => c.ClockId)
                .Where(c => c != clockId)
                .Distinct();
        }

        private static bool ClockSnapshotAppliesToSequence(ClockSnapshotRecord snapshot, uint clockId, uint? sequenceId)
        {
            return !IsSequenceScopedClock(clockId) || (sequenceId.HasValue && snapshot.SequenceId == sequenceId.Value);
        }

        private static bool IsSequenceScopedClock(uint clockId)
        {
            return clockId >= FirstSequenceScopedClockId && clockId <= LastSequenceScopedClockId;
        }

        private static uint NormalizeClockId(BuiltinClock clockId) => NormalizeClockId((uint)clockId);

        private static uint NormalizeClockId(uint clockId) => clockId == (uint)BuiltinClock.Unknown ? (uint)BuiltinClock.Boottime : clockId;

        private static ulong GetUnitMultiplierNs(ClockSnapshot.Types.Clock clock)
        {
            return clock.HasUnitMultiplierNs && clock.UnitMultiplierNs != 0 ? clock.UnitMultiplierNs : 1;
        }

        private static ulong ScaleTimestamp(ulong timestamp, ulong unitMultiplierNs)
        {
            if (unitMultiplierNs == 1)
            {
                return timestamp;
            }

            return timestamp > ulong.MaxValue / unitMultiplierNs ? ulong.MaxValue : timestamp * unitMultiplierNs;
        }

        private static ulong AddSaturating(ulong left, ulong right)
        {
            return ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
        }

        private sealed record ClockSnapshotRecord(uint SequenceId, IReadOnlyList<ClockSnapshotClock> Clocks)
        {
            public BuiltinClock PrimaryTraceClock { get; init; } = BuiltinClock.Unknown;
        }

        private sealed record ClockSnapshotClock(uint ClockId, ulong Timestamp);

        private sealed record PacketTimestamp(uint SequenceId, uint ClockId, ulong Timestamp);

        private sealed record PacketTimestampData(PacketTimestamp[] TraceOriginTimestamps, Dictionary<TracePacket, PacketTimestamp> PacketTimestampByPacket);

        private sealed class PacketTimestampSequenceState
        {
            private readonly Dictionary<uint, SequenceClockState> _clocks = new();

            public uint? DefaultTimestampClockId { get; set; }

            public ulong ResolveTimestamp(uint clockId, ulong timestamp)
            {
                clockId = NormalizeClockId(clockId);
                if (!_clocks.TryGetValue(clockId, out SequenceClockState? clockState))
                {
                    return timestamp;
                }

                ulong scaledTimestamp = ScaleTimestamp(timestamp, clockState.UnitMultiplierNs);
                if (!clockState.IsIncremental)
                {
                    return scaledTimestamp;
                }

                ulong absoluteTimestamp = AddSaturating(clockState.LastTimestamp ?? 0, scaledTimestamp);
                clockState.LastTimestamp = absoluteTimestamp;
                return absoluteTimestamp;
            }

            public void ProcessClockSnapshot(ClockSnapshot clockSnapshot)
            {
                foreach (ClockSnapshot.Types.Clock clock in clockSnapshot.Clocks.Where(c => c.HasClockId && c.HasTimestamp))
                {
                    uint clockId = NormalizeClockId(clock.ClockId);
                    ulong unitMultiplierNs = GetUnitMultiplierNs(clock);
                    _clocks[clockId] = new SequenceClockState
                    {
                        IsIncremental = clock.IsIncremental,
                        UnitMultiplierNs = unitMultiplierNs,
                        LastTimestamp = clock.IsIncremental ? ScaleTimestamp(clock.Timestamp, unitMultiplierNs) : null
                    };
                }
            }
        }

        private sealed class SequenceClockState
        {
            public bool IsIncremental { get; init; }
            public ulong UnitMultiplierNs { get; init; } = 1;
            public ulong? LastTimestamp { get; set; }
        }
    }
}
