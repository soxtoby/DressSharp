package com.soxtoby.dresssharp.rider

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import java.nio.file.Files
import java.nio.file.Path

class DressSharpToolManifestTest {
    @TempDir
    lateinit var repository: Path

    @Test
    fun `finds the manifest in the config directory above a nested file`() {
        val manifest = writeManifest(repository.resolve(".config/dotnet-tools.json"), dressSharpManifest())
        val file = repository.resolve("src/Project/Program.cs")

        assertEquals(ToolManifest(manifest, repository), DressSharpToolManifest.find(file))
    }

    @Test
    fun `accepts a manifest directly in a directory`() {
        val manifest = writeManifest(repository.resolve("dotnet-tools.json"), dressSharpManifest())

        assertEquals(ToolManifest(manifest, repository), DressSharpToolManifest.find(repository.resolve("Program.cs")))
    }

    @Test
    fun `matches the package id in any case and by its command name`() {
        writeManifest(repository.resolve("a/.config/dotnet-tools.json"), manifest("""{"DressSharp": {"version": "0.4.0", "commands": ["dotnet-dress"]}}"""))
        writeManifest(repository.resolve("b/.config/dotnet-tools.json"), manifest("""{"my-fork": {"version": "0.4.0", "commands": ["dotnet-dress"]}}"""))

        assertEquals(repository.resolve("a"), DressSharpToolManifest.find(repository.resolve("a/Program.cs"))?.root)
        assertEquals(repository.resolve("b"), DressSharpToolManifest.find(repository.resolve("b/Program.cs"))?.root)
    }

    @Test
    fun `finds nothing when no manifest installs DressSharp`() {
        writeManifest(repository.resolve(".config/dotnet-tools.json"), manifest("""{"csharpier": {"version": "1.0.0", "commands": ["csharpier"]}}"""))

        assertNull(DressSharpToolManifest.find(repository.resolve("Program.cs")))
    }

    @Test
    fun `a root manifest without DressSharp ends the search`() {
        writeManifest(repository.resolve(".config/dotnet-tools.json"), dressSharpManifest())
        writeManifest(repository.resolve("inner/.config/dotnet-tools.json"), manifest("{}", isRoot = true))

        assertNull(DressSharpToolManifest.find(repository.resolve("inner/Program.cs")))
    }

    @Test
    fun `a manifest that is not a root is combined with those above it`() {
        val outer = writeManifest(repository.resolve(".config/dotnet-tools.json"), dressSharpManifest())
        writeManifest(repository.resolve("inner/.config/dotnet-tools.json"), manifest("{}", isRoot = false))

        assertEquals(ToolManifest(outer, repository), DressSharpToolManifest.find(repository.resolve("inner/Program.cs")))
    }

    @Test
    fun `a manifest dotnet could not read is passed over`() {
        val outer = writeManifest(repository.resolve(".config/dotnet-tools.json"), dressSharpManifest())
        writeManifest(repository.resolve("inner/.config/dotnet-tools.json"), "{ not json")
        writeManifest(repository.resolve("inner/deeper/.config/dotnet-tools.json"), "[]")

        assertEquals(ToolManifest(outer, repository), DressSharpToolManifest.find(repository.resolve("inner/deeper/Program.cs")))
    }

    private fun dressSharpManifest() = manifest("""{"dresssharp": {"version": "0.4.0", "commands": ["dotnet-dress"], "rollForward": false}}""")

    private fun manifest(tools: String, isRoot: Boolean = true) = """{"version": 1, "isRoot": $isRoot, "tools": $tools}"""

    private fun writeManifest(path: Path, content: String): Path {
        Files.createDirectories(path.parent)
        return Files.writeString(path, content)
    }
}
