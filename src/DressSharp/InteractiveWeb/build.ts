import { copyFile, mkdir, rm } from "node:fs/promises";

await rm("dist", {force: true, recursive: true});
await mkdir("dist", {recursive: true});

const result = await Bun.build({
    entrypoints: ["src/app.ts"],
    outdir: "dist",
    naming: "app.js",
    target: "browser",
    minify: true,
});

if (!result.success) {
    for (const log of result.logs) console.error(log);
    process.exit(1);
}

await Promise.all([
    copyFile("src/index.html", "dist/index.html"),
    copyFile("src/app.css", "dist/app.css"),
]);
