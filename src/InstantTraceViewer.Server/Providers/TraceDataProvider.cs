using ModelContextProtocol.Server;
using InstantTraceViewer.Server.Services;
using InstantTraceViewer;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging;
using System.IO;

namespace InstantTraceViewer.Server.Providers
{
    [McpServerToolType]
    public static class TraceDataProvider
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions 
        { 
            WriteIndented = true,
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                new DefaultJsonTypeInfoResolver()
            )
        };

        private static void LogDebug(string message)
        {
            var logFile = Path.Combine(Path.GetTempPath(), "InstantTraceViewer_MCP_Debug.log");
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            File.AppendAllText(logFile, $"[{timestamp}] {message}\n");
            System.Diagnostics.Debug.WriteLine($"[MCP] {message}");
        }

        // Helper classes for JSON serialization
        private class QueryResultData
        {
            public List<Dictionary<string, object>> events { get; set; } = new();
            public int total_matched { get; set; }
            public string filter_applied { get; set; } = "";
        }

        private class ErrorResult
        {
            public string error { get; set; } = "";
        }

        private class TraceSourceInfo
        {
            public string name { get; set; } = "";
            public int event_count { get; set; }
            public string[] columns { get; set; } = Array.Empty<string>();
            public long lost_events { get; set; }
            public bool can_pause { get; set; }
            public bool is_paused { get; set; }
            public bool can_clear { get; set; }
            public bool is_preprocessing { get; set; }
        }

        private class TraceSummary
        {
            public int trace_count { get; set; }
            public int total_events { get; set; }
            public List<TraceSourceInfo> sources { get; set; } = new();
            public string[] all_available_columns { get; set; } = Array.Empty<string>();
        }
        [McpServerTool, Description("Query trace events using advanced filter syntax (e.g., '@Message contains \"error\"', '@Level >= Warning', '@Process == \"myapp.exe\"'). This is the most powerful query tool and supports all ETL query syntax features.")]
        public static string QueryWithSyntax(
            TraceManager traceManager,
            TraceQueryEngine queryEngine,
            [Description("Filter expression using the ETL query syntax. If empty or null, all events are returned. Examples: '@Message contains \"error\"', '@Level >= Warning', '(@Process == \"app.exe\") and (@Level >= Error)'")] string filterExpression = "",
            [Description("Maximum number of results to return (default: 100)")] int maxResults = 100,
            [Description("Specific columns to return (optional, returns all if not specified)")] string[]? columns = null)
        {
            LogDebug($"QueryWithSyntax called with filter: '{filterExpression}', maxResults: {maxResults}");
            var allEvents = new List<Dictionary<string, object>>();
            var traceSources = traceManager.GetTraceSources();
            LogDebug($"Found {traceSources.Count()} trace sources for query");

            foreach (var source in traceSources)
            {
                LogDebug($"Querying source: {source.DisplayName}");
                var queryResult = queryEngine.Query(source, filterExpression);

                if (queryResult.HasError)
                {
                    return JsonSerializer.Serialize(new ErrorResult { error = queryResult.ErrorMessage ?? "Unknown error" }, JsonOptions);
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
                        eventDict[col.Name] = filteredSnapshot.GetColumnValueString(i, col) ?? "";
                    }
                    allEvents.Add(eventDict);
                }
                
                if (allEvents.Count >= maxResults)
                {
                    allEvents = allEvents.Take(maxResults).ToList();
                    break;
                }
            }

            var result = JsonSerializer.Serialize(new QueryResultData
            {
                events = allEvents,
                total_matched = allEvents.Count,
                filter_applied = filterExpression ?? "none"
            }, JsonOptions);

            return result;
        }

        [McpServerTool, Description("Get summary information about loaded traces, including available columns for querying")]
        public static string GetTraceSummary(TraceManager traceManager)
        {
            LogDebug("GetTraceSummary called");
            var traceSources = traceManager.GetTraceSources();
            LogDebug($"Found {traceSources.Count()} trace sources");
            
            var allColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sources = new List<TraceSourceInfo>();

            int totalEvents = 0;
            foreach (var source in traceSources)
            {
                LogDebug($"Processing source: {source.DisplayName}");
                var snapshot = source.CreateSnapshot();
                LogDebug($"Snapshot type: {snapshot?.GetType().Name ?? "null"}");
                
                if (snapshot is ITraceTableSnapshot tableSnapshot)
                {
                    LogDebug($"Table snapshot has {tableSnapshot.RowCount} rows");
                    totalEvents += tableSnapshot.RowCount;
                    var sourceColumns = tableSnapshot.Schema.Columns.Select(c => c.Name).ToArray();
                    LogDebug($"Columns: [{string.Join(", ", sourceColumns)}]");
                    foreach (var col in sourceColumns) allColumns.Add(col);

                    sources.Add(new TraceSourceInfo
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
                else
                {
                    LogDebug("Snapshot is not ITraceTableSnapshot");
                }
            }

            var summary = new TraceSummary
            {
                trace_count = traceSources.Count(),
                total_events = totalEvents,
                sources = sources,
                all_available_columns = allColumns.OrderBy(c => c).ToArray()
            };

            LogDebug($"Final summary: {traceSources.Count()} sources, {totalEvents} total events");
            var result = JsonSerializer.Serialize(summary, JsonOptions);
            LogDebug($"Serialized result: {result}");
            return result;
        }

        [McpServerTool, Description("Debug tool: Add a test trace source to verify MCP integration")]
        public static string AddTestTraceSource(TraceManager traceManager)
        {
            LogDebug("AddTestTraceSource called");
            LogDebug($"TraceManager instance: {traceManager?.GetType().Name ?? "null"}");
            LogDebug($"Current trace source count: {traceManager?.GetTraceSources()?.Count ?? -1}");
            
            return JsonSerializer.Serialize(new { 
                message = "MCP infrastructure test completed",
                trace_manager_type = traceManager?.GetType().Name ?? "null",
                current_source_count = traceManager?.GetTraceSources()?.Count ?? -1,
                note = "If this shows proper TraceManager and responds correctly, MCP infrastructure works. The issue is UI not calling AddTraceSource."
            }, JsonOptions);
        }
    }
}
