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
    private const val PACKAGE_ID = "dresssharp"
    private const val COMMAND = "dotnet-dress"
    private val manifestNames = listOf(Path.of(".config", "dotnet-tools.json"), Path.of("dotnet-tools.json"))

    fun find(file: Path): ToolManifest? {
        var directory: Path? = file.toAbsolutePath().normalize().parent
        while (directory != null) {
            for (name in manifestNames) {
                val candidate = directory.resolve(name)
                if (!Files.isRegularFile(candidate)) continue
                val manifest = read(candidate) ?: continue
                if (manifest.installsDressSharp) return ToolManifest(candidate, directory)
                if (manifest.isRoot) return null
            }
            directory = directory.parent
        }
        return null
    }

    private class Manifest(val isRoot: Boolean, val installsDressSharp: Boolean)

    /** Reads a manifest, or returns null when it is not one dotnet itself would accept. */
    private fun read(file: Path): Manifest? = try {
        val json = JsonParser.parseString(Files.readString(file)).asJsonObject
        val tools = json.get("tools")?.asObjectOrNull() ?: JsonObject()
        Manifest(
            isRoot = json.get("isRoot")?.takeIf { it.isJsonPrimitive && it.asJsonPrimitive.isBoolean }?.asBoolean ?: false,
            installsDressSharp = tools.entrySet().any { (id, tool) ->
                id.equals(PACKAGE_ID, ignoreCase = true) || tool.commands().any { it.equals(COMMAND, ignoreCase = true) }
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
