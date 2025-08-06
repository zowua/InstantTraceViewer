using InstantTraceViewer;
using System.Collections.Generic;
using System.Linq;

namespace InstantTraceViewer.Server.Services
{
    public class TraceManager
    {
        private readonly List<ITraceSource> _traceSources = new List<ITraceSource>();
        private readonly object _lock = new object();

        public void AddTraceSource(ITraceSource source)
        {
            lock (_lock)
            {
                _traceSources.Add(source);
            }
        }

        public void RemoveTraceSource(ITraceSource source)
        {
            lock (_lock)
            {
                _traceSources.Remove(source);
            }
        }

        public List<ITraceSource> GetTraceSources()
        {
            lock (_lock)
            {
                return new List<ITraceSource>(_traceSources);
            }
        }

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _traceSources.Count;
                }
            }
        }
    }
}
