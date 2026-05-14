using System.Collections.Generic;
using System.Linq;
using Perfetto.Protos;

namespace InstantTraceViewerUI.Perfetto
{
    internal class ProcessThreadTracker
    {
        public record class ThreadData(int Tid, int Pid, string Name);

        // TODO: Add Uid? ParentId? Currently not needed.
        public record class ProcessData(int Pid, string Name);

        private record class TrackData(ulong Uuid, ulong ParentUuid, ProcessData? ProcessData, ThreadData? ThreadData);

        private Dictionary<ulong, TrackData> _trackByUuid = new(); // Uuid is key
        private Dictionary<ulong, ThreadData> _threadNameByUuid = new(); // Uuid is key
        private Dictionary<ulong, ProcessData> _processNameByUuid = new(); // Uuid is key

        private Dictionary<uint, ThreadData> _threadNameByTrustedPacketSequenceId = new(); // TrustedPacketSequenceId is key
        private Dictionary<uint, ProcessData> _processNameByTrustedPacketSequenceId = new(); // TrustedPacketSequenceId is key

        private Dictionary<int, ThreadData> _threadNameByTid = new(); // Tid is key
        private Dictionary<int, ProcessData> _processNameByPid = new(); // Pid is key

        public void ProcessPacket(TracePacket packet)
        {
            // For unknown reasons there may be repeats (same Uuid or same TrustedPacketSequenceId).

            if (packet.TrackDescriptor != null)
            {
                TrackDescriptor trackDescriptor = packet.TrackDescriptor;
                ProcessData? processData = null;
                ThreadData? threadData = null;

                if (trackDescriptor.Process?.HasPid ?? false)
                {
                    int pid = trackDescriptor.Process.Pid;
                    string processName = trackDescriptor.Process.HasProcessName ? trackDescriptor.Process.ProcessName : string.Empty;
                    processData = new ProcessData(pid, processName);

                    _processNameByUuid.TryGetValue(trackDescriptor.Uuid, out ProcessData? processByUuid);
                    _processNameByUuid[trackDescriptor.Uuid] = MergeProcessData(processByUuid, processData);

                    _processNameByTrustedPacketSequenceId.TryGetValue(packet.TrustedPacketSequenceId, out ProcessData? processBySequence);
                    _processNameByTrustedPacketSequenceId[packet.TrustedPacketSequenceId] = MergeProcessData(processBySequence, processData);

                    _processNameByPid.TryGetValue(pid, out ProcessData? processByPid);
                    _processNameByPid[pid] = MergeProcessData(processByPid, processData);
                }

                if (trackDescriptor.Thread?.HasTid ?? false)
                {
                    int tid = trackDescriptor.Thread.Tid;
                    string threadName = trackDescriptor.Thread.HasThreadName ? trackDescriptor.Thread.ThreadName : string.Empty;
                    int pid = trackDescriptor.Thread.HasPid ? trackDescriptor.Thread.Pid : 0;
                    threadData = new ThreadData(tid, pid, threadName);

                    _threadNameByUuid.TryGetValue(trackDescriptor.Uuid, out ThreadData? threadByUuid);
                    _threadNameByUuid[trackDescriptor.Uuid] = MergeThreadData(threadByUuid, threadData);

                    _threadNameByTrustedPacketSequenceId.TryGetValue(packet.TrustedPacketSequenceId, out ThreadData? threadBySequence);
                    _threadNameByTrustedPacketSequenceId[packet.TrustedPacketSequenceId] = MergeThreadData(threadBySequence, threadData);

                    _threadNameByTid.TryGetValue(tid, out ThreadData? threadByTid);
                    _threadNameByTid[tid] = MergeThreadData(threadByTid, threadData);
                }

                TrackData trackData = new(
                    trackDescriptor.Uuid,
                    trackDescriptor.HasParentUuid ? trackDescriptor.ParentUuid : 0,
                    processData,
                    threadData);
                _trackByUuid.TryGetValue(trackDescriptor.Uuid, out TrackData? existingTrackData);
                _trackByUuid[trackDescriptor.Uuid] = MergeTrackData(existingTrackData, trackData);
            }

            if (packet.ProcessTree != null)
            {
                foreach (var process in packet.ProcessTree.Processes)
                {
                    if (process.HasPid)
                    {
                        string processName = process.Cmdline.Count > 0 ? process.Cmdline.First() : string.Empty;
                        ProcessData processData = new(process.Pid, processName);
                        _processNameByPid.TryGetValue(process.Pid, out ProcessData? processByPid);
                        _processNameByPid[process.Pid] = MergeProcessData(processByPid, processData);
                    }
                }

                foreach (var thread in packet.ProcessTree.Threads)
                {
                    if (thread.HasTid)
                    {
                        string threadName = thread.HasName ? thread.Name : string.Empty;
                        ThreadData threadData = new(thread.Tid, thread.Tgid /* pid */, threadName);
                        _threadNameByTid.TryGetValue(thread.Tid, out ThreadData? threadByTid);
                        _threadNameByTid[thread.Tid] = MergeThreadData(threadByTid, threadData);
                    }
                }
            }
        }

