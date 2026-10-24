import {expect, test} from "bun:test";
import {lineEdits, type LineEdit, type Position} from "./line-edits";

test.each([
    ["identical texts", "a\nb\n", "a\nb\n"],
    ["a changed line", "a\nb\nc\n", "a\nB\nc\n"],
    ["an inserted line", "a\nc\n", "a\nb\nc\n"],
    ["a removed line", "a\nb\nc\n", "a\nc\n"],
    ["reordered lines", "a\nb\nc\nd\n", "c\nd\na\nb\n"],
    ["an added final line feed", "a\nb", "a\nb\n"],
    ["a removed final line feed", "a\nb\n", "a\nb"],
    ["a line added after an unterminated last line", "a", "a\nb"],
    ["an empty text filled", "", "a\nb\n"],
    ["a text emptied", "a\nb\n", ""],
    ["blank lines collapsed", "a\n\n\n\nb\n", "a\n\nb\n"],
])("lineEdits for %s turn the old text into the new", (_, before, after) => {
    let result = lineEdits(before, after);

    expect(apply(before, result)).toBe(after);
});

test("lineEdits for CRLF text compare line endings as line feeds", () => {
    let result = lineEdits("a\r\nb\r\nc\r\n", "a\nB\nc\n");

    expect(result).toEqual([{start: {line: 1, character: 0}, end: {line: 2, character: 0}, text: "B\n"}]);
});

test("lineEdits for scattered changes leave the lines between them alone", () => {
    let before = "class C\n{\n    void M(){}\n    int f;\n    void N(){}\n}\n";
    let after = "class C\n{\n    void M() { }\n    int f;\n    void N() { }\n}\n";

    let result = lineEdits(before, after);

    expect(result).toEqual([
        {start: {line: 2, character: 0}, end: {line: 3, character: 0}, text: "    void M() { }\n"},
        {start: {line: 4, character: 0}, end: {line: 5, character: 0}, text: "    void N() { }\n"},
    ]);
});

test("lineEdits for texts that share no lines over thousands of lines still turn the old text into the new", () => {
    let before = Array.from({length: 3000}, (_, index) => `old ${index}\n`).join("");
    let after = Array.from({length: 3000}, (_, index) => `new ${index}\n`).join("");

    let result = lineEdits(before, after);

    expect(apply(before, result)).toBe(after);
});

/** Applies edits the way an editor does, to the text with line feeds for line endings. */
function apply(text: string, edits: LineEdit[]): string {
    let normalized = text.replace(/\r\n?/g, "\n");
    let lineStarts = [0];
    for (let index = normalized.indexOf("\n"); index >= 0; index = normalized.indexOf("\n", index + 1))
        lineStarts.push(index + 1);
    let offset = (position: Position) => lineStarts[position.line]! + position.character;

    let result = normalized;
    for (let edit of edits.toReversed())
        result = result.slice(0, offset(edit.start)) + edit.text + result.slice(offset(edit.end));
    return result;
}
