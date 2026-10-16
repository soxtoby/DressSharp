package com.soxtoby.dresssharp.rider

import com.intellij.execution.configurations.PathEnvironmentVariableUtil
import com.intellij.openapi.util.SystemInfo
import com.intellij.util.EnvironmentUtil
import java.nio.file.Files
import java.nio.file.InvalidPathException
import java.nio.file.Path

/** Finds the `dotnet` executable that runs the local tool. */
object DotnetLocator {
    sealed interface Result {
        data class Found(val path: Path) : Result

        /** The user named an executable in settings that isn't there. */
        data class ConfiguredPathMissing(val path: String) : Result

        data object NotFound : Result
    }

    private val executableName = if (SystemInfo.isWindows) "dotnet.exe" else "dotnet"

    fun locate(configuredPath: String?): Result {
        val configured = configuredPath?.trim().orEmpty()
        if (configured.isNotEmpty()) {
            val path = try {
                Path.of(configured)
            } catch (e: InvalidPathException) {
                return Result.ConfiguredPathMissing(configured)
            }
            return if (Files.isRegularFile(path)) Result.Found(path) else Result.ConfiguredPathMissing(configured)
        }
        PathEnvironmentVariableUtil.findInPath(executableName)?.let { return Result.Found(it.toPath()) }
        return wellKnownLocations().firstOrNull { Files.isRegularFile(it) }?.let { Result.Found(it) } ?: Result.NotFound
    }

    /** Where SDK installers put dotnet when it isn't on the PATH the IDE was started with. */
    private fun wellKnownLocations(): List<Path> {
        val fromRoot = EnvironmentUtil.getValue("DOTNET_ROOT")?.let { Path.of(it, executableName) }
        val installed = when {
            SystemInfo.isWindows -> listOfNotNull(System.getenv("ProgramFiles")?.let { Path.of(it, "dotnet", executableName) })
            SystemInfo.isMac -> listOf(Path.of("/usr/local/share/dotnet/dotnet"), Path.of("/opt/homebrew/bin/dotnet"))
            else -> listOf(Path.of("/usr/bin/dotnet"), Path.of("/usr/share/dotnet/dotnet"), Path.of("/usr/lib/dotnet/dotnet"))
        }
        val user = System.getProperty("user.home")?.let { Path.of(it, ".dotnet", executableName) }
        return listOfNotNull(fromRoot) + installed + listOfNotNull(user)
    }
}
