import {expect, test} from "bun:test";
import {diffWithContext} from "./diff-context";

test("identical example files retain all context lines", () => {
    const file = {name: "Example.cs", contents: "class C\n{\n}\n"};
    const diff = diffWithContext(file, file);
    expect(diff.splitLineCount).toBe(3);
    expect(diff.hunks[0]?.hunkContent).toEqual([{type: "context", lines: 3, additionLineIndex: 0, deletionLineIndex: 0}]);
});

test("changed example files retain additions and deletions", () => {
    const diff = diffWithContext({name: "Example.cs", contents: "Call(1,2);"}, {name: "Example.cs", contents: "Call(1, 2);"});
    expect(diff.hunks[0]?.additionLines).toBe(1);
    expect(diff.hunks[0]?.deletionLines).toBe(1);
});
