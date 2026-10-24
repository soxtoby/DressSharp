import {stat} from "node:fs/promises";

export async function isFile(path: string): Promise<boolean> {
    try {
        return (await stat(path)).isFile();
    } catch {
        return false;
    }
}

/** A file's modification time and size, which change when the file is replaced; empty when it is missing. */
export async function fileStamp(path: string): Promise<string> {
    try {
        let info = await stat(path);
        return `${info.mtimeMs}:${info.size}`;
    } catch {
        return "";
    }
}
