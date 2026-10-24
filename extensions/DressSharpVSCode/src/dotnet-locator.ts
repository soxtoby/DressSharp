import {homedir} from "node:os";
import {delimiter, join, resolve} from "node:path";
import {isFile} from "./files";

/** Where the `dotnet` executable that runs the local tool is, or why it wasn't found. */
export type DotnetLocation =
    | {kind: "found", path: string}
    /** The user named an executable in settings that isn't there. */
    | {kind: "configuredPathMissing", path: string}
    | {kind: "notFound"};

let executableName = process.platform == "win32" ? "dotnet.exe" : "dotnet";

export async function locateDotnet(configuredPath: string | undefined, environment = process.env): Promise<DotnetLocation> {
    let configured = configuredPath?.trim() ?? "";
    return configured ? await locateConfigured(configured) : await locateDefault(environment);
}

async function locateConfigured(configured: string): Promise<DotnetLocation> {
    let path = resolve(configured);
    return await isFile(path) ? {kind: "found", path} : {kind: "configuredPathMissing", path: configured};
}

async function locateDefault(environment: NodeJS.ProcessEnv): Promise<DotnetLocation> {
    let candidates = [...onPath(environment), ...wellKnownLocations(environment)];
    let found = await Promise.all(candidates.map(isFile));
    let path = candidates[found.indexOf(true)];
    return path ? {kind: "found", path} : {kind: "notFound"};
}

function onPath(environment: NodeJS.ProcessEnv): string[] {
    return (environment["PATH"] ?? environment["Path"] ?? "")
        .split(delimiter)
        .map(directory => directory.replace(/^"(.*)"$/, "$1"))
        .filter(directory => directory)
        .map(directory => join(directory, executableName));
}

/** Where SDK installers put dotnet when it isn't on the PATH VS Code was started with. */
function wellKnownLocations(environment: NodeJS.ProcessEnv): string[] {
    let fromRoot = environment["DOTNET_ROOT"] ? [join(environment["DOTNET_ROOT"], executableName)] : [];
    let installed = process.platform == "win32" ? (environment["ProgramFiles"] ? [join(environment["ProgramFiles"], "dotnet", executableName)] : [])
        : process.platform == "darwin" ? ["/usr/local/share/dotnet/dotnet", "/opt/homebrew/bin/dotnet"]
        : ["/usr/bin/dotnet", "/usr/share/dotnet/dotnet", "/usr/lib/dotnet/dotnet"];
    return [...fromRoot, ...installed, join(homedir(), ".dotnet", executableName)];
}
