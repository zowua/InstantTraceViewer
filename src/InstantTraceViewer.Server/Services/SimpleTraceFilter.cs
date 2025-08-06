using InstantTraceViewer;
using System;
using System.Collections.Generic;
using System.Linq;

namespace InstantTraceViewer.Server.Services
{
    /// <summary>
    /// A simple trace filtering service that provides basic filtering capabilities
    /// without depending on the UI's complex filtering logic.
    /// </summary>
    public class SimpleTraceFilter
    {
        public class FilterResult
        {
            public List<Dictionary<string, object>> Events { get; init; } = new();
            public string ErrorMessage { get; init; } = string.Empty;
            public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
        }

        /// <summary>
        /// Applies a simple filter to trace data.
        /// For now, this provides basic filtering - a full implementation would parse the filter expression.
        /// </summary>
        public FilterResult FilterTraceData(ITraceSource source, string filterExpression, int maxResults = 100, string[]? requestedColumns = null)
        {
            try
            {
                var snapshot = source.CreateSnapshot();
                if (snapshot is not ITraceTableSnapshot tableSnapshot)
                {
                    return new FilterResult { ErrorMessage = "Could not create a table snapshot from the trace source." };
                }

                var events = new List<Dictionary<string, object>>();
                var columnsToReturn = requestedColumns?.Length > 0
                    ? tableSnapshot.Schema.Columns.Where(c => requestedColumns.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).ToArray()
                    : tableSnapshot.Schema.Columns.ToArray();

                // Simple filtering logic - for a complete implementation, you'd parse the filter expression
                // For now, we'll just apply basic text filtering if the filter contains simple patterns
                for (int i = 0; i < tableSnapshot.RowCount && events.Count < maxResults; i++)
                {
                    // Simple check: if no filter or filter is empty, include all
                    bool includeRow = string.IsNullOrWhiteSpace(filterExpression);
                    
                    if (!includeRow && !string.IsNullOrWhiteSpace(filterExpression))
                    {
                        // Simple text search across all columns
                        // This is a basic implementation - the UI has much more sophisticated parsing
                        var filterLower = filterExpression.ToLower();
                        foreach (var col in columnsToReturn)
                        {
                            var value = tableSnapshot.GetColumnValueString(i, col);
                            if (value?.ToLower().Contains(filterLower) == true)
                            {
                                includeRow = true;
                                break;
                            }
                        }
                    }

                    if (includeRow)
                    {
                        var eventDict = new Dictionary<string, object>();
                        eventDict["source"] = source.DisplayName;

                        foreach (var col in columnsToReturn)
                        {
                            eventDict[col.Name] = tableSnapshot.GetColumnValueString(i, col) ?? string.Empty;
                        }
                        events.Add(eventDict);
                    }
                }

                return new FilterResult { Events = events };
            }
            catch (Exception ex)
            {
                return new FilterResult { ErrorMessage = $"Error filtering trace data: {ex.Message}" };
            }
        }
    }
}
