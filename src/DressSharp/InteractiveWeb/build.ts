import { copyFile, mkdir } from "node:fs/promises";

await mkdir("dist", {recursive: true});

const result = await Bun.build({
    entrypoints: ["src/app.ts"],
    outdir: "dist",
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
    copyFile("src/index.html", "dist/index.html"),
    copyFile("src/app.css", "dist/app.css"),
]);
