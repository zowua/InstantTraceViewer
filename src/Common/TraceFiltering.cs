using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace InstantTraceViewer
{
    public enum TraceRowRuleAction
    {
        Include,
        Exclude
    }

    public interface IRule
    {
        public string Query { get; }
        public TraceRowRuleAction Action { get; }
        public bool Enabled { get; }

        // The result of parsing the query.
        public TraceTableRowSelectorParseResults ParseResult { get; }

        // Predicate is compiled from the query if successful.
        public TraceTableRowSelector? Predicate { get; }
    }

    public class ViewerRules
    {
        class Rule : IRule
        {
            public required string Query { get; init; }
            public required TraceRowRuleAction Action { get; init; }

            public bool Enabled { get; set; } = true;

            // The result of parsing the query.
            public TraceTableRowSelectorParseResults ParseResult { get; set; }

            // Predicate is compiled from the query if successful.
            public TraceTableRowSelector? Predicate { get; set; }
        }

        private List<Rule> _visibleRules = new();

        private int _visibleRulePredicatesRuleGenerationId = -1;
        private int _visibleRulePredicatesTableGenerationId = -1;

        // Bumping this id will trigger a complete rebuild of the filtered trace table.
        public int GenerationId { get; private set; } = 1;

        public bool _applyFiltering = true;
        public bool ApplyFiltering
        {
            get => _applyFiltering;
            set
            {
                _applyFiltering = value;
                GenerationId++;
            }
        }

        public void ClearRules()
        {
            _visibleRules.Clear();
            GenerationId++;
        }

        public void AddRule(string query, TraceRowRuleAction ruleAction)
        {
            if (ruleAction == TraceRowRuleAction.Include)
            {
                // Include rules go last to ensure anything already excluded stays excluded.
                _visibleRules.Add(new Rule { Query = query, Action = TraceRowRuleAction.Include });
            }
            else if (ruleAction == TraceRowRuleAction.Exclude)
            {
                // Exclude rules go first to ensure they exclude things that might be matched by a preexisting include rule.
                _visibleRules.Insert(0, new Rule { Query = query, Action = TraceRowRuleAction.Exclude });
            }
            GenerationId++;
        }

        public void AppendRule(bool enabled, TraceRowRuleAction action, string query)
        {
            _visibleRules.Add(new Rule { Query = query, Enabled = enabled, Action = action });
            GenerationId++;
        }

        public void UpdateRule(int index, string query)
        {
            Rule oldRule = _visibleRules[index];
            _visibleRules[index] = new Rule { Query = query, Action = oldRule.Action, Enabled = oldRule.Enabled };
            GenerationId++;
        }

        public void RemoveRule(int index)
        {
            _visibleRules.RemoveAt(index);
            GenerationId++;
        }

        public void MoveRule(int index, int newIndex)
        {
            var rule = _visibleRules[index];
            _visibleRules.RemoveAt(index);
            _visibleRules.Insert(newIndex, rule);
            GenerationId++;
        }

        public void SetRuleEnabled(int index, bool enabled)
        {
            _visibleRules[index].Enabled = enabled;
            GenerationId++;
        }

        public IReadOnlyList<IRule> Rules => _visibleRules;

        public TraceRowRuleAction GetVisibleAction(ITraceTableSnapshot traceTable, int unfilteredRowIndex)
        {
            if (_visibleRules.Count == 0 || !ApplyFiltering)
            {
                return TraceRowRuleAction.Include;
            }

            EnsureVisibleRulePredicates(traceTable);

            TraceRowRuleAction defaultAction = TraceRowRuleAction.Include;
            foreach (var rule in _visibleRules)
            {
                if (rule.Predicate == null)
                {
                    continue; // This rule could not be parsed.
                }

                if (!rule.Enabled)
                {
                    continue;
                }

                if (rule.Predicate(traceTable, unfilteredRowIndex))
                {
                    return rule.Action;
                }

                // If user is explicitly including things, then exclude anything unmatched.
                // Thus if user only excludes things, then include anything unmatched.
                if (rule.Action == TraceRowRuleAction.Include)
                {
                    defaultAction = TraceRowRuleAction.Exclude;
                }
            }
            return defaultAction;
        }

        public ViewerRules Clone()
        {
            return new ViewerRules
            {
                _visibleRules = _visibleRules.ToList(),
                _applyFiltering = _applyFiltering,
                GenerationId = GenerationId
            };
        }

        private void EnsureVisibleRulePredicates(ITraceTableSnapshot traceTable)
        {
            if (_visibleRulePredicatesRuleGenerationId != GenerationId ||
                _visibleRulePredicatesTableGenerationId != traceTable.GenerationId)
            {
                Trace.WriteLine("Recompiling query predicates...");
                var parser = new TraceTableRowSelectorSyntax(traceTable.Schema);
                foreach (var rule in _visibleRules)
                {
                    rule.ParseResult = parser.Parse(rule.Query);
                    rule.Predicate = rule.ParseResult.Expression?.Compile();
                    rule.Enabled &= (rule.Predicate != null); // Disable if the rule could not be parsed.
                }
            }

            _visibleRulePredicatesRuleGenerationId = GenerationId;
            _visibleRulePredicatesTableGenerationId = traceTable.GenerationId;
        }
    }

    // This object tracks the list of rows that are included by the user's rules.
    // It updates itself incrementally to avoid having to rebuild the view from scratch every frame.
    public class FilteredTraceTableBuilder
    {
        // Holds the row indices of the trace records that are included by the user's rules.
        private ListBuilder<int> _visibleRowsBuilder = new();
        private int _generationId = 0;

        private int _lastViewerRulesGenerationId = -1;
        protected ITraceTableSnapshot? _lastUnfilteredTraceRecordSnapshot = null;
        protected int _errorCount = 0;

        public bool Update(ViewerRules viewerRules, ITraceTableSnapshot newSnapshot)
        {
            bool rebuildFilteredView =
                newSnapshot.GenerationId != (_lastUnfilteredTraceRecordSnapshot?.GenerationId ?? 0) ||
                viewerRules.GenerationId != _lastViewerRulesGenerationId;
            if (rebuildFilteredView)
            {
                Debug.WriteLine("Rebuilding visible rows...");
                _visibleRowsBuilder = new ListBuilder<int>();
                _lastUnfilteredTraceRecordSnapshot = null;
                _errorCount = 0;
                _generationId++;
            }

            for (int i = (_lastUnfilteredTraceRecordSnapshot?.RowCount ?? 0); i < newSnapshot.RowCount; i++)
            {
                if (viewerRules.GetVisibleAction(newSnapshot, i) == TraceRowRuleAction.Include)
                {
                    if (newSnapshot.Schema.UnifiedLevelColumn != null)
                    {
                        UnifiedLevel level = newSnapshot.GetUnifiedLevel(i);
                        if (level == UnifiedLevel.Error || level == UnifiedLevel.Fatal)
                        {
                            _errorCount++;
                        }
                    }

                    _visibleRowsBuilder.Add(i);
                }
            }

            if (rebuildFilteredView)
            {
                Debug.WriteLine("Done rebuilding visible rows.");
            }

            _lastUnfilteredTraceRecordSnapshot = newSnapshot;
            _lastViewerRulesGenerationId = viewerRules.GenerationId;

            return rebuildFilteredView;
        }

        public FilteredTraceTableSnapshot Snapshot()
        {
            return new FilteredTraceTableSnapshot(_lastUnfilteredTraceRecordSnapshot, _visibleRowsBuilder.CreateSnapshot(), _generationId, _errorCount);
        }
    }

    /// <summary>
    /// A read-only view of a full table but with only the rows that are included by the user's rules.
    /// </summary>
    public class FilteredTraceTableSnapshot : ITraceTableSnapshot
    {
        private readonly IReadOnlyList<int> _visibleRowIndiciesSnapshot;

        public FilteredTraceTableSnapshot(ITraceTableSnapshot fullTraceTableSnapshot, IReadOnlyList<int> visibleRowIndiciesSnapshot, int generationId, int errorCount)
        {
            _visibleRowIndiciesSnapshot = visibleRowIndiciesSnapshot;
            FullTable = fullTraceTableSnapshot;
            ErrorCount = errorCount;
            GenerationId = generationId;
        }

        public int ErrorCount { get; private init; }

        public ITraceTableSnapshot FullTable { get; private init; }

        public int GetFullTableRowIndex(int filteredRowIndex)
            => _visibleRowIndiciesSnapshot[filteredRowIndex];

        public string GetColumnValueString(int filteredRowIndex, TraceSourceSchemaColumn column, bool allowMultiline = false)
            => FullTable.GetColumnValueString(GetFullTableRowIndex(filteredRowIndex), column, allowMultiline);

        public string GetColumnValueNameForId(int filteredRowIndex, TraceSourceSchemaColumn column)
            => FullTable.GetColumnValueNameForId(GetFullTableRowIndex(filteredRowIndex), column);

        public int GetColumnValueInt(int filteredRowIndex, TraceSourceSchemaColumn column)
            => FullTable.GetColumnValueInt(GetFullTableRowIndex(filteredRowIndex), column);

        public DateTime GetColumnValueDateTime(int filteredRowIndex, TraceSourceSchemaColumn column)
            => FullTable.GetColumnValueDateTime(GetFullTableRowIndex(filteredRowIndex), column);

        public UnifiedLevel GetColumnValueUnifiedLevel(int filteredRowIndex, TraceSourceSchemaColumn column)
            => FullTable.GetColumnValueUnifiedLevel(GetFullTableRowIndex(filteredRowIndex), column);

        public UnifiedOpcode GetColumnValueUnifiedOpcode(int filteredRowIndex, TraceSourceSchemaColumn column)
            => FullTable.GetColumnValueUnifiedOpcode(GetFullTableRowIndex(filteredRowIndex), column);

        public TraceTableSchema Schema => FullTable.Schema;

        public int RowCount => _visibleRowIndiciesSnapshot.Count;

        public int GenerationId { get; private set; }
    }
}
