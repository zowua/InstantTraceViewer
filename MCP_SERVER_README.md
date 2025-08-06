# InstantTraceViewer MCP Server

This project extends InstantTraceViewer with Model Context Protocol (MCP) server capabilities, allowing LLMs to query and analyze trace data from ETL and Perfetto files using the same powerful filtering engine as the UI.

## Architecture

The MCP server is designed with clean separation of concerns:

- **Common Project**: Shared code between UI and Server
  - `TraceQueryEngine`: Centralized query processing using advanced filtering logic
  - `ViewerRules` & `FilteredTraceTableBuilder`: Filtering and table building components
  - `IEmbeddedMcpServer`: Interface for server lifecycle management
- **Server Project**: MCP-specific implementation
  - `TraceDataProvider`: MCP tools using attribute-based pattern
  - `EmbeddedMcpServer`: Server lifecycle and hosting
  - `TraceManager`: Manages loaded trace sources
- **UI Project**: References both Common and Server for MCP integration

## Available Tools

### `QueryWithSyntax` - Advanced Filtering

The primary and most powerful query tool that supports the full ETL query syntax.

**Parameters:**
- `filterExpression` (optional): Filter using ETL query syntax. If empty, returns all events
- `maxResults` (optional): Maximum results to return (default: 100)  
- `columns` (optional): Specific columns to include in results

**Examples:**
- `@Level >= Error`
- `@Message contains "exception"`
- `(@Process == "myapp.exe") and (@Level >= Warning)`

### `GetTraceSummary` - Overview

Get summary information about loaded traces including row counts, available columns, and trace statistics.

## Filter Syntax Support

The MCP server supports the **exact same** advanced filter syntax as the InstantTraceViewer UI:

### Column References
- Use `@ColumnName` to reference columns (e.g., `@Message`, `@Process`, `@Level`)

### String Operations
- `contains` - Case insensitive substring match  
- `contains_cs` - Case sensitive substring match
- `matches` - Wildcard match with *, ?
- `matches regex` - Regular expression match
- `==`, `=~` - Equality (case sensitive/insensitive)
- `!=`, `!~` - Inequality (case sensitive/insensitive)

### Comparison Operations
- `<`, `<=`, `>`, `>=` - Numeric/date comparisons
- `in` - List membership: `@Level in [Error, Critical]`

### Logical Operations  
- `and`, `or`, `not` - Logical operators
- `()` - Parentheses for grouping

## Usage

1. **Start the Application**: Launch InstantTraceViewer as usual
2. **Load Trace Files**: Open ETL, Perfetto, or CSV files through the UI  
3. **MCP Server**: The server automatically starts using STDIO transport
4. **Query Data**: Use an MCP-compatible LLM client to connect and query the trace data

## Example Queries

With an MCP-compatible LLM, you can ask:

- "Show me all error events in the trace"
- "Find events from process myapp.exe with warnings or higher"
- "What processes are represented in this trace?"
- "Give me a summary of the loaded trace files"
- "Show recent events containing 'memory' in the message"

## Architecture Benefits

✅ **Zero Code Duplication**: MCP server uses the exact same filtering logic as the UI  
✅ **Consistency Guaranteed**: Queries behave identically between UI and MCP  
✅ **Maintainability**: Shared code in Common project, single source of truth  
✅ **Performance**: Direct access to loaded trace data, no serialization overhead  
✅ **Full Feature Support**: Complete access to the UI's powerful filtering capabilities  

## Technical Implementation

- **Official SDK**: Uses `ModelContextProtocol.AspNetCore` v0.3.0-preview.3
- **Attribute-Based Tools**: `[McpServerTool]` attributes for tool discovery
- **STDIO Transport**: Standard MCP communication protocol
- **Dependency Injection**: Clean service registration and lifecycle management
- **Shared Services**: UI and server use the same filtering and query components

## Integration

The MCP server is seamlessly integrated into the main application:

1. When trace files are opened, their data sources are registered with the TraceManager
2. MCP tools can immediately query the loaded data using the same engine as the UI
3. The server lifecycle is managed by the main application process

## Dependencies

- **ModelContextProtocol.AspNetCore** (0.3.0-preview.3) - Official MCP SDK
- **Common Project** - Shared trace processing logic
- **Existing InstantTraceViewer dependencies** - ETL parsing, Perfetto support, etc.
