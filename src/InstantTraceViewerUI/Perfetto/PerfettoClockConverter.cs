using System;
using System.Collections.Generic;
using System.Linq;
using Perfetto.Protos;

namespace InstantTraceViewerUI.Perfetto
{
    internal class PerfettoClockConverter
    {
        public PerfettoClockConverter(Trace trace, DateTime? inferredRealtimeOrigin = null)
        {
            ClockSnapshots = trace.Packet
                .Where(p => p.ClockSnapshot != null)
                .Select(p => p.ClockSnapshot)
                .ToArray();

            PrimaryTraceClock = ClockSnapshots
                .Where(s => s.HasPrimaryTraceClock && s.PrimaryTraceClock != BuiltinClock.Unknown)
                .Select(s => s.PrimaryTraceClock)
                .DefaultIfEmpty(BuiltinClock.Boottime)
                .First();

            var packetTimestamps = GetPacketTimestamps(trace).ToArray();

            var bootTimestamps = packetTimestamps
                .Where(p => p.ClockId == BuiltinClock.Boottime)
                .Select(p => p.Timestamp);
            EarliestBootTimestamp = bootTimestamps.DefaultIfEmpty().Min();

            EarliestTimestampByClock = packetTimestamps
                .GroupBy(p => NormalizeClockId(p.ClockId))
                .ToDictionary(g => g.Key, g => g.Min(p => p.Timestamp));

            foreach (var clock in ClockSnapshots
                .SelectMany(s => s.Clocks)
                .Where(c => c.HasClockId && c.HasTimestamp)
                .GroupBy(c => NormalizeClockId((BuiltinClock)c.ClockId)))
            {
                ulong earliestSnapshotTimestamp = clock.Min(c => c.Timestamp);
                if (!EarliestTimestampByClock.TryGetValue(clock.Key, out ulong earliestPacketTimestamp) || earliestSnapshotTimestamp < earliestPacketTimestamp)
                {
                    EarliestTimestampByClock[clock.Key] = earliestSnapshotTimestamp;
                }
            }

            InferredRealtimeOrigin = inferredRealtimeOrigin;
            UsesSyntheticRealtime = !TryConvertTimestampThroughSnapshots(PrimaryTraceClock, BuiltinClock.Realtime, GetClockOrigin(PrimaryTraceClock), out _);
        }

        public IReadOnlyCollection<ClockSnapshot> ClockSnapshots { get; private init; }
        public BuiltinClock PrimaryTraceClock { get; private init; }
        public Dictionary<BuiltinClock, ulong> EarliestTimestampByClock { get; private init; }
        public DateTime? InferredRealtimeOrigin { get; private init; }
        public bool UsesSyntheticRealtime { get; private init; }

        // ui.perfetto.dev appears to use the earliest timestamp as time 0 so match that behavior here. This is in BuiltinClock.Boottime domain.
        public ulong EarliestBootTimestamp { get; private init; }

        public ulong ConvertTimestamp(BuiltinClock fromClockId, BuiltinClock toClockId, ulong fromTimestamp)
        {
            fromClockId = NormalizeClockId(fromClockId);
            toClockId = NormalizeClockId(toClockId);

            if (TryConvertTimestampThroughSnapshots(fromClockId, toClockId, fromTimestamp, out ulong convertedTimestamp))
            {
                return convertedTimestamp;
            }

            if (toClockId == BuiltinClock.Realtime)
            {
                return ConvertToSyntheticRealtime(fromClockId, fromTimestamp);
            }

            System.Diagnostics.Debug.Fail($"No matching clock snapshot from {fromClockId} to {toClockId}");
            return fromTimestamp;
        }

