import { copyFile, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { gzipSync } from "node:zlib";

const compress = process.argv.includes("--compress");
const out = resolve(import.meta.dir, "../dist/interactive");

// The output holds exactly what DressSharp embeds, so it starts empty.
await rm(out, {recursive: true, force: true});
await mkdir(out, {recursive: true});

const result = await Bun.build({
    entrypoints: ["src/app.ts"],
    outdir: out,
    naming: "app.js",
    target: "browser",
    minify: true,
    plugins: [{name: "preview-highlighter", setup(build) {
        build.onResolve({filter: /^shiki$/}, () => ({path: `${import.meta.dir}/src/shiki-preview.ts`}));
        build.onResolve({filter: /^shiki\/wasm$/}, () => ({path: "unused-wasm", namespace: "preview"}));
        build.onLoad({filter: /.*/, namespace: "preview"}, () => ({contents: "export default undefined", loader: "js"}));
    }}],
});

if (!result.success) {
    for (const log of result.logs) console.error(log);
    process.exit(1);
}

await Promise.all([
    copyFile("src/index.html", `${out}/index.html`),
    copyFile("src/app.css", `${out}/app.css`),
    ...["dresssharp.svg", "favicon.svg", "favicon.ico"].map(name =>
        copyFile(resolve(import.meta.dir, "../assets", name), `${out}/${name}`)),
]);

if (compress) {
    await writeFile(`${out}/app.js.gz`, gzipSync(await readFile(`${out}/app.js`), {level: 9}));
    await rm(`${out}/app.js`);
}
