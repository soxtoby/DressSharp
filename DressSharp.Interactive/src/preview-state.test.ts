import {expect, test} from "bun:test";
import {cursorPosition, PreviewRevision, previewPreferences, whitespaceMarkers} from "./preview-state";

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

test("cursor position reports the active end using one-based coordinates", () => {
    expect(cursorPosition([{start: {line: 1, character: 2}, end: {line: 3, character: 4}, direction: 1}])).toBe("Ln 4, Col 5");
    expect(cursorPosition([{start: {line: 1, character: 2}, end: {line: 3, character: 4}, direction: -1}])).toBe("Ln 2, Col 3");
    expect(cursorPosition([])).toBeNull();
});
