// Stands in for `dotnet dress format --server` in the worker tests: `fake-formatter.ts <mode> <assembly path>`.
// It echoes each text with its process ID, exits on the text "exit", and ignores the text "hang".
import {createInterface} from "node:readline";

let [mode, assemblyPath] = process.argv.slice(2);

if (mode == "unrestored") {
    process.stderr.write(`Run "dotnet tool restore" to make the "dotnet-dress" command available.\n`);
    process.exit(1);
} else {
    process.stdout.write(JSON.stringify({protocol: 2, processId: process.pid, assemblyPath}) + "\n");
    for await (let line of createInterface({input: process.stdin})) {
        let request = JSON.parse(line) as {path: string, text: string};
        if (request.text == "exit")
            process.exit(3);
        else if (request.text != "hang")
            process.stdout.write(JSON.stringify({exitCode: 0, stdout: `${process.pid}:${request.text}`, stderr: ""}) + "\n");
    }
}
