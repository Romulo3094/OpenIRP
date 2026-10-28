<!-- codebase-memory-mcp:start -->
# Codebase Knowledge Graph (codebase-memory-mcp)

ALWAYS prefer MCP graph tools over grep/glob/file-search for code discovery.

## Priority Order
1. `search_graph` — find functions, classes, routes, variables by pattern
2. `trace_path` — trace who calls a function or what it calls
3. `get_code_snippet` — read specific function/class source code
4. `query_graph` — run Cypher queries for complex patterns
5. `get_architecture` — high-level project summary

Run `index_repository` first if the project is not indexed.
Fall back to text/file search for string literals, error messages, configuration,
non-code files, or when MCP results are insufficient.
<!-- codebase-memory-mcp:end -->

## graphify

Graphify complements codebase-memory-mcp with a local graph, community analysis,
and an interactive HTML visualization in `graphify-out/`.
Keep the MCP discovery priority above. Use Graphify when explicitly requested,
for its visualization/community analysis, or when MCP results are insufficient.

- When the user invokes `/graphify`, read and follow the installed Graphify skill.
- If `graphify-out/graph.json` exists, use `graphify query "<question>"`,
  `graphify path "<A>" "<B>"`, or `graphify explain "<concept>"` for scoped results.
- After code changes, run `graphify update .` from the project root to refresh
  the structural graph (AST only; no external AI API calls).
- After updating, run `graphify export html` to refresh the visualization.
- `.graphifyignore` defines the scan scope. Keep dependencies, generated output,
  local configuration, and secrets excluded.
- Graph artifacts are local and ignored by Git. Build the initial graph with
  `graphify update .` if it is missing. Documentation/image semantic extraction
  is a separate operation through the Graphify skill.
