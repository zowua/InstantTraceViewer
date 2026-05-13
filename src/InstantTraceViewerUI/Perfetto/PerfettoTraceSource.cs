using System;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.IO;
using System.Threading;
using System.Linq;
using System.Collections.Generic;
using InstantTraceViewer;
using Google.Protobuf;
using Perfetto.Protos;

namespace InstantTraceViewerUI.Perfetto
{
    internal class PerfettoTraceSource : ITraceSource
    {
        public static readonly TraceSourceSchemaColumn ColumnProcess = new TraceSourceSchemaColumn { Name = "Process", DefaultColumnSize = 3.75f };
        public static readonly TraceSourceSchemaColumn ColumnThread = new TraceSourceSchemaColumn { Name = "Thread", DefaultColumnSize = 3.75f };
        public static readonly TraceSourceSchemaColumn ColumnSource = new TraceSourceSchemaColumn { Name = "Source", DefaultColumnSize = 3.75f };
        public static readonly TraceSourceSchemaColumn ColumnName = new TraceSourceSchemaColumn { Name = "Name", DefaultColumnSize = 8.75f };
        public static readonly TraceSourceSchemaColumn ColumnCategory = new TraceSourceSchemaColumn { Name = "Category", DefaultColumnSize = 3.75f };
        public static readonly TraceSourceSchemaColumn ColumnPriority = new TraceSourceSchemaColumn { Name = "Priority", DefaultColumnSize = 3.75f };
        public static readonly TraceSourceSchemaColumn ColumnTime = new TraceSourceSchemaColumn { Name = "Time", DefaultColumnSize = 9.00f };
        public static readonly TraceSourceSchemaColumn ColumnData = new TraceSourceSchemaColumn { Name = "Data", DefaultColumnSize = null };

        private static readonly TraceTableSchema _schema = new TraceTableSchema
        {
            Columns = [ColumnProcess, ColumnThread, ColumnSource, ColumnName, ColumnCategory, ColumnPriority, ColumnTime, ColumnData],
            TimestampColumn = ColumnTime,
            ProviderColumn = ColumnSource,
            UnifiedLevelColumn = ColumnPriority,
            UnifiedOpcodeColumn = ColumnCategory,
            ProcessIdColumn = ColumnProcess,
            ThreadIdColumn = ColumnThread,
            NameColumn = ColumnName,
        };

        private static readonly JsonFormatter JsonFormatter = new JsonFormatter(JsonFormatter.Settings.Default.WithIndentation());

        private readonly Stream _traceStream;
        private readonly string _displayName;

        private readonly ReaderWriterLockSlim _traceRecordsLock = new ReaderWriterLockSlim();
        private ListBuilder<PerfettoRecord> _traceRecords = new ListBuilder<PerfettoRecord>();
        private int _generationId = 0;
        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();
        private readonly Thread _readThread;
        private bool _usesRelativeTimestamps = false;

        private sealed class PacketSequenceState
        {
            public uint? TimestampClockId;
            public ulong? TrackUuid;
            public bool IncrementalStateValid = true;
        }

        private readonly Dictionary<uint, PacketSequenceState> _packetSequenceStates = new();
        private readonly Dictionary<(uint SequenceId, ulong TrackUuid), Stack<(string Name, string SourceName)>> _sliceBeginEvents = new();

        private static string GetTrackEventSourceName(IEnumerable<string> categories)
        {
            string[] uniqueCategories = categories
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return uniqueCategories.Length > 0 ? string.Join(", ", uniqueCategories) : Source.TrackEvent.ToString();
        }

