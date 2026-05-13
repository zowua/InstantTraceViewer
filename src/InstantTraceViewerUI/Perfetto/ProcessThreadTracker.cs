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
                ProcessData? processData = null;
                ThreadData? threadData = null;

                if (packet.TrackDescriptor.Process?.HasPid ?? false)
                {
                    string processName = packet.TrackDescriptor.Process.HasProcessName ? packet.TrackDescriptor.Process.ProcessName : string.Empty;
                    processData = new ProcessData(packet.TrackDescriptor.Process.Pid, processName);
                    UpdateProcessByUuid(packet.TrackDescriptor.Uuid, processData);
                    UpdateProcessByTrustedPacketSequenceId(packet.TrustedPacketSequenceId, processData);
                    UpdateProcessByPid(packet.TrackDescriptor.Process.Pid, processData);
                }

                if (packet.TrackDescriptor.Thread?.HasTid ?? false)
                {
                    string threadName = packet.TrackDescriptor.Thread.HasThreadName ? packet.TrackDescriptor.Thread.ThreadName : string.Empty;
                    int pid = packet.TrackDescriptor.Thread.HasPid ? packet.TrackDescriptor.Thread.Pid : 0;
                    threadData = new ThreadData(packet.TrackDescriptor.Thread.Tid, pid, threadName);
                    UpdateThreadByUuid(packet.TrackDescriptor.Uuid, threadData);
                    UpdateThreadByTrustedPacketSequenceId(packet.TrustedPacketSequenceId, threadData);
                    UpdateThreadByTid(packet.TrackDescriptor.Thread.Tid, threadData);
                }

                TrackData trackData = new(
                    packet.TrackDescriptor.Uuid,
                    packet.TrackDescriptor.HasParentUuid ? packet.TrackDescriptor.ParentUuid : 0,
                    processData,
                    threadData);
                UpdateTrackByUuid(packet.TrackDescriptor.Uuid, trackData);
            }

            if (packet.ProcessTree != null)
            {
                foreach (var process in packet.ProcessTree.Processes)
                {
                    if (process.HasPid)
                    {
                        string processName = process.Cmdline.Count > 0 ? process.Cmdline.First() : string.Empty;
                        UpdateProcessByPid(process.Pid, new ProcessData(process.Pid, processName));
                    }
                }

                foreach (var thread in packet.ProcessTree.Threads)
                {
                    if (thread.HasTid)
                    {
                        string threadName = thread.HasName ? thread.Name : string.Empty;
                        UpdateThreadByTid(thread.Tid, new ThreadData(thread.Tid, thread.Tgid /* pid */, threadName));
                    }
                }
            }
        }

        private void UpdateTrackByUuid(ulong uuid, TrackData trackData)
        {
            _trackByUuid[uuid] = MergeTrackData(_trackByUuid.TryGetValue(uuid, out TrackData? existingTrackData) ? existingTrackData : null, trackData);
        }

        private void UpdateThreadByUuid(ulong uuid, ThreadData threadData)
        {
            _threadNameByUuid[uuid] = MergeThreadData(_threadNameByUuid.TryGetValue(uuid, out ThreadData? existingThreadData) ? existingThreadData : null, threadData);
        }

        private void UpdateThreadByTrustedPacketSequenceId(uint trustedPacketSequenceId, ThreadData threadData)
        {
            _threadNameByTrustedPacketSequenceId[trustedPacketSequenceId] = MergeThreadData(_threadNameByTrustedPacketSequenceId.TryGetValue(trustedPacketSequenceId, out ThreadData? existingThreadData) ? existingThreadData : null, threadData);
        }

        private void UpdateThreadByTid(int tid, ThreadData threadData)
        {
            _threadNameByTid[tid] = MergeThreadData(_threadNameByTid.TryGetValue(tid, out ThreadData? existingThreadData) ? existingThreadData : null, threadData);
        }

        private void UpdateProcessByUuid(ulong uuid, ProcessData processData)
        {
            _processNameByUuid[uuid] = MergeProcessData(_processNameByUuid.TryGetValue(uuid, out ProcessData? existingProcessData) ? existingProcessData : null, processData);
        }

        private void UpdateProcessByTrustedPacketSequenceId(uint trustedPacketSequenceId, ProcessData processData)
        {
            _processNameByTrustedPacketSequenceId[trustedPacketSequenceId] = MergeProcessData(_processNameByTrustedPacketSequenceId.TryGetValue(trustedPacketSequenceId, out ProcessData? existingProcessData) ? existingProcessData : null, processData);
        }

        private void UpdateProcessByPid(int pid, ProcessData processData)
        {
            _processNameByPid[pid] = MergeProcessData(_processNameByPid.TryGetValue(pid, out ProcessData? existingProcessData) ? existingProcessData : null, processData);
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
                threadData = GetThreadDataByTrackUuid(trackUuid);
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
                processData = GetProcessDataByTrackUuid(trackUuid);
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

        private ThreadData? GetThreadDataByTrackUuid(ulong trackUuid)
        {
            return GetThreadDataByTrackUuid(trackUuid, new HashSet<ulong>());
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

        private ProcessData? GetProcessDataByTrackUuid(ulong trackUuid)
        {
            return GetProcessDataByTrackUuid(trackUuid, new HashSet<ulong>());
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
            ThreadData? threadData = null;
            _threadNameByTid.TryGetValue(tid, out threadData);
            return threadData;
        }

        public ProcessData? GetProcessDataByPid(int pid)
        {
            ProcessData? processData = null;
            _processNameByPid.TryGetValue(pid, out processData);
            return processData;
        }
    }
}
