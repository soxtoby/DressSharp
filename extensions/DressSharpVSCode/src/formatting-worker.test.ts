import {afterEach, beforeEach, expect, test} from "bun:test";
import {mkdtemp, rm, writeFile} from "node:fs/promises";
import {tmpdir} from "node:os";
import {join} from "node:path";
import {FormattingWorker} from "./formatting-worker";

let directory: string;
let assembly: string;
let input: string;
let sut: FormattingWorker | undefined;

beforeEach(async () => {
    directory = await mkdtemp(join(tmpdir(), "dresssharp-worker-"));
    assembly = join(directory, "DressSharp.dll");
    input = join(directory, "dotnet-tools.json");
    await writeFile(assembly, "1");
    await writeFile(input, "1");
});

afterEach(async () => {
    sut?.dispose();
    await rm(directory, {recursive: true, force: true});
});

test("FormattingWorker for successive requests answers them from one process", async () => {
    sut = worker("formatter");

    let first = await sut.format({path: "A.cs", text: "a"});
    let second = await sut.format({path: "B.cs", text: "b"});

    expect(first).toEqual({exitCode: 0, stdout: `${processOf(first.stdout)}:a`, stderr: ""});
    expect(second.stdout).toBe(`${processOf(first.stdout)}:b`);
});

test("FormattingWorker for requests made together answers each with its own reply", async () => {
    sut = worker("formatter");

    let results = await Promise.all(["a", "b", "c"].map(text => sut!.format({path: "A.cs", text})));

    expect(results.map(result => result.stdout.split(":")[1])).toEqual(["a", "b", "c"]);
});

test("FormattingWorker after its process exits starts another for the next request", async () => {
    sut = worker("formatter");
    let first = await sut.format({path: "A.cs", text: "a"});

    let exit = sut.format({path: "A.cs", text: "exit"});
    let next = await sut.format({path: "A.cs", text: "b"});

    await expect(exit).rejects.toThrow("DressSharp exited with code 3.");
    expect(processOf(next.stdout)).not.toBe(processOf(first.stdout));
});

test("FormattingWorker for a tool that can't start reports what it wrote", async () => {
    sut = worker("unrestored");

    let result = sut.format({path: "A.cs", text: "a"});

    await expect(result).rejects.toThrow(`DressSharp exited with code 1. Run "dotnet tool restore" to make the "dotnet-dress" command available.`);
});

test("FormattingWorker after an input file changes starts another process", async () => {
    sut = worker("formatter");
    let first = await sut.format({path: "A.cs", text: "a"});

    await writeFile(input, "22");
    let next = await sut.format({path: "A.cs", text: "b"});

    expect(processOf(next.stdout)).not.toBe(processOf(first.stdout));
});

test("FormattingWorker after its assembly changes starts another process", async () => {
    sut = worker("formatter");
    let first = await sut.format({path: "A.cs", text: "a"});

    await writeFile(assembly, "22");
    let next = await sut.format({path: "A.cs", text: "b"});

    expect(processOf(next.stdout)).not.toBe(processOf(first.stdout));
});

test("FormattingWorker for a request that isn't answered in time fails it and starts another process", async () => {
    sut = worker("formatter", 1000);
    let first = await sut.format({path: "A.cs", text: "a"});

    let hang = sut.format({path: "A.cs", text: "hang"});
    let next = sut.format({path: "A.cs", text: "b"});

    await expect(hang).rejects.toThrow("DressSharp did not answer within 1 seconds.");
    expect(processOf((await next).stdout)).not.toBe(processOf(first.stdout));
});

test("FormattingWorker when stopped answers the requests already made", async () => {
    sut = worker("formatter");

    let request = sut.format({path: "A.cs", text: "a"});
    let stopped = sut.stop();

    expect((await request).stdout).toEndWith(":a");
    await stopped;
    await expect(sut.format({path: "A.cs", text: "b"})).rejects.toThrow("The DressSharp formatter has closed.");
});

function worker(mode: string, timeoutMs?: number): FormattingWorker {
    let command = {executable: process.execPath, args: [join(import.meta.dir, "fake-formatter.ts"), mode, assembly], cwd: import.meta.dir};
    return new FormattingWorker(command, [input], timeoutMs);
}

function processOf(stdout: string): string {
    return stdout.split(":")[0]!;
}
