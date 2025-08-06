using ModelContextProtocol.Server;
using InstantTraceViewer.Server.Services;
using InstantTraceViewer;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System;

namespace InstantTraceViewer.Server.Providers
{
    [McpServerToolType]
    public static class TraceDataProvider
    {
        [McpServerTool, Description("Query trace events using advanced filter syntax (e.g., '@Message contains \"error\"', '@Level >= Warning', '@Process == \"myapp.exe\"'). This is the most powerful query tool and supports all ETL query syntax features.")]
        public static async Task<string> QueryWithSyntax(
            TraceManager traceManager,
            TraceQueryEngine queryEngine,
            [Description("Filter expression using the ETL query syntax. If empty or null, all events are returned. Examples: '@Message contains \"error\"', '@Level >= Warning', '(@Process == \"app.exe\") and (@Level >= Error)'")] string filterExpression = "",
            [Description("Maximum number of results to return (default: 100)")] int maxResults = 100,
            [Description("Specific columns to return (optional, returns all if not specified)")] string[]? columns = null)
        {
            var allEvents = new List<Dictionary<string, object>>();
            var traceSources = traceManager.GetTraceSources();

            foreach (var source in traceSources)
            {
                var queryResult = queryEngine.Query(source, filterExpression);

                if (queryResult.HasError)
                {
                    return JsonSerializer.Serialize(new { error = queryResult.ErrorMessage }, new JsonSerializerOptions { WriteIndented = true });
                }

                var filteredSnapshot = queryResult.Snapshot;
                if (filteredSnapshot == null) continue;

                // Get requested columns or all columns if none specified
                var columnsToReturn = columns?.Length > 0
                    ? filteredSnapshot.Schema.Columns.Where(c => columns.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).ToArray()
                    : filteredSnapshot.Schema.Columns.ToArray();

                // Collect the filtered events
                for (int i = 0; i < filteredSnapshot.RowCount && allEvents.Count < maxResults; i++)
                {
                    var eventDict = new Dictionary<string, object>();
                    eventDict["source"] = source.DisplayName;
                    
                    foreach (var col in columnsToReturn)
                    {
                        eventDict[col.Name] = filteredSnapshot.GetColumnValueString(i, col) ?? string.Empty;
                    }
                    allEvents.Add(eventDict);
                }
                
                if (allEvents.Count >= maxResults)
                {
                    allEvents = allEvents.Take(maxResults).ToList();
                    break;
                }
            }

            var result = JsonSerializer.Serialize(new 
            {
                events = allEvents,
                total_matched = allEvents.Count,
                filter_applied = filterExpression ?? "none"
            }, new JsonSerializerOptions { WriteIndented = true });

            return result;
        }

        [McpServerTool, Description("Get summary information about loaded traces, including available columns for querying")]
        public static async Task<string> GetTraceSummary(TraceManager traceManager)
        {
            var traceSources = traceManager.GetTraceSources();
            var allColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sources = new List<object>();

            int totalEvents = 0;
            foreach (var source in traceSources)
            {
                var snapshot = source.CreateSnapshot();
                if (snapshot is ITraceTableSnapshot tableSnapshot)
                {
                    totalEvents += tableSnapshot.RowCount;
                    var sourceColumns = tableSnapshot.Schema.Columns.Select(c => c.Name).ToArray();
                    foreach (var col in sourceColumns) allColumns.Add(col);

                    sources.Add(new
                    {
                        name = source.DisplayName,
                        event_count = tableSnapshot.RowCount,
                        columns = sourceColumns,
                        lost_events = source.LostEvents,
                        can_pause = source.CanPause,
                        is_paused = source.IsPaused,
                        can_clear = source.CanClear,
                        is_preprocessing = source.IsPreprocessingData
                    });
                }
            }

            var summary = new
            {
                trace_count = traceSources.Count(),
                total_events = totalEvents,
                sources = sources,
                all_available_columns = allColumns.OrderBy(c => c).ToArray()
            };

            var result = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
            return result;
        }
    }
}
