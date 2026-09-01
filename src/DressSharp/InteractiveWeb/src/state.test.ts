import {describe, expect, test} from "bun:test";
import {matchesRule, setEdit, validValue, type PreferenceSnapshot} from "./state";

const preference: PreferenceSnapshot = {
    key: "indent_style",
    local: {kind: "explicit", value: "space"},
    inherited: {kind: "absent", value: null},
    inheritedSourcePath: null,
    effectiveValue: "space",
    effectiveSourcePath: "C:/repo/.editorconfig",
};

describe("interactive state", () => {
    test("drops an edit when manually restored", () => {
        const changed = setEdit(new Map(), preference, {kind: "explicit", value: "tab"});
        expect(changed.size).toBe(1);
        expect(setEdit(changed, preference, {kind: "explicit", value: "space"}).size).toBe(0);
    });

    test("search includes hidden keys, descriptions, and values", () => {
        const rule = {
            name: "Indent style",
            key: "indent_style",
            description: "Controls indentation",
            values: [{value: "space"}, {value: "tab"}],
            specialValues: [],
        };
        expect(matchesRule("indent_style", rule)).toBe(true);
        expect(matchesRule("controls", rule)).toBe(true);
        expect(matchesRule("tab", rule)).toBe(true);
        expect(matchesRule("brace", rule)).toBe(false);
    });

    test("validates integers, selections, and permutations", () => {
        expect(validValue("4", {valueKind: "integer", values: [], minimum: 1, specialValues: ["tab"]})).toBe(true);
        expect(validValue("0", {valueKind: "integer", values: [], minimum: 1, specialValues: ["tab"]})).toBe(false);
        expect(validValue("a,b", {valueKind: "multiplechoice", values: [{value: "a"}, {value: "b"}], minimum: null, specialValues: []})).toBe(true);
        expect(validValue("a,a", {valueKind: "multiplechoice", values: [{value: "a"}], minimum: null, specialValues: []})).toBe(false);
        expect(validValue("b,a", {valueKind: "permutation", values: [{value: "a"}, {value: "b"}], minimum: null, specialValues: []})).toBe(true);
    });
});
