/** A place in a text as editors count it: lines from zero, then characters within the line. */
export interface Position {
    line: number;
    character: number;
}

/** Whole lines of a text, from start until end, replaced by other lines. */
export interface LineEdit {
    start: Position;
    end: Position;
    text: string;
}

/** Texts that differ by more lines than this are replaced in one edit, since telling which lines they share takes memory with the square of it. */
let maxChangedLines = 1000;

/**
 * The line edits that turn one text into another, leaving the lines they share alone so that an editor
 * keeps its cursor and markers on them. Line endings compare as line feeds, and the edits use line
 * feeds, which editors convert to the document's own line ending.
 */
export function lineEdits(before: string, after: string): LineEdit[] {
    let oldLines = lines(before);
    let newLines = lines(after);
    return changes(oldLines, newLines).map(change => ({
        start: position(oldLines, change.oldStart),
        end: position(oldLines, change.oldEnd),
        text: newLines.slice(change.newStart, change.newEnd).join(""),
    }));
}

/** The text split after each line feed, so that joining the lines gives the text back. */
function lines(text: string): string[] {
    return text.replace(/\r\n?/g, "\n").match(/[^\n]*\n|[^\n]+$/g) ?? [];
}

/** Where line `index` starts, or for the end of a text without a final line feed, where its last line ends. */
function position(lines: string[], index: number): Position {
    let last = lines.at(-1);
    return index < lines.length || !last || last.endsWith("\n")
        ? {line: index, character: 0}
        : {line: index - 1, character: last.length};
}

/** Lines oldStart until oldEnd become lines newStart until newEnd. */
interface Change {
    oldStart: number;
    oldEnd: number;
    newStart: number;
    newEnd: number;
}

function changes(oldLines: string[], newLines: string[]): Change[] {
    let start = 0;
    while (start < oldLines.length && start < newLines.length && oldLines[start] == newLines[start])
        start++;
    let oldEnd = oldLines.length;
    let newEnd = newLines.length;
    while (oldEnd > start && newEnd > start && oldLines[oldEnd - 1] == newLines[newEnd - 1]) {
        oldEnd--;
        newEnd--;
    }

    // Each change lies between two kept lines, counting the lines just outside both ends as kept.
    let shared = sharedLines(oldLines.slice(start, oldEnd), newLines.slice(start, newEnd)) ?? [];
    let kept: [number, number][] = [[start - 1, start - 1], ...shared.map(([o, n]): [number, number] => [o + start, n + start]), [oldEnd, newEnd]];
    return kept.slice(1)
        .map(([oldKept, newKept], index) => ({oldStart: kept[index]![0] + 1, oldEnd: oldKept, newStart: kept[index]![1] + 1, newEnd: newKept}))
        .filter(change => change.oldEnd > change.oldStart || change.newEnd > change.newStart);
}

/**
 * The lines both texts keep, as pairs of indexes in order, found by Myers' O(ND) difference algorithm;
 * undefined when the texts differ by more than maxChangedLines.
 */
function sharedLines(a: string[], b: string[]): [number, number][] | undefined {
    let limit = Math.min(a.length + b.length, maxChangedLines);
    let offset = limit + 1;
    // The furthest x reached so far on each diagonal k = x - y, at index k + offset.
    let furthest = new Int32Array(2 * offset + 1);
    let trace: Int32Array[] = [];
    let found = false;
    for (let d = 0; d <= limit && !found; d++) {
        for (let k = -d; k <= d && !found; k += 2) {
            let x = k == -d || (k != d && furthest[k - 1 + offset]! < furthest[k + 1 + offset]!)
                ? furthest[k + 1 + offset]!
                : furthest[k - 1 + offset]! + 1;
            let y = x - k;
            while (x < a.length && y < b.length && a[x] == b[y]) {
                x++;
                y++;
            }
            furthest[k + offset] = x;
            found = x >= a.length && y >= b.length;
        }
        trace.push(furthest.slice(offset - d, offset + d + 1));
    }
    return found ? backtrack(trace, a.length, b.length) : undefined;
}

/** Walks the edit path back from the ends of both texts, collecting the diagonal steps where a line is kept. */
function backtrack(trace: Int32Array[], aLength: number, bLength: number): [number, number][] {
    let shared: [number, number][] = [];
    let x = aLength;
    let y = bLength;
    for (let d = trace.length - 1; d > 0; d--) {
        // Diagonals -(d - 1) to d - 1, at index k + d - 1.
        let previous = trace[d - 1]!;
        let k = x - y;
        let inserted = k == -d || (k != d && previous[k - 2 + d]! < previous[k + d]!);
        let previousK = inserted ? k + 1 : k - 1;
        let previousX = previous[previousK + d - 1]!;
        let snakeStart = inserted ? previousX : previousX + 1;
        while (x > snakeStart) {
            x--;
            y--;
            shared.push([x, y]);
        }
        x = previousX;
        y = previousX - previousK;
    }
    while (x > 0) {
        x--;
        y--;
        shared.push([x, y]);
    }
    return shared.reverse();
}
