# Interactive configuration is local and catalog-driven

`dotnet dress interactive` hosts an offline browser UI on a random loopback port using a narrowly configured `HttpListener`, avoiding an ASP.NET Core runtime requirement for the CLI tool. Its `--config` option selects the target EditorConfig; the existing MSBuild option retains only its unambiguous `--configuration` name. The versioned Rule catalog is the authoritative source for every supported preference and its interactive documentation; the detailed Markdown rule reference is retired. The interactive configuration edits exact `[*.cs]` assignments in one discovered or specified EditorConfig, previews all pending preferences against transient C# as a side-by-side diff, and merges only user-changed preferences into the latest file when saving.

Write endpoints require an unguessable, per-launch CSRF token in a request header. Read endpoints and embedded static assets remain public on the random loopback listener; the token is not placed in URLs.

The repository's DotNetDo build orchestration runs Bun before every .NET build and generates the browser assets without committing `dist`. MSBuild never invokes Bun, and there is no frontend-skip build option.