        private static ThreadData MergeThreadData(ThreadData? existingThreadData, ThreadData newThreadData)
        {
            if (existingThreadData == null)
            {
                return newThreadData;
            }

            return new ThreadData(
                existingThreadData.Tid != 0 ? existingThreadData.Tid : newThreadData.Tid,
                existingThreadData.Pid != 0 ? existingThreadData.Pid : newThreadData.Pid,
                !string.IsNullOrEmpty(existingThreadData.Name) ? existingThreadData.Name : newThreadData.Name);
        }

        private static ProcessData MergeProcessData(ProcessData? existingProcessData, ProcessData newProcessData)
        {
            if (existingProcessData == null)
            {
                return newProcessData;
            }

            return new ProcessData(
                existingProcessData.Pid != 0 ? existingProcessData.Pid : newProcessData.Pid,
                !string.IsNullOrEmpty(existingProcessData.Name) ? existingProcessData.Name : newProcessData.Name);
        }

        private static TrackData MergeTrackData(TrackData? existingTrackData, TrackData newTrackData)
        {
            if (existingTrackData == null)
            {
                return newTrackData;
            }

            return new TrackData(
                existingTrackData.Uuid != 0 ? existingTrackData.Uuid : newTrackData.Uuid,
                existingTrackData.ParentUuid != 0 ? existingTrackData.ParentUuid : newTrackData.ParentUuid,
                existingTrackData.ProcessData != null && newTrackData.ProcessData != null ? MergeProcessData(existingTrackData.ProcessData, newTrackData.ProcessData) : existingTrackData.ProcessData ?? newTrackData.ProcessData,
                existingTrackData.ThreadData != null && newTrackData.ThreadData != null ? MergeThreadData(existingTrackData.ThreadData, newTrackData.ThreadData) : existingTrackData.ThreadData ?? newTrackData.ThreadData);
        }

        public ThreadData? GetThreadData(TracePacket packet, ulong? defaultTrackUuid)
        {
            ThreadData? threadData = null;
            if (TryGetEffectiveTrackUuid(packet, defaultTrackUuid, out ulong trackUuid))
            {
                threadData = GetThreadDataByTrackUuid(trackUuid, new HashSet<ulong>());
            }

            if (threadData == null)
            {
                _threadNameByTrustedPacketSequenceId.TryGetValue(packet.TrustedPacketSequenceId, out threadData);
            }

            return threadData;
        }

        public ProcessData? GetProcessData(TracePacket packet, ThreadData? threadData, ulong? defaultTrackUuid)
        {
            ProcessData? processData = null;
            if (TryGetEffectiveTrackUuid(packet, defaultTrackUuid, out ulong trackUuid))
            {
                processData = GetProcessDataByTrackUuid(trackUuid, new HashSet<ulong>());
            }

            if (processData == null)
            {
                _processNameByTrustedPacketSequenceId.TryGetValue(packet.TrustedPacketSequenceId, out processData);
            }

            if (processData == null && threadData != null)
            {
                _processNameByPid.TryGetValue(threadData.Pid, out processData);
            }

            return processData;
        }

        private static bool TryGetEffectiveTrackUuid(TracePacket packet, ulong? defaultTrackUuid, out ulong trackUuid)
        {
            if (packet.TrackEvent == null)
            {
                trackUuid = 0;
                return false;
            }

            if (packet.TrackEvent.HasTrackUuid)
            {
                trackUuid = packet.TrackEvent.TrackUuid;
                return true;
            }

            trackUuid = defaultTrackUuid ?? 0;
            return true;
        }

        private ThreadData? GetThreadDataByTrackUuid(ulong trackUuid, HashSet<ulong> visited)
        {
            if (!visited.Add(trackUuid))
            {
                return null;
            }

            if (_threadNameByUuid.TryGetValue(trackUuid, out ThreadData? threadData))
            {
                return threadData;
            }

            if (!_trackByUuid.TryGetValue(trackUuid, out TrackData? trackData))
            {
                return null;
            }

            if (trackData.ThreadData != null)
            {
                return trackData.ThreadData;
            }

            return trackData.ParentUuid != 0 ? GetThreadDataByTrackUuid(trackData.ParentUuid, visited) : null;
        }

        private ProcessData? GetProcessDataByTrackUuid(ulong trackUuid, HashSet<ulong> visited)
        {
            if (!visited.Add(trackUuid))
            {
                return null;
            }

            if (_processNameByUuid.TryGetValue(trackUuid, out ProcessData? processData))
            {
                return processData;
            }

            if (!_trackByUuid.TryGetValue(trackUuid, out TrackData? trackData))
            {
                return null;
            }

            if (trackData.ProcessData != null)
            {
                return trackData.ProcessData;
            }

            if (trackData.ThreadData != null && _processNameByPid.TryGetValue(trackData.ThreadData.Pid, out processData))
            {
                return processData;
            }

            return trackData.ParentUuid != 0 ? GetProcessDataByTrackUuid(trackData.ParentUuid, visited) : null;
        }

        public ThreadData? GetThreadDataByTid(int tid)
        {
            _threadNameByTid.TryGetValue(tid, out ThreadData? threadData);
            return threadData;
        }

        public ProcessData? GetProcessDataByPid(int pid)
        {
            _processNameByPid.TryGetValue(pid, out ProcessData? processData);
            return processData;
        }
    }
}
