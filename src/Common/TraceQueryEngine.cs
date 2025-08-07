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
                // Validate the syntax first by parsing it ourselves
                var parser = new TraceTableRowSelectorSyntax(tableSnapshot.Schema);
                var parseResult = parser.Parse(filterExpression);
                if (parseResult.Expression == null)
                {
                    var expectedTokens = parseResult.ExpectedTokens;
                    string expectedText = expectedTokens != null && expectedTokens.Count > 0 ? 
                        string.Join(", ", expectedTokens) : "valid expression";
                    string error = $"Invalid filter syntax. Expected one of: {expectedText}";
                    return new QueryResult { Snapshot = null, ErrorMessage = error };
                }

                rules.AddRule(filterExpression, TraceRowRuleAction.Include);
                rules.ApplyFiltering = true;
            }

            var filteredBuilder = new FilteredTraceTableBuilder();
            filteredBuilder.Update(rules, tableSnapshot);
            
            return new QueryResult { Snapshot = filteredBuilder.Snapshot() };
        }
    }
}
