import {expect, test} from "bun:test";
import {PreviewRevision, previewPreferences, whitespaceMarkers} from "./preview-state";

test("pending assignments retain the loaded inheritance instead of stale effective values", () => {
    const inherited = {kind: "explicit", value: "8"} as const;
    const preferences = previewPreferences({targetPath: ".editorconfig", interactiveRoot: ".", revision: "loaded", preferences: [
        {key: "tab_width", local: {kind: "explicit", value: "4"}, inherited, inheritedSourcePath: null, effectiveValue: "4", effectiveSourcePath: null},
    ]}, new Map([["tab_width", {kind: "absent", value: null}]]));
    expect(preferences).toEqual([{key: "tab_width", local: {kind: "absent", value: null}, inherited}]);
});

test("editing during debounce invalidates an in-flight result", () => {
    const revisions = new PreviewRevision();
    const first = revisions.next();
    const latest = revisions.next();
    expect(revisions.isCurrent(first)).toBe(false);
    expect(revisions.isCurrent(latest)).toBe(true);
});

test("whitespace decorations preserve tab alignment without copying source text", () => {
    expect(whitespaceMarkers("a \tb\n")).toBe("\u00a0·→\u00a0\u00a0");
});
