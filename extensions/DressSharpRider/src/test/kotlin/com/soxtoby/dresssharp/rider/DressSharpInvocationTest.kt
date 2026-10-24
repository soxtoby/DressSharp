package com.soxtoby.dresssharp.rider

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test
import java.nio.file.Path

class DressSharpInvocationTest {
    private val root = Path.of("repo").toAbsolutePath()

    @Test
    fun `files become includes relative to the manifest root with forward slashes`() {
        val sut = DressSharpInvocation(root, listOf(root.resolve("src/B.cs"), root.resolve("src/A.cs"), root.resolve("src/../src/B.cs")))

        assertEquals(listOf("src/A.cs", "src/B.cs"), sut.includes)
    }

    @Test
    fun `the command formats the includes verbosely from the manifest root`() {
        val result = DressSharpInvocation(root, listOf(root.resolve("src/A.cs"))).commandLine(Path.of("dotnet"))

        assertEquals(listOf("dotnet", "dress", "format", "--verbose", "--include", "src/A.cs"), result.getCommandLineList(null))
        assertEquals(root, result.workingDirectory)
        assertEquals("1", result.environment["DOTNET_NOLOGO"])
    }

    @Test
    fun `a worker runs the repository local tool from the manifest root`() {
        val result = DressSharpInvocation.server(root, Path.of("dotnet"))

        assertEquals(listOf("dotnet", "dress", "format", "--server"), result.getCommandLineList(null))
        assertEquals(root, result.workingDirectory)
        assertEquals("1", result.environment["DOTNET_NOLOGO"])
    }

    @Test
    fun `a fresh typing process formats standard input as the repository file`() {
        val result = DressSharpInvocation.standardInput(root, root.resolve("src/A.cs"), Path.of("dotnet"))

        assertEquals(listOf("dotnet", "dress", "format", "--stdin", "src/A.cs"), result.getCommandLineList(null))
        assertEquals(root, result.workingDirectory)
        assertEquals("1", result.environment["DOTNET_NOLOGO"])
    }

    @Test
    fun `verbose output names the files that changed and counts the selection`() {
        val stdout = "src/A.cs\nFormatted 1 of 2 files in 0.42 s.\n"

        val result = DressSharpOutput.parse(stdout, listOf("src/A.cs", "src/B.cs"))

        assertEquals(DressSharpOutput(listOf("src/A.cs"), formatted = 1, selected = 2), result)
    }

    @Test
    fun `an empty selection is reported with nothing changed`() {
        val stdout = "No eligible C# files selected.\nFormatted 0 of 0 files in 0.00 s.\n"

        val result = DressSharpOutput.parse(stdout, listOf("src/A.cs"))

        assertEquals(DressSharpOutput(emptyList(), formatted = 0, selected = 0), result)
    }

    @Test
    fun `verbose parse context notes do not become warning notifications`() {
        val stderr = "src/Shared.cs: parsed 2 ways by the projects that compile it.\n"

        val result = DressSharpOutput.parseWarnings(stderr)

        assertEquals(emptySet<String>(), result)
    }

    @Test
    fun `actionable warnings remain when verbose notes accompany them`() {
        val warning = "warning: src/Shared.cs: the projects that compile it disagree about its layout, so it is left as it is."
        val stderr = "src/Shared.cs: parsed 2 ways by the projects that compile it.\r\n$warning\r\n"

        val result = DressSharpOutput.parseWarnings(stderr)

        assertEquals(setOf(warning), result)
    }

    @Test
    fun `a run that ended early has no counts`() {
        val result = DressSharpOutput.parse("", listOf("src/A.cs"))

        assertNull(result.formatted)
        assertNull(result.selected)
    }
}
