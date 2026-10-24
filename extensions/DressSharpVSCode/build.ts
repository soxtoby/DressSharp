import {mkdir, rm} from "node:fs/promises";
import {resolve} from "node:path";
import {$} from "bun";

let packageExtension = process.argv.includes("--package");
let out = resolve(import.meta.dir, "out");

// The output holds exactly what the extension ships, so it starts empty.
await rm(out, {recursive: true, force: true});

let result = await Bun.build({
    entrypoints: ["src/extension.ts"],
    outdir: out,
    target: "node",
    format: "cjs",
    external: ["vscode"],
});

if (!result.success) {
    for (let log of result.logs) console.error(log);
    process.exit(1);
}

if (packageExtension) {
    // The extension is released with the formatter, so it carries the formatter's version.
    let project = await Bun.file(resolve(import.meta.dir, "../../DressSharp/DressSharp.csproj")).text();
    let version = /<Version>([^<]+)<\/Version>/.exec(project)?.[1];
    if (!version) throw new Error("DressSharp.csproj has no Version.");

    let dist = resolve(import.meta.dir, "../../dist");
    await mkdir(dist, {recursive: true});
    let vsix = resolve(dist, `DressSharpVSCode-${version}.vsix`);
    await $`vsce package ${version} --no-update-package-json --no-dependencies --skip-license --out ${vsix}`.cwd(import.meta.dir);
}
