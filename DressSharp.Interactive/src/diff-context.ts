import {parseDiffFromFile, type FileContents} from "@pierre/diffs";

export function diffWithContext(oldFile: FileContents, newFile: FileContents) {
    const diff = parseDiffFromFile(oldFile, newFile);
    // Identical files still need visible code in previews and examples.
    if (oldFile.contents === newFile.contents && newFile.contents.length > 0) {
        const lines = diff.additionLines.length;
        diff.hunks = [{
            collapsedBefore: 0, additionStart: 1, deletionStart: 1,
            additionCount: lines, deletionCount: lines, additionLines: 0, deletionLines: 0,
            additionLineIndex: 0, deletionLineIndex: 0,
            hunkContent: [{type: "context", lines, additionLineIndex: 0, deletionLineIndex: 0}],
            splitLineStart: 0, unifiedLineStart: 0, splitLineCount: lines, unifiedLineCount: lines,
            noEOFCRAdditions: !newFile.contents.endsWith("\n"), noEOFCRDeletions: !oldFile.contents.endsWith("\n"),
        }];
        diff.splitLineCount = lines;
        diff.unifiedLineCount = lines;
    }
    return diff;
}