        public DateTime GetPacketRealtimeTimestamp(TracePacket packet, uint? defaultTimestampClockId = null)
        {
            // Some packets like SystemInfo and TraceConfig do not have a timestamp so they will show at the top with the earliest timestamp.
            if (!packet.HasTimestamp)
            {
                return RealTimeClockToDateTime(ConvertTimestamp(BuiltinClock.Boottime, BuiltinClock.Realtime, EarliestBootTimestamp));
            }

            BuiltinClock fromClock = GetPacketTimestampClock(packet, defaultTimestampClockId);
            return RealTimeClockToDateTime(ConvertTimestamp(fromClock, BuiltinClock.Realtime, packet.Timestamp));
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

        private ulong ConvertToSyntheticRealtime(BuiltinClock fromClockId, ulong fromTimestamp)
        {
            ulong relativeTimestamp;
            if (TryConvertTimestampThroughSnapshots(fromClockId, PrimaryTraceClock, fromTimestamp, out ulong primaryTimestamp))
            {
                relativeTimestamp = MakeTraceRelativeTimestamp(PrimaryTraceClock, primaryTimestamp);
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

        private ulong MakeTraceRelativeTimestamp(BuiltinClock clockId, ulong timestamp)
        {
            ulong origin = GetClockOrigin(clockId);
            return timestamp >= origin ? timestamp - origin : 0;
        }

        private ulong GetClockOrigin(BuiltinClock clockId)
        {
            clockId = NormalizeClockId(clockId);
            if (EarliestTimestampByClock.TryGetValue(clockId, out ulong origin))
            {
                return origin;
            }

            if (clockId == BuiltinClock.Boottime)
            {
                return EarliestBootTimestamp;
            }

            return 0;
        }

        private static IEnumerable<(BuiltinClock ClockId, ulong Timestamp)> GetPacketTimestamps(Trace trace)
        {
            Dictionary<uint, uint> defaultTimestampClockIdBySequence = new();
            HashSet<uint> invalidSequences = new();
            foreach (TracePacket packet in trace.Packet)
            {
                uint sequenceId = packet.TrustedPacketSequenceId;
                if (packet.FirstPacketOnSequence || PerfettoSequenceState.IsCleanStateCleared(packet))
                {
                    defaultTimestampClockIdBySequence.Remove(sequenceId);
                    invalidSequences.Remove(sequenceId);
                }
                else if (packet.PreviousPacketDropped)
                {
                    defaultTimestampClockIdBySequence.Remove(sequenceId);
                    invalidSequences.Add(sequenceId);
                    continue;
                }

                if (invalidSequences.Contains(sequenceId))
                {
                    continue;
                }

                if (packet.HasTimestamp)
                {
                    uint? defaultTimestampClockId = defaultTimestampClockIdBySequence.TryGetValue(sequenceId, out uint clockId) ? clockId : null;
                    yield return (GetPacketTimestampClock(packet, defaultTimestampClockId), packet.Timestamp);
                }

                if (packet.TracePacketDefaults?.HasTimestampClockId ?? false)
                {
                    defaultTimestampClockIdBySequence[sequenceId] = packet.TracePacketDefaults.TimestampClockId;
                }
            }
        }

        private static BuiltinClock GetPacketTimestampClock(TracePacket packet, uint? defaultTimestampClockId)
        {
            return packet.HasTimestampClockId ?
                (BuiltinClock)packet.TimestampClockId :
                (BuiltinClock)(defaultTimestampClockId ?? (uint)BuiltinClock.Boottime);
        }

        private bool TryConvertTimestampThroughSnapshots(BuiltinClock fromClockId, BuiltinClock toClockId, ulong fromTimestamp, out ulong convertedTimestamp)
        {
            convertedTimestamp = fromTimestamp;
            return TryConvertTimestampThroughSnapshots(fromClockId, toClockId, fromTimestamp, new HashSet<BuiltinClock>(), out convertedTimestamp);
        }

        private bool TryConvertTimestampThroughSnapshots(BuiltinClock fromClockId, BuiltinClock toClockId, ulong fromTimestamp, HashSet<BuiltinClock> visited, out ulong convertedTimestamp)
        {
            fromClockId = NormalizeClockId(fromClockId);
            toClockId = NormalizeClockId(toClockId);

            if (fromClockId == toClockId)
            {
                convertedTimestamp = fromTimestamp;
                return true;
            }

            visited.Add(fromClockId);

            if (TryConvertTimestampDirectly(fromClockId, toClockId, fromTimestamp, out convertedTimestamp))
            {
                return true;
            }

            foreach (BuiltinClock intermediateClockId in GetConnectedClockIds(fromClockId))
            {
                if (visited.Contains(intermediateClockId))
                {
                    continue;
                }

                if (!TryConvertTimestampDirectly(fromClockId, intermediateClockId, fromTimestamp, out ulong intermediateTimestamp))
                {
                    continue;
                }

                if (TryConvertTimestampThroughSnapshots(intermediateClockId, toClockId, intermediateTimestamp, visited, out convertedTimestamp))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryConvertTimestampDirectly(BuiltinClock fromClockId, BuiltinClock toClockId, ulong fromTimestamp, out ulong convertedTimestamp)
        {
            Func<ClockSnapshot.Types.Clock, bool> isFromClock = c => c.HasClockId && c.HasTimestamp && NormalizeClockId((BuiltinClock)c.ClockId) == fromClockId;
            Func<ClockSnapshot.Types.Clock, bool> isToClock = c => c.HasClockId && c.HasTimestamp && NormalizeClockId((BuiltinClock)c.ClockId) == toClockId;

            var closestFromMatch = ClockSnapshots
                .Where(s => s.Clocks.Any(isFromClock) && s.Clocks.Any(isToClock))
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

        private IEnumerable<BuiltinClock> GetConnectedClockIds(BuiltinClock clockId)
        {
            return ClockSnapshots
                .Where(s => s.Clocks.Any(c => c.HasClockId && NormalizeClockId((BuiltinClock)c.ClockId) == clockId))
                .SelectMany(s => s.Clocks)
                .Where(c => c.HasClockId)
                .Select(c => NormalizeClockId((BuiltinClock)c.ClockId))
                .Where(c => c != clockId)
                .Distinct();
        }

        private static BuiltinClock NormalizeClockId(BuiltinClock clockId) => clockId == BuiltinClock.Unknown ? BuiltinClock.Boottime : clockId;
    }
}
