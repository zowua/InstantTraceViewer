using Microsoft.AspNetCore.Mvc;
using InstantTraceViewer.Server.Services;
using InstantTraceViewer;
using System.Collections.Generic;
using System.Linq;

namespace InstantTraceViewer.Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TraceController : ControllerBase
    {
        private readonly TraceManager _traceManager;
        private readonly TraceQueryEngine _queryEngine;

        public TraceController(TraceManager traceManager, TraceQueryEngine queryEngine)
        {
            _traceManager = traceManager;
            _queryEngine = queryEngine;
        }

        [HttpGet("events")]
        public ActionResult<IEnumerable<object>> GetTraceEvents([FromQuery] string? filter = null, [FromQuery] int limit = 100)
        {
            var allEvents = new List<object>();
            var traceSources = _traceManager.GetTraceSources();

            foreach (var source in traceSources)
            {
                // REUSE, DON'T REINVENT: Use the centralized query engine
                var queryResult = _queryEngine.Query(source, filter ?? string.Empty);

                if (queryResult.HasError)
                {
                    return BadRequest(new { error = queryResult.ErrorMessage });
                }

                var filteredSnapshot = queryResult.Snapshot;
                if (filteredSnapshot == null) continue;

                // Collect the filtered events
                for (int i = 0; i < filteredSnapshot.RowCount && allEvents.Count < limit; i++)
                {
                    var row = new Dictionary<string, object>();
                    row["source"] = source.DisplayName ?? "Unknown";
                    
                    foreach (var col in filteredSnapshot.Schema.Columns)
                    {
                        row[col.Name] = filteredSnapshot.GetColumnValueString(i, col);
                    }
                    allEvents.Add(row);
                }
            }

            if (!allEvents.Any())
            {
                return NoContent();
            }

            return Ok(allEvents);
        }

        [HttpGet("summary")]
        public ActionResult<object> GetTraceSummary()
        {
            var traceSources = _traceManager.GetTraceSources();
            var summary = new
            {
                trace_count = traceSources.Count(),
                sources = traceSources.Select(source =>
                {
                    var snapshot = source.CreateSnapshot();
                    return new
                    {
                        name = source.DisplayName,
                        event_count = snapshot?.RowCount ?? 0,
                        lost_events = source.LostEvents,
                        can_pause = source.CanPause,
                        is_paused = source.IsPaused
                    };
                }).ToArray()
            };

            return Ok(summary);
        }
    }
}
