import {expect, test} from "bun:test";
import {affectsSelection, findSelectionRules, outputSelection} from "./selection-rules";

test("source selection follows wrapping and earlier inserted lines", () => {
    const source = "header\nif (true) return false;\ntail\n";
    const output = "header\nextra\nif (true)\n    return false;\ntail\n";
    expect(outputSelection(source, output, {start: 1, end: 2, side: "source"}))
        .toEqual({start: 1, end: 4, side: "output"});
    expect(outputSelection(source, output, {start: 2, end: 3, side: "source"}))
        .toEqual({start: 4, end: 5, side: "output"});
});

test("unrelated earlier edits do not implicate a selected output line", () => {
    const output = "header\nextra\nif (true)\n    return false;\ntail\n";
    const selected = {start: 3, end: 4, side: "output"} as const;
    expect(affectsSelection(output, "header\nif (true)\n    return false;\ntail\n", selected)).toBe(false);
    expect(affectsSelection(output, "header\nextra\nif (true) return false;\ntail\n", selected)).toBe(true);
    expect(affectsSelection(output, output, selected)).toBe(false);
});

test("an output selection follows wrapping changes within the output pane", () => {
    const source = "if (true) return false;\ntail\n";
    const wrapped = "if (true)\n    return false;\ntail\n";
    const compact = outputSelection(wrapped, source, {start: 1, end: 2, side: "source"});
    expect(compact).toEqual({start: 0, end: 1, side: "output"});
    expect(outputSelection(source, wrapped, {...compact, side: "source"})).toEqual({start: 0, end: 2, side: "output"});
});

test("removing a selected source line retains a boundary to inspect", () => {
    const selected = outputSelection("a\nremoved\nb\n", "a\nb\n", {start: 1, end: 2, side: "source"});
    expect(selected).toEqual({start: 1, end: 1, side: "output"});
    expect(affectsSelection("a\nb\n", "a\nremoved\nb\n", selected)).toBe(true);
});

test("analysis disables inherited settings with unset and ignores inactive settings", async () => {
    const absent = {kind: "absent", value: null} as const;
    const explicit = {kind: "explicit", value: "next_line"} as const;
    const preferences = [
        {key: "placement", local: absent, inherited: explicit},
        {key: "unrelated", local: explicit, inherited: absent},
        {key: "inactive", local: {kind: "unset", value: null} as const, inherited: explicit},
    ];
    const calls: string[] = [];
    const keys = await findSelectionRules("if (true) return false;\n", "if (true)\n    return false;\n", preferences,
        {start: 1, end: 2, side: "output"}, async variant => {
            const disabled = variant.find(item => item.key !== "inactive" && item.local.kind === "unset")!;
            calls.push(disabled.key);
            return disabled.key === "placement" ? "if (true) return false;\n" : "if (true)\n    return false;\n";
        }, new AbortController().signal);
    expect([...keys]).toEqual(["placement"]);
    expect(calls.sort()).toEqual(["placement", "unrelated"]);
    expect(preferences[0]!.local).toEqual(absent);
});

test("cancelled analysis never returns stale matches", async () => {
    const controller = new AbortController();
    await expect(findSelectionRules("a", "b", [{key: "rule", local: {kind: "explicit", value: "true"}, inherited: {kind: "absent", value: null}}],
        {start: 0, end: 1, side: "output"}, async () => { controller.abort(); return "a"; }, controller.signal)).rejects.toThrow();
});
