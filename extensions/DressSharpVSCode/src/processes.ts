import {spawn, type ChildProcess, type ChildProcessWithoutNullStreams} from "node:child_process";

/** A process to run: the executable, its arguments, and the directory it runs in. */
export interface Command {
    executable: string;
    args: string[];
    cwd: string;
}

/** What a DressSharp run reported. */
export interface ProcessOutput {
    exitCode: number;
    stdout: string;
    stderr: string;
}

/** Runs from the tool manifest's directory, so that the dotnet CLI finds the local tool. */
export function start(command: Command): ChildProcessWithoutNullStreams {
    let child = spawn(command.executable, command.args, {
        cwd: command.cwd,
        env: {...process.env, DOTNET_NOLOGO: "1"},
        windowsHide: true,
        // Its own process group lets killTree stop the formatter the dotnet launcher starts as a child.
        detached: process.platform != "win32",
    });
    // A process that exits early closes its input; the exit is what gets reported.
    child.stdin.on("error", () => {});
    return child;
}

/** Runs a command to completion with the given standard input, stopping it after timeoutMs. */
export async function run(command: Command, input: string, timeoutMs: number): Promise<ProcessOutput> {
    let child = start(command);
    let stdout = "";
    let stderr = "";
    child.stdout.setEncoding("utf8").on("data", chunk => stdout += chunk);
    child.stderr.setEncoding("utf8").on("data", chunk => stderr += chunk);
    child.stdin.end(input, "utf8");
    let exitCode = await withTimeout(exited(child), timeoutMs, () => killTree(child));
    return {exitCode, stdout, stderr};
}

function exited(child: ChildProcess): Promise<number> {
    return new Promise((resolve, reject) => {
        child.on("error", reject);
        child.on("close", code => resolve(code ?? 1));
    });
}

/** Settles like the promise, unless timeoutMs passes first, when onTimeout runs and it fails instead. */
export async function withTimeout<T>(promise: Promise<T>, timeoutMs: number, onTimeout: () => void): Promise<T> {
    let timer: NodeJS.Timeout | undefined;
    let timeout = new Promise<never>((_, reject) => {
        timer = setTimeout(() => {
            onTimeout();
            reject(new Error(`DressSharp did not answer within ${timeoutMs / 1000} seconds.`));
        }, timeoutMs);
    });
    try {
        return await Promise.race([promise, timeout]);
    } finally {
        clearTimeout(timer);
    }
}

/** Stops a process along with the formatter the dotnet launcher runs for it. */
export function killTree(child: ChildProcess) {
    if (child.pid == undefined || child.exitCode != null || child.signalCode != null) return;

    if (process.platform == "win32") {
        spawn("taskkill", ["/pid", String(child.pid), "/t", "/f"], {windowsHide: true, stdio: "ignore"}).on("error", () => child.kill());
    } else {
        try {
            process.kill(-child.pid, "SIGKILL");
        } catch {
            child.kill("SIGKILL");
        }
    }
}
