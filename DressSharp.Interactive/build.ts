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
        // The preview highlights with @pierre/highlights in one theme. Pierre's Shiki backend and
        // its catalogs of every theme stay reachable, so stub them out of the bundle.
        build.onResolve({filter: /^(shiki(\/.*)?|@shikijs\/.*|@pierre\/theming\/themes)$/}, () => ({path: "unused", namespace: "preview"}));
        build.onResolve({filter: /^@pierre\/highlights\/themes\/loader$/}, () => ({path: `${import.meta.dir}/src/highlights-themes.ts`}));
        build.onLoad({filter: /.*/, namespace: "preview"}, () => ({loader: "js", contents: `
            const unused = () => { throw new Error("The preview highlights with @pierre/highlights."); };
            export {unused as createHighlighterCore, unused as createCssVariablesTheme, unused as EncodedTokenMetadata, unused as INITIAL};`}));
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
