# Pierre fragment offsets

`@pierre/diffs@1.4.1.patch` keeps each rendered fragment's actual line column in
`data-char`. Intra-line highlighting can split one Shiki token into several spans;
copying the original token's column onto every fragment makes native selections
resolve to earlier characters. This patches the main-thread transformer used by
our offline preview; the preview does not use Pierre workers.

`tests/browser/preview-geometry.js` checks mouse selection against rendered glyph
bounds, including a split token and horizontally scrolled content. Re-run it when
upgrading Pierre and remove this patch when the upstream transformer preserves
fragment columns.
