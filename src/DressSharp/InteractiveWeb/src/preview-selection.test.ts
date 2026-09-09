import {afterAll, beforeAll, expect, mock, test} from "bun:test";
import {Preview} from "./preview";

const globals = ["document", "Element", "requestAnimationFrame"] as const;
const descriptors = globals.map(key => Object.getOwnPropertyDescriptor(globalThis, key));
beforeAll(() => {
    Object.defineProperty(globalThis, "document", {configurable: true, value: {getSelection: () => null}});
    Object.defineProperty(globalThis, "Element", {configurable: true, value: class {}});
    Object.defineProperty(globalThis, "requestAnimationFrame", {configurable: true, value: (callback: FrameRequestCallback) => { callback(0); return 1; }});
});
afterAll(() => {
    globals.forEach((key, index) => {
        const descriptor = descriptors[index];
        if (descriptor) Object.defineProperty(globalThis, key, descriptor);
        else Reflect.deleteProperty(globalThis, key);
    });
});

function previewSelection(start: number, end: number) {
    const current = {start: {line: 2, character: start}, end: {line: 2, character: end}, direction: 1};
    const onSelectionRules = mock(() => {});
    const preview = Object.assign(Object.create(Preview.prototype), {
        host: {shadowRoot: {}}, stale: false,
        editor: {getState: () => ({selections: [current]})},
        selectionAction: {}, onSelectionRules, selectionRulesPinned: false,
        queueMarkers: mock(() => {}),
    });
    return {preview, current, onSelectionRules};
}

test("a source caret is not a code selection", () => {
    const {preview, onSelectionRules} = previewSelection(3, 3);
    preview.captureSelection();
    expect(preview.selection).toBeUndefined();
    expect(onSelectionRules).not.toHaveBeenCalled();
});

test("keyboard selection updates do not rebuild the settings UI", () => {
    const {preview, current, onSelectionRules} = previewSelection(3, 4);
    preview.captureSelection();
    current.end.line = 3;
    preview.captureSelection();
    expect(preview.selection).toEqual({start: 2, end: 4, side: "source"});
    expect(onSelectionRules).not.toHaveBeenCalled();
    current.end = {...current.start};
    preview.captureSelection();
    expect(preview.selection).toBeUndefined();
    expect(preview.selectionAction.disabled).toBe(true);
});

test("marker updates do not write line selections back into the editor", () => {
    const setSelectedLines = mock(() => {});
    const preview = Object.assign(Object.create(Preview.prototype), {
        selection: {start: 2, end: 4, side: "source"}, selectionSummary: {}, markerFrame: 0,
        updateHorizontalScroll: mock(() => {}), updateOverview: mock(() => {}), updateMarkers: mock(() => {}),
        diff: {setSelectedLines},
    });
    preview.queueMarkers();
    expect(setSelectedLines).not.toHaveBeenCalled();
});

test("changing selection clears a pinned rule filter once", () => {
    const {preview, current, onSelectionRules} = previewSelection(3, 4);
    preview.selectionRulesPinned = true;
    preview.captureSelection();
    current.end.line = 3;
    preview.captureSelection();
    expect(onSelectionRules).toHaveBeenCalledTimes(1);
    expect(onSelectionRules).toHaveBeenCalledWith(null, "");
});
