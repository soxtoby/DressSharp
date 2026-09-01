export type AssignmentKind = "absent" | "unset" | "explicit";

export type Assignment = {
    kind: AssignmentKind;
    value: string | null;
};

export type PreferenceSnapshot = {
    key: string;
    local: Assignment;
    inherited: Assignment;
    inheritedSourcePath: string | null;
    effectiveValue: string | null;
    effectiveSourcePath: string | null;
};

export type ConfigurationSnapshot = {
    targetPath: string;
    interactiveRoot: string;
    revision: string;
    preferences: PreferenceSnapshot[];
};

export type PendingEdits = Map<string, Assignment>;

export function setEdit(edits: PendingEdits, preference: PreferenceSnapshot, desired: Assignment) {
    const next = new Map(edits);
    if (sameAssignment(preference.local, desired)) next.delete(preference.key);
    else next.set(preference.key, desired);
    return next;
}

export function desiredAssignment(edits: PendingEdits, preference: PreferenceSnapshot) {
    return edits.get(preference.key) ?? preference.local;
}

export function sameAssignment(left: Assignment, right: Assignment) {
    return left.kind === right.kind && left.value === right.value;
}

export function matchesRule(
    query: string,
    rule: {name: string; key: string; description: string; values: Array<{value: string}>; specialValues: string[]},
) {
    const term = query.trim().toLocaleLowerCase();
    if (!term) return true;
    return [rule.name, rule.key, rule.description, ...rule.values.map(value => value.value), ...rule.specialValues]
        .some(value => value.toLocaleLowerCase().includes(term));
}

export function validValue(
    value: string,
    rule: {valueKind: string; values: Array<{value: string}>; minimum: number | null; specialValues: string[]},
) {
    const normalized = value.trim().toLocaleLowerCase();
    if (rule.specialValues.some(candidate => candidate.toLocaleLowerCase() === normalized)) return true;
    if (rule.valueKind === "integer") {
        const number = Number(normalized);
        return Number.isInteger(number) && number >= (rule.minimum ?? 0);
    }
    if (rule.valueKind === "multiplechoice") {
        const selected = normalized.split(",").map(item => item.trim()).filter(Boolean);
        return selected.length > 0
            && new Set(selected).size === selected.length
            && selected.every(item => rule.values.some(option => option.value.toLocaleLowerCase() === item));
    }
    if (rule.valueKind === "permutation") {
        const selected = normalized.split(",").map(item => item.trim()).filter(Boolean);
        return selected.length === rule.values.length
            && new Set(selected).size === selected.length
            && selected.every(item => rule.values.some(option => option.value.toLocaleLowerCase() === item));
    }
    return rule.values.some(option => option.value.toLocaleLowerCase() === normalized);
}
