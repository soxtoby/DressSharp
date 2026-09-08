import {parseDiffFromFile, type ChangeContent} from "@pierre/diffs";
import type {previewPreferences} from "./preview-state";

export type LineSelection = {start: number; end: number; side: "source" | "output"};
type Preferences = ReturnType<typeof previewPreferences>;

function changes(before: string, after: string) {
    const result: ChangeContent[] = [];
    for (const block of parseDiffFromFile({name: "Preview.cs", contents: before}, {name: "Preview.cs", contents: after})
        .hunks.flatMap(hunk => hunk.hunkContent)) {
        if (block.type !== "change") continue;
        const previous = result.at(-1);
        if (previous && previous.deletionLineIndex + previous.deletions === block.deletionLineIndex
            && previous.additionLineIndex + previous.additions === block.additionLineIndex) {
            previous.deletions += block.deletions;
            previous.additions += block.additions;
        } else result.push({...block});
    }
    return result;
}

// Line ranges are zero-based and end-exclusive. A replaced source line maps to
// its entire replacement, including lines introduced by wrapping.
export function outputSelection(source: string, output: string, selection: LineSelection): LineSelection {
    if (selection.side === "output") return selection;
    const blocks = changes(source, output);
    function map(line: number, end: boolean) {
        let shift = 0;
        for (const block of blocks) {
            const first = block.deletionLineIndex;
            const last = first + block.deletions;
            if (line < first || end && line === first) break;
            if (line < last || end && line === last)
                return block.additionLineIndex + (end ? block.additions : 0);
            shift = block.additionLineIndex + block.additions - last;
        }
        return line + shift;
    }
    return {start: map(selection.start, false), end: map(selection.end, true), side: "output"};
}

export function affectsSelection(output: string, alternative: string, selection: LineSelection) {
    return changes(output, alternative).some(block => {
        const start = block.deletionLineIndex;
        const end = start + block.deletions;
        if (selection.start === selection.end) return start <= selection.start && end >= selection.end;
        return block.deletions === 0
            ? start >= selection.start && start <= selection.end
            : start < selection.end && end > selection.start;
    });
}

export async function findSelectionRules(
    source: string, output: string, preferences: Preferences, selection: LineSelection,
    format: (preferences: Preferences) => Promise<string>, signal: AbortSignal,
) {
    const selected = outputSelection(source, output, selection);
    const enabled = preferences.filter(preference =>
        (preference.local.kind === "absent" ? preference.inherited : preference.local).kind === "explicit");
    const keys = new Set<string>();
    let next = 0;
    await Promise.all(Array.from({length: Math.min(4, enabled.length)}, async () => {
        while (next < enabled.length) {
            signal.throwIfAborted();
            const preference = enabled[next++]!;
            const alternative = await format(preferences.map(item => item.key === preference.key
                ? {...item, local: {kind: "unset", value: null}} : item));
            signal.throwIfAborted();
            if (affectsSelection(output, alternative, selected)) keys.add(preference.key);
        }
    }));
    return keys;
}
