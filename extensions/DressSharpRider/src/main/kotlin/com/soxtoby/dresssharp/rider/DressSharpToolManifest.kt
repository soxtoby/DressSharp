package com.soxtoby.dresssharp.rider

import com.google.gson.JsonElement
import com.google.gson.JsonObject
import com.google.gson.JsonParseException
import com.google.gson.JsonParser
import java.io.IOException
import java.nio.file.Files
import java.nio.file.Path

/** A .NET local tool manifest that installs `dotnet dress` for every file beneath [root]. */
data class ToolManifest(val file: Path, val root: Path)

/**
 * Finds the local tool manifest that installs DressSharp for a file, the way the dotnet CLI finds
 * manifests: each directory from the file's upward is checked for `.config/dotnet-tools.json` and
 * then `dotnet-tools.json`, and a manifest marked `isRoot` ends the search.
 */
object DressSharpToolManifest {
    private const val packageId = "dresssharp"
    private const val command = "dotnet-dress"
    private val manifestNames = listOf(Path.of(".config", "dotnet-tools.json"), Path.of("dotnet-tools.json"))

    fun find(file: Path): ToolManifest? {
        val located = generateSequence(file.toAbsolutePath().normalize().parent) { it.parent }
            .flatMap(::candidates)
            .firstOrNull { it.manifest.installsDressSharp || it.manifest.isRoot }
        return located?.takeIf { it.manifest.installsDressSharp }?.let { ToolManifest(it.file, it.root) }
    }

    private class Manifest(val isRoot: Boolean, val installsDressSharp: Boolean)
    private data class LocatedManifest(val file: Path, val root: Path, val manifest: Manifest)

    private fun candidates(directory: Path): Sequence<LocatedManifest> = manifestNames.asSequence().mapNotNull { name ->
        val candidate = directory.resolve(name)
        if (Files.isRegularFile(candidate)) read(candidate)?.let { LocatedManifest(candidate, directory, it) } else null
    }

    /** Reads a manifest, or returns null when it is not one dotnet itself would accept. */
    private fun read(file: Path): Manifest? = try {
        val json = JsonParser.parseString(Files.readString(file)).asJsonObject
        val tools = json.get("tools")?.asObjectOrNull() ?: JsonObject()
        Manifest(
            isRoot = json.get("isRoot")?.takeIf { it.isJsonPrimitive && it.asJsonPrimitive.isBoolean }?.asBoolean ?: false,
            installsDressSharp = tools.entrySet().any { (id, tool) ->
                id.equals(packageId, ignoreCase = true) || tool.commands().any { it.equals(command, ignoreCase = true) }
            },
        )
    } catch (e: IOException) {
        null
    } catch (e: JsonParseException) {
        null
    } catch (e: IllegalStateException) {
        null
    }

    private fun JsonElement.asObjectOrNull(): JsonObject? = if (isJsonObject) asJsonObject else null

    private fun JsonElement.commands(): List<String> = asObjectOrNull()?.get("commands")
        ?.takeIf { it.isJsonArray }?.asJsonArray
        ?.mapNotNull { if (it.isJsonPrimitive && it.asJsonPrimitive.isString) it.asString else null }
        ?: emptyList()
}