        public PerfettoTraceSource(string perfettoPath)
        {
            FileStream fileStream = new FileStream(perfettoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _traceStream = perfettoPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ?
                new GZipStream(fileStream, CompressionMode.Decompress) :
                fileStream;
            _displayName = Path.GetFileName(perfettoPath);

            _readThread = new Thread(ReadThread)
            {
                IsBackground = true,
                Name = "Perfetto trace parser"
            };
            _readThread.Start();
        }

        public string DisplayName => $"{_displayName} (Perfetto)";

        public bool CanClear => false;

        public void Clear() => throw new NotSupportedException();

        public bool CanPause => false;
        public bool IsPaused => false;

        public void TogglePause()
        {
            throw new NotImplementedException();
        }

        public int LostEvents => 0;

        // Perfetto parsing is quite slow and must be sorted at the end, so events can't stream out as they are parsed. So we indicate preprocessing is happening until all of the data is ready.
        public bool IsPreprocessingData { get; private set; } = true;

        public ITraceTableSnapshot CreateSnapshot()
        {
            _traceRecordsLock.EnterReadLock();
            try
            {
                return new PerfettoTraceTableSnapshot
                {
                    RecordSnapshot = _traceRecords.CreateSnapshot(),
                    GenerationId = _generationId,
                    Schema = _schema,
                    UsesRelativeTimestamps = _usesRelativeTimestamps,
                };
            }
            finally
            {
                _traceRecordsLock.ExitReadLock();
            }
        }

        public void Dispose()
        {
            _tokenSource.Cancel();
            _traceStream.Dispose();
            _readThread.Join(TimeSpan.FromSeconds(2));
        }

        private void ReadThread()
        {
            try
            {
                List<PerfettoRecord> records = new();

                Trace trace = Trace.Parser.ParseFrom(_traceStream);

                var processThreadTracker = new ProcessThreadTracker();
                var clockSync = new PerfettoClockConverter(trace);
                _usesRelativeTimestamps = clockSync.UsesSyntheticRealtime;

                // First pass: Preprocess packets for process and thread names.
                foreach (var packet in EnumeratePacketsWithValidIncrementalState(trace.Packet))
                {
                    _tokenSource.Token.ThrowIfCancellationRequested();
                    processThreadTracker.ProcessPacket(packet);
                }

                var internedStringManager = new InternedStringManager();
                foreach (var packet in trace.Packet)
                {
                    _tokenSource.Token.ThrowIfCancellationRequested();
                    if (!PreparePacketSequenceStateForPacket(packet))
                    {
                        continue;
                    }

                    // Interned strings may be reset or overridden and so the interned string manager can't be precomputed.
                    // It must be used as it is consuming packets.
                    internedStringManager.ProcessPacket(packet);

                    if (packet.SystemInfo != null)
                    {
                        PerfettoRecord record = new();
                        record.Source = Source.Metadata;
                        record.Name = "SystemInfo";
                        record.Priority = Priority.Info;
                        record.NamedValues = [new NamedValue { Name = null, Value = JsonFormatter.Format(packet.SystemInfo) }];
                        record.Timestamp = clockSync.GetPacketRealtimeTimestamp(packet, GetDefaultTimestampClockId(packet));
                        records.Add(record);
                    }
                    else if (packet.TraceConfig != null)
                    {
                        PerfettoRecord record = new();
                        record.Source = Source.Metadata;
                        record.Name = "TraceConfig";
                        record.Priority = Priority.Info;
                        record.NamedValues = [new NamedValue { Name = null, Value = JsonFormatter.Format(packet.TraceConfig) }];
                        record.Timestamp = clockSync.GetPacketRealtimeTimestamp(packet, GetDefaultTimestampClockId(packet));
                        records.Add(record);
                    }
                    else if (packet.ProcessTree != null)
                    {
                        // Handled by the ProcessThreadTracker
                    }
                    else if (packet.TrackEvent != null)
                    {
                        ProcessTrackEvent(records, packet, internedStringManager, processThreadTracker, clockSync);
                    }
                    else if (packet.AndroidLog != null)
                    {
                        ParseAndroidLogEvent(records, packet, processThreadTracker, clockSync);
                    }
                    else if (packet.FtraceEvents != null)
                    {
                        // Must be processed later to reorder things...
                    }
                    else if (packet.PerfSample != null)
                    {
                        // Stack sampling ignored. Too noisy and not intended for a log viewer.
                    }

                    if (packet.SynchronizationMarker != null)
                    {
                        // TODO: Can this be used to incrementally load in data? Docs say:
                        // > This is used to be able to efficiently partition long traces without having to fully parse them.
                        // But I am seeing FTrace events that come out of sequence across synchronization markers.
                    }

                    UpdatePacketSequenceStateAfterPacket(packet);
                }

                _tokenSource.Token.ThrowIfCancellationRequested();
                ProcessFTrace(records, trace, clockSync, processThreadTracker);

                // All events have been added and now they can be sorted.
                _tokenSource.Token.ThrowIfCancellationRequested();
                records.Sort((left, right) =>
                    (left.Timestamp < right.Timestamp) ? -1 :
                    (left.Timestamp > right.Timestamp) ? 1 : 0);

                PublishRecords(records);
            }
            catch (OperationCanceledException)
            {
                // Trace source is being disposed.
            }
            catch (ObjectDisposedException) when (_tokenSource.IsCancellationRequested)
            {
                // Trace source is being disposed while the parser is reading.
            }
            catch (IOException) when (_tokenSource.IsCancellationRequested)
            {
                // Trace source is being disposed while the parser is reading.
            }
            catch (Exception ex)
            {
                if (!_tokenSource.IsCancellationRequested)
                {
                    PublishRecords([CreateParserErrorRecord(ex)]);
                }
            }
            finally
            {
                IsPreprocessingData = false;
            }
        }

        private void PublishRecords(IEnumerable<PerfettoRecord> records)
        {
            _traceRecordsLock.EnterWriteLock();
            try
            {
                foreach (var record in records)
                {
                    _traceRecords.Add(record);
                }
            }
            finally
            {
                _traceRecordsLock.ExitWriteLock();
            }
        }

        private static PerfettoRecord CreateParserErrorRecord(Exception ex)
        {
            return new PerfettoRecord
            {
                Source = Source.Metadata,
                SourceName = "Perfetto parser",
                Name = "Failed to process trace",
                Priority = Priority.Error,
                Timestamp = DateTime.UnixEpoch,
                NamedValues = [new NamedValue("Error", ex.Message), new NamedValue("Type", ex.GetType().Name)]
            };
        }

        private void ProcessFTrace(List<PerfettoRecord> records, Trace trace, PerfettoClockConverter clockSync, ProcessThreadTracker processThreadTracker)
        {
            // See ParseSystraceTracePoint in Perfetto's trace_processor for the known structure of the 'Buf' field.
            Dictionary<(uint pid, int tid), Stack<string>> ftracePrintStacks = new();

            // In rare cases an FTrace end packet won't specify the pid so we have to look it up by last observed tid from a begin packet...
            Dictionary<int /* tid */, int /* pid */> beginPidTracker = new();

            void AddRecord(FtraceEvent evt, string name, int pid, Category category)
            {
                PerfettoRecord record = new();
                record.Source = Source.FTrace;
                record.Name = name;
                record.Pid = pid;
                record.Tid = (int)evt.Pid /* kernel pid appears to be tid... */;
                record.ProcessName = processThreadTracker.GetProcessDataByPid(pid)?.Name;
                record.ThreadName = processThreadTracker.GetThreadDataByTid((int)evt.Pid)?.Name;
                record.Category = category;
                record.Priority = Priority.Info;
                record.Timestamp = PerfettoClockConverter.RealTimeClockToDateTime(clockSync.ConvertTimestamp(BuiltinClock.Boottime, BuiltinClock.Realtime, evt.Timestamp));
                records.Add(record);
            }

            // FTrace events may be out of order so they must be sorted first in order to process the Begin/End print messages correctly...
            foreach (var e in trace.Packet
                .Where(p => p.FtraceEvents != null)
                .SelectMany(p => p.FtraceEvents.Event)
                .Where(e => e.EventCase == FtraceEvent.EventOneofCase.Print && e.Print.HasBuf && e.HasPid && e.HasTimestamp)
                .OrderBy(e => e.Timestamp))
            {
                _tokenSource.Token.ThrowIfCancellationRequested();

                string buf = e.Print.Buf.TrimEnd('\n');
                if (buf.StartsWith("B|")) // Begin
                {
                    int nameIndex = buf.IndexOf('|', 2);
                    if (nameIndex != -1 && nameIndex < buf.Length - 1 && int.TryParse(buf[2..nameIndex], out int pid))
                    {
                        string name = buf[(nameIndex + 1)..];

                        Stack<string> printStack;
                        if (!ftracePrintStacks.TryGetValue((e.Pid, pid), out printStack))
                        {
                            printStack = new Stack<string>();
                            ftracePrintStacks.Add((e.Pid, pid), printStack);
                        }

                        AddRecord(e, name, pid, Category.Begin);

                        printStack.Push(name);
                        beginPidTracker[(int)e.Pid /* kernel pid is tid */] = pid;
                    }
                }
                else if (buf.StartsWith("E|")) // End
                {
                    int pid;
                    if (int.TryParse(buf[2..], out pid) || beginPidTracker.TryGetValue((int)e.Pid, out pid))
                    {
                        // Must protect against getting an End without a matching Begin.
                        if (ftracePrintStacks.TryGetValue((e.Pid, pid), out Stack<string> printStack) && printStack.TryPop(out string name))
                        {
                            AddRecord(e, name, pid, Category.End);
                        }
                    }
                }
                else if (buf.StartsWith("I")) // Instant
                {
                    int nameIndex = buf.IndexOf('|', 2);
                    if (nameIndex != -1 && nameIndex < buf.Length - 1 && int.TryParse(buf[2..nameIndex], out int pid))
                    {
                        string name = buf[(nameIndex + 1)..];
                        AddRecord(e, name, pid, Category.None);
                    }
                }
            }
        }

        private void ProcessTrackEvent(List<PerfettoRecord> records, TracePacket packet, InternedStringManager internedStringManager, ProcessThreadTracker processThreadTracker, PerfettoClockConverter clockConverter)
        {
            // Use the provided non-interned name if available.
            string name = string.Empty;
            if (packet.TrackEvent.NameFieldCase == TrackEvent.NameFieldOneofCase.NameIid)
            {
                name = internedStringManager.GetInternedEventName(packet, packet.TrackEvent.NameIid);
            }
            else if (packet.TrackEvent.NameFieldCase == TrackEvent.NameFieldOneofCase.Name)
            {
                name = packet.TrackEvent.Name;
            }

            // I am unsure if these are exclusive but I just combine them.
            var categories = new List<string>();
            categories.AddRange(packet.TrackEvent.Categories);
            categories.AddRange(packet.TrackEvent.CategoryIids.Select(ciid => internedStringManager.GetInternedCategoryName(packet, ciid)));
            string sourceName = GetTrackEventSourceName(categories);
            (uint SequenceId, ulong TrackUuid) sliceStackKey = GetSliceStackKey(packet, GetDefaultTrackUuid(packet));

            // Later versions of Perfetto do not store the event name with the SliceEnd. Instead if must be inferred using a stack.
            if (!string.IsNullOrEmpty(name) && packet.TrackEvent.Type == TrackEvent.Types.Type.SliceBegin)
            {
                Stack<(string Name, string SourceName)> nameStack = null;
                if (_sliceBeginEvents.TryGetValue(sliceStackKey, out nameStack))
                {
                    nameStack.Push((name, sourceName));
                }
                else
                {
                    nameStack = new Stack<(string Name, string SourceName)>(new[] { (name, sourceName) });
                    _sliceBeginEvents.Add(sliceStackKey, nameStack);
                }
            }
            else if (packet.TrackEvent.Type == TrackEvent.Types.Type.SliceEnd)
            {
                Stack<(string Name, string SourceName)> nameStack = null;
                if (_sliceBeginEvents.TryGetValue(sliceStackKey, out nameStack) && nameStack.Count > 0)
                {
                    (string beginName, string beginSourceName) = nameStack.Pop();
                    if (string.IsNullOrEmpty(name))
                    {
                        name = beginName;
                    }

                    if (sourceName == Source.TrackEvent.ToString())
                    {
                        sourceName = beginSourceName;
                    }
                }
                else if (string.IsNullOrEmpty(name))
                {
                    name = "!!!MatchingSliceBeginMissing!!!";
                }
            }

            // See https://github.com/google/perfetto/blob/21753a5bd0877d6b7aac4bea0b593d3f8e55cfef/src/trace_processor/util/debug_annotation_parser.cc
            List<NamedValue> namedValues = new();
            foreach (var debugAnnotation in packet.TrackEvent.DebugAnnotations)
            {
                string debugAnnotationName = string.Empty;
                if (debugAnnotation.HasNameIid && debugAnnotation.NameFieldCase == DebugAnnotation.NameFieldOneofCase.NameIid)
                {
                    debugAnnotationName = internedStringManager.GetInternedDebugAnnotationName(packet, debugAnnotation.NameIid);
                }
                else if (debugAnnotation.HasName)
                {
                    debugAnnotationName = debugAnnotation.Name;
                }
                else
                {
                    debugAnnotationName = "???";
                }

                var debugAnnotationValue = GetDebugAnnotationStringValue(debugAnnotation);
                namedValues.Add(new NamedValue(debugAnnotationName, debugAnnotationValue));
            }

            ProcessThreadTracker.ThreadData? threadData = processThreadTracker.GetThreadData(packet, GetDefaultTrackUuid(packet));
            ProcessThreadTracker.ProcessData? processData = processThreadTracker.GetProcessData(packet, threadData, GetDefaultTrackUuid(packet));

            PerfettoRecord record = new();
            record.Name = name;
            record.Source = Source.TrackEvent;
            record.SourceName = sourceName;
            record.Category = packet.TrackEvent.Type switch
            {
                TrackEvent.Types.Type.SliceBegin => Category.Begin,
                TrackEvent.Types.Type.SliceEnd => Category.End,
                _ => Category.None,
            };
            record.Priority = record.Category == Category.None ? Priority.Info : Priority.Verbose;
            record.ProcessName = processData?.Name;
            record.ThreadName = threadData?.Name;
            record.Pid = processData?.Pid ?? threadData?.Pid ?? 0;
            record.Tid = threadData?.Tid ?? 0;
            record.Timestamp = clockConverter.GetPacketRealtimeTimestamp(packet, GetDefaultTimestampClockId(packet));
            record.NamedValues = namedValues.ToArray();
            records.Add(record);
        }

        // See DebugAnnotationParser::ParseDebugAnnotationValue
        private static string GetDebugAnnotationStringValue(DebugAnnotation debugAnnotation)
        {
            switch (debugAnnotation.ValueCase)
            {
                case DebugAnnotation.ValueOneofCase.None:
                    return string.Empty;
                case DebugAnnotation.ValueOneofCase.BoolValue:
                    return debugAnnotation.BoolValue.ToString();
                case DebugAnnotation.ValueOneofCase.UintValue:
                    return debugAnnotation.UintValue.ToString();
                case DebugAnnotation.ValueOneofCase.IntValue:
                    return debugAnnotation.IntValue.ToString();
                case DebugAnnotation.ValueOneofCase.DoubleValue:
                    return debugAnnotation.DoubleValue.ToString();
                case DebugAnnotation.ValueOneofCase.StringValue:
                    return debugAnnotation.StringValue;
                case DebugAnnotation.ValueOneofCase.PointerValue:
                    return debugAnnotation.PointerValue.ToString("X");
                case DebugAnnotation.ValueOneofCase.NestedValue:
                    // This annotation type is marked as deprecated.
                    return GetDebugAnnotationNestedValueStringValue(debugAnnotation.NestedValue);
                case DebugAnnotation.ValueOneofCase.LegacyJsonValue:
                    return debugAnnotation.LegacyJsonValue;
            }

            if ((debugAnnotation.ArrayValues?.Count ?? 0) > 0)
            {
                // TODO: Is this right? Do I just ignore the Values on this annotation? Why isn't this one of the "ValueCase"?
                return $"[{string.Join(", ", debugAnnotation.ArrayValues.Select(a => GetDebugAnnotationStringValue(a)))}]";
            }

            if (debugAnnotation.DictEntries != null)
            {
                return "<UNSUPPORTED DICTIONARY>";
                // TODO: Do I just ignore the Values on this annotation? Why isn't this one of the "ValueCase"?
                // TODO: Implement me.
            }

            return string.Empty;
        }

        private static string GetDebugAnnotationNestedValueStringValue(DebugAnnotation.Types.NestedValue nestedValue)
        {
            if (nestedValue.HasIntValue)
            {
                return nestedValue.IntValue.ToString();
            }
            else if (nestedValue.HasBoolValue)
            {
                return nestedValue.BoolValue.ToString();
            }
            else if (nestedValue.HasDoubleValue)
            {
                return nestedValue.DoubleValue.ToString();
            }
            else if (nestedValue.HasStringValue)
            {
                return nestedValue.StringValue;
            }
            // nestedValue.NestedType is unspecified when the NestedValue contains array data so it is ignored.
            else if (nestedValue.ArrayValues?.Count > 0)
            {
                return $"[{string.Join(", ", nestedValue.ArrayValues.Select(av => GetDebugAnnotationNestedValueStringValue(av)))}]";
            }
            else if (nestedValue.DictKeys?.Count > 0)
            {
                System.Diagnostics.Debug.Assert(nestedValue.DictKeys.Count == nestedValue.DictValues.Count);
                return $"[{string.Join(", ", nestedValue.DictKeys.Select((dk, i) => $"{dk}={GetDebugAnnotationNestedValueStringValue(nestedValue.DictValues[i])}"))}]";
            }

            return "UNSPECIFIED NESTED VALUE TYPE";
        }


        private void ParseAndroidLogEvent(List<PerfettoRecord> records, TracePacket packet, ProcessThreadTracker processThreadTracker, PerfettoClockConverter clockSync)
        {
            foreach (var evt in packet.AndroidLog.Events)
            {
                var pid = evt.HasPid ? evt.Pid : 0;
                var tid = evt.HasTid ? evt.Tid : 0;

                PerfettoRecord record = new();
                record.Name = evt.HasTag ? evt.Tag : string.Empty;
                record.Source = evt.LogId switch
                {
                    AndroidLogId.LidDefault => Source.LogcatDefault,
                    AndroidLogId.LidRadio => Source.LogcatRadio,
                    AndroidLogId.LidEvents => Source.LogcatEvents,
                    AndroidLogId.LidSystem => Source.LogcatSystem,
                    AndroidLogId.LidCrash => Source.LogcatCrash,
                    AndroidLogId.LidStats => Source.LogcatStats,
                    AndroidLogId.LidSecurity => Source.LogcatSecurity,
                    AndroidLogId.LidKernel => Source.LogcatKernel,
                    _ => Source.LogcatDefault,
                };
                record.SourceName = IsKnownAndroidLogId(evt.LogId) ? string.Empty : evt.LogId.ToString();
                record.Priority = evt.Prio switch
                {
                    AndroidLogPriority.PrioVerbose => Priority.Verbose,
                    AndroidLogPriority.PrioDebug => Priority.Debug,
                    AndroidLogPriority.PrioInfo => Priority.Info,
                    AndroidLogPriority.PrioWarn => Priority.Warning,
                    AndroidLogPriority.PrioError => Priority.Error,
                    AndroidLogPriority.PrioFatal => Priority.Fatal,
                    _ => Priority.Info,
                };
                record.Pid = pid;
                record.Tid = tid;
                record.ProcessName = processThreadTracker.GetProcessDataByPid(pid)?.Name;
                record.ThreadName = processThreadTracker?.GetThreadDataByTid(tid)?.Name;

                // evt.Timestamp is more accurate than packet.Timestamp. It's already in the Realtime clock domain.
                record.Timestamp = PerfettoClockConverter.RealTimeClockToDateTime(evt.Timestamp);
                record.NamedValues = [new NamedValue(null, evt.Message ?? string.Empty)];

                records.Add(record);
            }
        }

        private uint? GetDefaultTimestampClockId(TracePacket packet)
        {
            return _packetSequenceStates.TryGetValue(packet.TrustedPacketSequenceId, out PacketSequenceState? state) && state.IncrementalStateValid ? state.TimestampClockId : null;
        }

        private ulong? GetDefaultTrackUuid(TracePacket packet)
        {
            return _packetSequenceStates.TryGetValue(packet.TrustedPacketSequenceId, out PacketSequenceState? state) && state.IncrementalStateValid ? state.TrackUuid : null;
        }

        private bool PreparePacketSequenceStateForPacket(TracePacket packet)
        {
            uint sequenceId = packet.TrustedPacketSequenceId;
            if (packet.FirstPacketOnSequence)
            {
                ResetPacketSequenceState(sequenceId, incrementalStateValid: true, clearSliceStacks: true);
                return true;
            }

            if (PerfettoSequenceState.IsCleanStateCleared(packet))
            {
                ResetPacketSequenceState(sequenceId, incrementalStateValid: true, clearSliceStacks: false);
                return true;
            }

            if (packet.PreviousPacketDropped)
            {
                ResetPacketSequenceState(sequenceId, incrementalStateValid: false, clearSliceStacks: true);
                return false;
            }

            if (!_packetSequenceStates.TryGetValue(sequenceId, out PacketSequenceState? state))
            {
                state = new PacketSequenceState();
                _packetSequenceStates.Add(sequenceId, state);
            }

            return state.IncrementalStateValid;
        }

        private void ResetPacketSequenceState(uint sequenceId, bool incrementalStateValid, bool clearSliceStacks)
        {
            _packetSequenceStates[sequenceId] = new PacketSequenceState
            {
                IncrementalStateValid = incrementalStateValid
            };

            if (!clearSliceStacks)
            {
                return;
            }

            foreach ((uint SequenceId, ulong TrackUuid) key in _sliceBeginEvents.Keys.Where(key => key.SequenceId == sequenceId).ToArray())
            {
                _sliceBeginEvents.Remove(key);
            }
        }

        private void UpdatePacketSequenceStateAfterPacket(TracePacket packet)
        {
            if (!_packetSequenceStates.TryGetValue(packet.TrustedPacketSequenceId, out PacketSequenceState? state))
            {
                state = new PacketSequenceState();
                _packetSequenceStates.Add(packet.TrustedPacketSequenceId, state);
            }

            if (!state.IncrementalStateValid)
            {
                return;
            }

            TracePacketDefaults? defaults = packet.TracePacketDefaults;
            if (defaults == null)
            {
                return;
            }

            if (defaults.HasTimestampClockId)
            {
                state.TimestampClockId = defaults.TimestampClockId;
            }

            if (defaults.TrackEventDefaults?.HasTrackUuid ?? false)
            {
                state.TrackUuid = defaults.TrackEventDefaults.TrackUuid;
            }
        }

        private static ulong GetEffectiveTrackUuid(TracePacket packet, ulong? defaultTrackUuid)
        {
            return packet.TrackEvent.HasTrackUuid ? packet.TrackEvent.TrackUuid : defaultTrackUuid ?? 0;
        }

        private static (uint SequenceId, ulong TrackUuid) GetSliceStackKey(TracePacket packet, ulong? defaultTrackUuid)
        {
            return (packet.TrustedPacketSequenceId, GetEffectiveTrackUuid(packet, defaultTrackUuid));
        }

        private static IEnumerable<TracePacket> EnumeratePacketsWithValidIncrementalState(IEnumerable<TracePacket> packets)
        {
            HashSet<uint> invalidSequences = new();
            foreach (TracePacket packet in packets)
            {
                uint sequenceId = packet.TrustedPacketSequenceId;
                if (packet.FirstPacketOnSequence || PerfettoSequenceState.IsCleanStateCleared(packet))
                {
                    invalidSequences.Remove(sequenceId);
                }
                else if (packet.PreviousPacketDropped)
                {
                    invalidSequences.Add(sequenceId);
                    continue;
                }

                if (!invalidSequences.Contains(sequenceId))
                {
                    yield return packet;
                }
            }
        }

        private static bool IsKnownAndroidLogId(AndroidLogId logId)
        {
            return logId is
                AndroidLogId.LidDefault or
                AndroidLogId.LidRadio or
                AndroidLogId.LidEvents or
                AndroidLogId.LidSystem or
                AndroidLogId.LidCrash or
                AndroidLogId.LidStats or
                AndroidLogId.LidSecurity or
                AndroidLogId.LidKernel;
        }
    }
}
