import {desiredAssignment, type ConfigurationSnapshot, type PendingEdits} from "./state";

export function previewPreferences(snapshot: ConfigurationSnapshot, edits: PendingEdits) {
    return snapshot.preferences.map(preference => ({
        key: preference.key,
        local: desiredAssignment(edits, preference),
        inherited: preference.inherited,
    }));
}

export function whitespaceMarkers(text: string, tabWidth = 4): string {
    let column = 0;
    return [...text.replace(/\r?\n$/, "")].map(character => {
        if (character === "\t") {
            const width = tabWidth - column % tabWidth;
            column += width;
            return "→" + "\u00a0".repeat(width - 1);
        }
        column++;
        return character === " " ? "·" : "\u00a0";
    }).join("");
}

// Every input change invalidates earlier requests, including during debounce.
export class PreviewRevision {
    private current = 0;
    next() { return ++this.current; }
    isCurrent(revision: number) { return revision === this.current; }
}
