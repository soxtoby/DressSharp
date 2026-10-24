import type {ChildProcessWithoutNullStreams} from "node:child_process";
import {realpathSync} from "node:fs";
import {dirname, join} from "node:path";
import {createInterface} from "node:readline";
import {fileStamp} from "./files";
import {killTree, start, withTimeout, type Command, type ProcessOutput} from "./processes";
import {ancestors, type ToolManifest} from "./tool-manifest";

/** The version of the `format --server` protocol this extension speaks. */
let protocol = 2;

/** A document to format: its path relative to the tool manifest's directory, and the text the editor holds for it. */
export interface FormatRequest {
    path: string;
    text: string;
}

/**
 * Formats documents over one long-running `dotnet dress format --server` process. Requests are answered
 * in order, a reply is read even when its caller has stopped waiting for it, and the process restarts
 * when it exits, stops answering, or a file it was started from changes.
 */
export class FormattingWorker {
    #command: Command;
    #inputs: string[];
    #timeoutMs: number;
    #queue: Promise<unknown> = Promise.resolve();
    #connection: Connection | undefined;
    #closed = false;

    constructor(command: Command, inputs: string[], timeoutMs = 60_000) {
        this.#command = command;
        this.#inputs = inputs;
        this.#timeoutMs = timeoutMs;
    }

    format(request: FormatRequest): Promise<ProcessOutput> {
        let result = this.#queue.then(() => this.#send(request));
        this.#queue = result.catch(() => {});
        return result;
    }

    /** Finishes the requests already made before stopping the process. */
    stop(): Promise<void> {
        let stopped = this.#queue.then(() => this.dispose());
        this.#queue = stopped;
        return stopped;
    }

    dispose() {
        this.#closed = true;
        this.#connection?.close();
        this.#connection = undefined;
    }

    async #send(request: FormatRequest): Promise<ProcessOutput> {
        let connection = await this.#current();
        try {
            return await withTimeout(connection.format(request), this.#timeoutMs, () => connection.close());
        } catch (error) {
            connection.close();
            throw error;
        }
    }

    async #current(): Promise<Connection> {
        let stamps = await stampAll(this.#inputs);
        let reusable = await this.#connection?.isCurrent(stamps);
        if (this.#closed) throw new Error("The DressSharp formatter has closed.");
        return this.#connection && reusable ? this.#connection : this.#restart(stamps);
    }

    #restart(stamps: string[]): Connection {
        this.#connection?.close();
        this.#connection = new Connection(start(this.#command), this.#inputs, stamps);
        return this.#connection;
    }
}

/**
 * The files whose change means a worker is no longer running the formatter a fresh run would: the
 * tool manifest, the dotnet executable and its installed SDKs, and any `global.json` that picks one.
 */
export function workerInputs(manifest: ToolManifest, dotnet: string): string[] {
    let executable = realPath(dotnet);
    let globalJsons = ancestors(join(manifest.root, "global.json")).map(directory => join(directory, "global.json"));
    return [manifest.file, dotnet, join(dirname(executable), "sdk"), ...globalJsons];
}

function realPath(path: string): string {
    try {
        return realpathSync.native(path);
    } catch {
        return path;
    }
}

function stampAll(paths: string[]): Promise<string[]> {
    return Promise.all(paths.map(fileStamp));
}

/** One formatter process, from its greeting until it exits or is closed. */
class Connection {
    #child: ChildProcessWithoutNullStreams;
    #inputs: string[];
    #stamps: string[];
    #replies = new Replies();
    #diagnostics = "";
    #greeting: Promise<void> | undefined;
    #assembly: {path: string, stamp: string} | undefined;
    #closed = false;
    #watcher: NodeJS.Timeout | undefined;

    constructor(child: ChildProcessWithoutNullStreams, inputs: string[], stamps: string[]) {
        this.#child = child;
        this.#inputs = inputs;
        this.#stamps = stamps;

        createInterface({input: child.stdout.setEncoding("utf8"), crlfDelay: Infinity}).on("line", line => this.#replies.push(line));
        child.stderr.setEncoding("utf8").on("data", chunk => {
            if (this.#diagnostics.length < 16_384) this.#diagnostics += chunk;
        });
        child.on("error", error => this.#replies.fail(error));
        child.on("close", (code, signal) => this.#replies.fail(new Error(`DressSharp exited with ${code != null ? `code ${code}` : signal}. ${this.#diagnostics.trim()}`.trim())));
        this.#watch();
    }

    async format(request: FormatRequest): Promise<ProcessOutput> {
        this.#greeting ??= this.#greet();
        await this.#greeting;
        this.#child.stdin.write(JSON.stringify(request) + "\n", "utf8");
        let reply = parse(await this.#replies.next()) as Partial<ProcessOutput>;
        return {exitCode: reply.exitCode ?? 2, stdout: reply.stdout ?? "", stderr: reply.stderr ?? ""};
    }

    async #greet() {
        let greeting = parse(await this.#replies.next()) as {protocol?: unknown, assemblyPath?: unknown};
        if (greeting.protocol != protocol || typeof greeting.assemblyPath != "string")
            throw new Error(`DressSharp speaks formatter protocol ${greeting.protocol}, but this extension needs protocol ${protocol}.`);
        this.#assembly = {path: greeting.assemblyPath, stamp: await fileStamp(greeting.assemblyPath)};
    }

    /** Whether the process is still running from the same files: its inputs, which have these stamps now, and its own assembly. */
    async isCurrent(stamps: string[]): Promise<boolean> {
        let assemblyStamp = this.#assembly && await fileStamp(this.#assembly.path);
        return !this.#closed
            && this.#child.exitCode == null
            && stamps.every((stamp, index) => stamp == this.#stamps[index])
            && assemblyStamp == this.#assembly?.stamp;
    }

    /** Stops a process whose files change while it waits, so that it doesn't hold an outdated tool open. */
    #watch() {
        this.#watcher = setTimeout(async () => {
            if (await this.isCurrent(await stampAll(this.#inputs))) this.#watch();
            else this.close();
        }, 250);
        this.#watcher.unref();
    }

    close() {
        if (this.#closed) return;

        this.#closed = true;
        clearTimeout(this.#watcher);
        killTree(this.#child);
        this.#child.stdin.destroy();
        this.#replies.fail(new Error("The DressSharp formatter has closed."));
    }
}

function parse(line: string): unknown {
    try {
        return JSON.parse(line);
    } catch {
        throw new Error(`DressSharp wrote something other than a formatter reply: ${line}`);
    }
}

/** The lines the formatter writes, read in order; once it has exited, every further read fails with the reason. */
class Replies {
    #lines: string[] = [];
    #waiting: {resolve: (line: string) => void, reject: (error: Error) => void} | undefined;
    #error: Error | undefined;

    push(line: string) {
        let waiting = this.#waiting;
        this.#waiting = undefined;
        if (waiting) waiting.resolve(line);
        else this.#lines.push(line);
    }

    fail(error: Error) {
        this.#error ??= error;
        this.#waiting?.reject(this.#error);
        this.#waiting = undefined;
    }

    next(): Promise<string> {
        let line = this.#lines.shift();
        return line != undefined ? Promise.resolve(line)
            : this.#error ? Promise.reject(this.#error)
            : new Promise((resolve, reject) => this.#waiting = {resolve, reject});
    }
}
