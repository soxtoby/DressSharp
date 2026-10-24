package com.soxtoby.dresssharp.rider

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import java.nio.file.Files
import java.nio.file.Path

class EditorConfigScopeTest {
    @TempDir
    lateinit var repository: Path

    @Test
    fun `the nearest editorconfig above the file is in scope`() {
        Files.writeString(repository.resolve(".editorconfig"), "root = true")
        val nested = Files.createDirectories(repository.resolve("src/Project"))
        val nearest = Files.writeString(nested.resolve(".editorconfig"), "[*.cs]")

        assertEquals(nearest, EditorConfigScope.nearest(nested.resolve("Program.cs")))
        assertEquals(repository.resolve(".editorconfig"), EditorConfigScope.nearest(repository.resolve("src/Other.cs")))
    }

    @Test
    fun `a directory named editorconfig does not count`() {
        Files.createDirectories(repository.resolve(".editorconfig"))

        assertNull(EditorConfigScope.nearest(repository.resolve("Program.cs")))
    }
}
