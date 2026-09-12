# Interactive rule examples

SOX-156 exposes each rule's example through an icon beside its caption, revealed on hover or keyboard focus alongside Copy. Touch devices always show the icons.

The dismissible, nonmodal panel identifies the rule by caption and EditorConfig key. It shows read-only Input code and Output in a Pierre split diff, with C# syntax highlighting, red/green changes, and intra-line highlights, starting with the rule's Default value. Value controls support choices, integers, selections, and permutations. Each example contains the syntax its rule controls. Shared settings have explicit companion preferences in the catalog, displayed in the panel, so indentation size and maximum line length demonstrate real formatting changes. Examples remain independent of pending preferences.

A Whitespace toggle reveals spaces, tabs, and line endings using non-mutating decorations, preserving copyable code. It starts enabled for settings whose changes would otherwise be invisible. Encoding, line endings, and final-newline metadata expose representation-only changes; identical code is labelled Code unchanged.

Examples use the existing in-memory preview endpoint and never write configuration or replace preview source. Closing the panel cancels its request; changing the value cancels the previous request. Invalid values and request errors remain recoverable. Configuration polling preserves the open panel.

Buttons support keyboard activation. Escape or Close dismisses the panel and restores focus to the initiating button. Code panes support keyboard scrolling. The panel fits the viewport, and source/output stack on narrow screens.

## Verification

`InteractiveHttpServerTests.Embedded_application_and_catalog_are_public_and_offline` checks that bootstrap exposes each catalog example unchanged.

`RuleExampleTests` runs every rule's example through the real formatter with contrasting values and its companion preferences, asserting distinct results and no skipped transformations. This catches irrelevant sample code and missing prerequisites rather than merely checking that examples render.

`tests/browser/rule-examples.js` is a Playwright `run-code` function for a rebuilt interactive application with a disposable EditorConfig. It checks every rule's default example, isolated formatting requests, outcome switching, keyboard access, focus after polling, invalid values, request recovery, representation metadata, narrow geometry, and preservation of preview source, pending edits, and saved configuration. It leaves a pending preference edit but does not save it.
