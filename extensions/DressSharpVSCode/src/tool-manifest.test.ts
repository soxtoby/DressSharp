import {afterEach, beforeEach, expect, test} from "bun:test";
import {mkdir, mkdtemp, rm, writeFile} from "node:fs/promises";
import {tmpdir} from "node:os";
import {dirname, join} from "node:path";
import {findToolManifest} from "./tool-manifest";

let root: string;
let file: string;

beforeEach(async () => {
    root = await mkdtemp(join(tmpdir(), "dresssharp-manifest-"));
    file = join(root, "repo", "src", "Program.cs");
    await mkdir(dirname(file), {recursive: true});
});

afterEach(() => rm(root, {recursive: true, force: true}));

test("findToolManifest for a manifest in an ancestor's .config folder returns it with that ancestor as the root", async () => {
    let manifest = await writeManifest("repo/.config/dotnet-tools.json", {tools: {dresssharp: {commands: ["dotnet-dress"]}}});

    let result = await findToolManifest(file);

    expect(result).toEqual({file: manifest, root: join(root, "repo")});
});

test("findToolManifest for a dotnet-tools.json in an ancestor folder returns it", async () => {
    let manifest = await writeManifest("repo/dotnet-tools.json", {tools: {DressSharp: {}}});

    let result = await findToolManifest(file);

    expect(result).toEqual({file: manifest, root: join(root, "repo")});
});

test("findToolManifest for a package that provides the dotnet-dress command under another name returns its manifest", async () => {
    let manifest = await writeManifest("repo/.config/dotnet-tools.json", {tools: {"dresssharp.fork": {commands: ["dotnet-dress"]}}});

    let result = await findToolManifest(file);

    expect(result?.file).toBe(manifest);
});

test("findToolManifest for a root manifest without DressSharp ignores manifests above it", async () => {
    await writeManifest(".config/dotnet-tools.json", {tools: {dresssharp: {}}});
    await writeManifest("repo/.config/dotnet-tools.json", {isRoot: true, tools: {other: {}}});

    let result = await findToolManifest(file);

    expect(result).toBeUndefined();
});

test("findToolManifest for an unreadable manifest keeps looking above it", async () => {
    let manifest = await writeManifest(".config/dotnet-tools.json", {tools: {dresssharp: {}}});
    await writeFile(join(root, "repo", "dotnet-tools.json"), "{ not json");

    let result = await findToolManifest(file);

    expect(result?.file).toBe(manifest);
});

async function writeManifest(path: string, json: object): Promise<string> {
    let manifest = join(root, path);
    await mkdir(dirname(manifest), {recursive: true});
    await writeFile(manifest, JSON.stringify(json));
    return manifest;
}
