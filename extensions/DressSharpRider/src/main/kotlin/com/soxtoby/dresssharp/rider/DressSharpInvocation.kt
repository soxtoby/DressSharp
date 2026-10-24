package com.soxtoby.dresssharp.rider

import com.intellij.execution.configurations.GeneralCommandLine
import java.nio.file.Path

/** One `dotnet dress format` run over files beneath a tool manifest's directory. */
class DressSharpInvocation(val root: Path, files: Collection<Path>) {
    /** The files as DressSharp's `--include` globs: paths relative to [root], with forward slashes. */
    val includes: List<String> = files
        .map { root.relativize(it.toAbsolutePath().normalize()).toString().replace('\\', '/') }
        .distinct()
        .sorted()

    fun commandLine(dotnet: Path): GeneralCommandLine {
        val command = GeneralCommandLine(dotnet.toString(), "dress", "format", "--verbose")
        for (include in includes) command.addParameters("--include", include)
        return command.fromRoot(root)
    }

    companion object {
        fun standardInput(root: Path, file: Path, dotnet: Path): GeneralCommandLine =
            GeneralCommandLine(dotnet.toString(), "dress", "format", "--stdin", root.relativize(file).toString().replace('\\', '/')).fromRoot(root)

        fun server(root: Path, dotnet: Path): GeneralCommandLine =
            GeneralCommandLine(dotnet.toString(), "dress", "format", "--server").fromRoot(root)
    }
}

/** Runs from the manifest's directory, so that the dotnet CLI finds the local tool. */
private fun GeneralCommandLine.fromRoot(root: Path): GeneralCommandLine = this
    .withWorkingDirectory(root)
    .withCharset(Charsets.UTF_8)
    .withEnvironment("DOTNET_NOLOGO", "1")

/** What a `dotnet dress format` run reported. */
data class DressSharpOutput(
    /** The includes DressSharp rewrote. */
    val changed: List<String>,
    val formatted: Int?,
    val selected: Int?,
) {
    companion object {
        private val summary = Regex("""^Formatted (\d+) of (\d+) files? in .*$""")

        fun parse(stdout: String, includes: Collection<String>): DressSharpOutput {
            var formatted: Int? = null
            var selected: Int? = null
            val changed = ArrayList<String>()
            for (raw in stdout.lineSequence()) {
                val line = raw.trim()
                val match = summary.matchEntire(line)
                if (match != null) {
                    formatted = match.groupValues[1].toInt()
                    selected = match.groupValues[2].toInt()
                } else if (includes.any { it.equals(line, ignoreCase = true) }) {
                    changed += line
                }
            }
            return DressSharpOutput(changed, formatted, selected)
        }

        /** Verbose notes also use stderr; only labeled warnings should produce notifications. */
        fun parseWarnings(stderr: String): Set<String> = stderr.lineSequence()
            .map { it.trim() }
            .filter { it.startsWith("warning: ") }
            .toSet()
    }
}
