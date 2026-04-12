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

        private Dictionary<ulong, ThreadData> _threadNameByUuid = new(); // Uuid is key
        private Dictionary<ulong, ProcessData> _processNameByUuid = new(); // Uuid is key

        private Dictionary<uint, ThreadData> _threadNameByTrustedPacketSequenceId = new(); // TrustedPacketSequenceId is key
        private Dictionary<uint, ProcessData> _processNameByTrustedPacketSequenceId = new(); // TrustedPacketSequenceId is key

        private Dictionary<int, ThreadData> _threadNameByTid = new(); // Tid is key
        private Dictionary<int, ProcessData> _processNameByPid = new(); // Pid is key

        public void ProcessPacket(TracePacket packet)
        {
            // For unknown reasons there may be repeats (same Uuid or same TrustedPacketSequenceId).

            if (packet.TrackDescriptor?.Process != null)
            {
                if (packet.TrackDescriptor.Process.HasPid)
                {
                    string processName = packet.TrackDescriptor.Process.HasProcessName ? packet.TrackDescriptor.Process.ProcessName : string.Empty;
                    ProcessData processData = new ProcessData(packet.TrackDescriptor.Process.Pid, processName);
                    UpdateProcessByUuid(packet.TrackDescriptor.Uuid, processData);
                    UpdateProcessByTrustedPacketSequenceId(packet.TrustedPacketSequenceId, processData);
                    UpdateProcessByPid(packet.TrackDescriptor.Process.Pid, processData);
                }
            }

            if (packet.TrackDescriptor?.Thread != null)
            {
                if (packet.TrackDescriptor.Thread.HasTid)
                {
                    string threadName = packet.TrackDescriptor.Thread.HasThreadName ? packet.TrackDescriptor.Thread.ThreadName : string.Empty;
                    int pid = packet.TrackDescriptor.Thread.HasPid ? packet.TrackDescriptor.Thread.Pid : 0;
                    ThreadData threadData = new ThreadData(packet.TrackDescriptor.Thread.Tid, pid, threadName);
                    UpdateThreadByUuid(packet.TrackDescriptor.Uuid, threadData);
                    UpdateThreadByTrustedPacketSequenceId(packet.TrustedPacketSequenceId, threadData);
                    UpdateThreadByTid(packet.TrackDescriptor.Thread.Tid, threadData);
                }
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
            if (existingThreadData == null || string.IsNullOrEmpty(existingThreadData.Name) && !string.IsNullOrEmpty(newThreadData.Name))
            {
                return newThreadData;
            }

            return existingThreadData;
        }

        private static ProcessData MergeProcessData(ProcessData? existingProcessData, ProcessData newProcessData)
        {
            if (existingProcessData == null || string.IsNullOrEmpty(existingProcessData.Name) && !string.IsNullOrEmpty(newProcessData.Name))
            {
                return newProcessData;
            }

            return existingProcessData;
        }

        public ThreadData GetThreadData(TracePacket packet)
        {
            ThreadData threadData = null;
            if (packet.TrackEvent?.HasTrackUuid ?? false)
            {
                _threadNameByUuid.TryGetValue(packet.TrackEvent.TrackUuid, out threadData);
            }

            if (threadData == null)
            {
                _threadNameByTrustedPacketSequenceId.TryGetValue(packet.TrustedPacketSequenceId, out threadData);
            }

            return threadData;
        }

        public ProcessData GetProcessData(TracePacket packet, ThreadData? threadData)
        {
            ProcessData processData = null;
            if (packet.TrackEvent?.HasTrackUuid ?? false)
            {
                _processNameByUuid.TryGetValue(packet.TrackEvent.TrackUuid, out processData);
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

        public ThreadData GetThreadDataByTid(int tid)
        {
            ThreadData threadData = null;
            _threadNameByTid.TryGetValue(tid, out threadData);
            return threadData;
        }

        public ProcessData GetProcessDataByPid(int pid)
        {
            ProcessData processData = null;
            _processNameByPid.TryGetValue(pid, out processData);
            return processData;
        }
    }
}
