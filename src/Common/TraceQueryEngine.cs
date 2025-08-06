using System.Linq;

namespace InstantTraceViewer
{
    /// <summary>
    /// Provides a centralized engine for querying trace data using the filtering logic.
    /// This ensures that queries from any source (UI or MCP server) are processed identically.
    /// </summary>
    public class TraceQueryEngine
    {
        public class QueryResult
        {
            public FilteredTraceTableSnapshot? Snapshot { get; init; }
            public string? ErrorMessage { get; init; }
            public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
        }

        /// <summary>
        /// Queries a trace source with a given filter expression.
        /// </summary>
        /// <param name="source">The trace source to query.</param>
        /// <param name="filterExpression">The filter expression string (e.g., '@Message contains "error"').</param>
        /// <returns>A QueryResult containing the filtered snapshot or an error message.</returns>
        public QueryResult Query(ITraceSource source, string filterExpression)
        {
            var fullSnapshot = source.CreateSnapshot();
            if (fullSnapshot is not ITraceTableSnapshot tableSnapshot)
            {
                return new QueryResult { Snapshot = null, ErrorMessage = "Could not create a table snapshot from the trace source." };
            }

            var rules = new ViewerRules();
            if (!string.IsNullOrWhiteSpace(filterExpression))
            {
                rules.AddRule(filterExpression, TraceRowRuleAction.Include);
                rules.ApplyFiltering = true;

                // Validate the syntax to provide a meaningful error.
                var firstRule = rules.Rules.FirstOrDefault();
                if (firstRule?.ParseResult?.Expression == null)
                {
                    string error = $"Invalid filter syntax. Expected one of: {string.Join(", ", firstRule.ParseResult.ExpectedTokens)}";
                    return new QueryResult { Snapshot = null, ErrorMessage = error };
                }
            }

            var filteredBuilder = new FilteredTraceTableBuilder();
            filteredBuilder.Update(rules, tableSnapshot);
            
            return new QueryResult { Snapshot = filteredBuilder.Snapshot() };
        }
    }
}
