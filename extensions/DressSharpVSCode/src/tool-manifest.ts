import {readFile} from "node:fs/promises";
import {dirname, join, resolve} from "node:path";

/** A .NET local tool manifest that installs `dotnet dress` for every file beneath its root. */
export interface ToolManifest {
    file: string;
    root: string;
}

let manifestNames = [join(".config", "dotnet-tools.json"), "dotnet-tools.json"];

/**
 * Finds the local tool manifest that installs DressSharp for a file, the way the dotnet CLI finds
 * manifests: each directory from the file's upward is checked for `.config/dotnet-tools.json` and
 * then `dotnet-tools.json`, and a manifest marked `isRoot` ends the search.
 */
export async function findToolManifest(file: string): Promise<ToolManifest | undefined> {
    let candidates = ancestors(file).flatMap(root => manifestNames.map(name => ({file: join(root, name), root})));
    let manifests = await Promise.all(candidates.map(candidate => readManifest(candidate.file)));
    let index = manifests.findIndex(manifest => manifest?.installsDressSharp || manifest?.isRoot);
    return manifests[index]?.installsDressSharp ? candidates[index] : undefined;
}

/** The directories containing a file, nearest first. */
export function ancestors(file: string): string[] {
    let directories = [dirname(resolve(file))];
    let parent = dirname(directories[0]!);
    while (parent != directories.at(-1)) {
        directories.push(parent);
        parent = dirname(parent);
    }
    return directories;
}

interface Manifest {
    isRoot: boolean;
    installsDressSharp: boolean;
}

/** Reads a manifest, or returns undefined when it is not one dotnet itself would accept. */
async function readManifest(file: string): Promise<Manifest | undefined> {
    try {
        let json = JSON.parse((await readFile(file, "utf8")).replace(/^﻿/, "")) as {isRoot?: unknown, tools?: unknown};
        let tools = isObject(json.tools) ? Object.entries(json.tools) : [];
        return {
            isRoot: json.isRoot === true,
            installsDressSharp: tools.some(([id, tool]) => id.toLowerCase() == "dresssharp" || commands(tool).some(command => command.toLowerCase() == "dotnet-dress")),
        };
    } catch {
        return undefined;
    }
}

function commands(tool: unknown): string[] {
    let commands = isObject(tool) ? tool["commands"] : undefined;
    return Array.isArray(commands) ? commands.filter(command => typeof command == "string") : [];
}

function isObject(value: unknown): value is Record<string, unknown> {
    return typeof value == "object" && value != null && !Array.isArray(value);
}
