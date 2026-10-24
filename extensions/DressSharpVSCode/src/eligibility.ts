import {join} from "node:path";
import {isFile} from "./files";
import {ancestors, findToolManifest, type ToolManifest} from "./tool-manifest";

/** Whether DressSharp is set up to format a file, and why not when it isn't. */
export type Eligibility =
    | {kind: "eligible", manifest: ToolManifest, editorConfig: string}
    | {kind: "noToolManifest"}
    | {kind: "noEditorConfig"};

export async function eligibility(file: string): Promise<Eligibility> {
    let [manifest, editorConfig] = await Promise.all([findToolManifest(file), nearestEditorConfig(file)]);
    return !manifest ? {kind: "noToolManifest"}
        : !editorConfig ? {kind: "noEditorConfig"}
        : {kind: "eligible", manifest, editorConfig};
}

/** The nearest `.editorconfig` in the file's directory or any directory above it. */
async function nearestEditorConfig(file: string): Promise<string | undefined> {
    let candidates = ancestors(file).map(directory => join(directory, ".editorconfig"));
    let found = await Promise.all(candidates.map(isFile));
    return candidates[found.indexOf(true)];
}
