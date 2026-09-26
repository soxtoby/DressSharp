# Interactive preview design

Agreed design implemented for SOX-155.

## Issue requirements

Use Pierre for editable source on the left and read-only formatted output on the right, with red source changes, green output changes, intra-line highlights, and C# syntax highlighting. The diff fills the available space; panes stack on narrow screens. Preview runs the real formatter under all pending preferences, never saves source, and keeps errors recoverable in the browser.

## Agreed behavior

Opening interactive configuration shows a bundled, editable C# sample so users can immediately see how preferences affect code. Users can replace it by pasting their own code.

The existing interactive configuration contract applies: preview source is transient and never saved; formatted output reflects all pending preferences.

Source edits and preference changes automatically refresh the preview after a short pause. Only the result for the latest source and pending preferences is displayed.

External EditorConfig changes do not change the preview until Reload. Reload incorporates the refreshed configuration while retaining pending edits, keeping the controls and preview consistent.

Incomplete or malformed C# receives the formatter's normal best-effort handling: safe regions can still be formatted. A compact notice reports skipped transformations.

When formatting fails or a pending preference is invalid, retain the last successful output, clearly marked outdated, and show an inline error. Preserve source and pending edits; changes automatically trigger another attempt.

Preview uses the latest stable C# version supported by the bundled formatter, with no preprocessor symbols or project selection. A compact label identifies this context; `#if DEBUG` branches are inactive unless the source defines DEBUG itself.

Treat browser source as UTF-8 without BOM. Show compact encoding and line-ending metadata alongside the diff so representation-only changes remain visible. The whitespace toggle reveals spaces, tabs, and line endings without changing either text.

While output is outdated, suppress change highlights rather than comparing it against newer source. Retain the last output with its outdated label until fresh output arrives.

The bundled sample and editor source use LF. Accept Pierre's normalization of pasted line endings to LF and label the source accordingly. Users can preview LF-to-CRLF formatting changes. This replaces the earlier pasted-line-ending preservation decision, avoiding custom paste handling.

## Implementation verification

Pierre 1.3.6 uses a stable preview identity with explicit render-cache invalidation, retaining editor history when output refreshes. Its empty-document edit mode keeps a caret available after all source is deleted. Non-mutating CSS decorations display whitespace; the JavaScript highlighter bundles only C#. The loopback server permits generated editor styles while retaining same-origin scripts and connections.

Exercise the real formatter with pending assignments and inherited values, malformed code, representation-only changes, and recoverable errors. Verify rapid edits cannot display an older response; external changes wait for Reload; save and polling preserve editor state; and the layout works with stacked panes. Preview formatting must remain in memory and keep configuration operations responsive.

`./do.cmd test` runs the TypeScript and .NET checks. `DressSharp.Interactive/e2e/interactive-preview.js` is a Playwright `run-code` function for an already-open local application using a disposable EditorConfig. It exercises actual editing, formatting, whitespace, undo, polling, save, failure recovery, empty source, narrow panes, and offline requests. It changes the disposable configuration when testing Save.
