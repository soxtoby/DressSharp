package com.soxtoby.dresssharp.rider

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test
import java.nio.file.Path

class DressSharpInvocationTest {
    private val root = Path.of("repo").toAbsolutePath()

    @Test
    fun `files become includes relative to the manifest root with forward slashes`() {
        val invocation = DressSharpInvocation(root, listOf(root.resolve("src/B.cs"), root.resolve("src/A.cs"), root.resolve("src/../src/B.cs")))

        assertEquals(listOf("src/A.cs", "src/B.cs"), invocation.includes)
    }

    @Test
    fun `the command formats the includes verbosely from the manifest root`() {
        val command = DressSharpInvocation(root, listOf(root.resolve("src/A.cs"))).commandLine(Path.of("dotnet"))

        assertEquals(listOf("dotnet", "dress", "format", "--verbose", "--include", "src/A.cs"), command.getCommandLineList(null))
        assertEquals(root, command.workingDirectory)
        assertEquals("1", command.environment["DOTNET_NOLOGO"])
    }

    @Test
    fun `a standard input run names the file it stands in for relative to the manifest root`() {
        val command = DressSharpInvocation.standardInput(root, root.resolve("src/../src/A.cs"), Path.of("dotnet"))

        assertEquals(listOf("dotnet", "dress", "format", "--stdin", "src/A.cs"), command.getCommandLineList(null))
        assertEquals(root, command.workingDirectory)
        assertEquals("1", command.environment["DOTNET_NOLOGO"])
    }

    @Test
    fun `verbose output names the files that changed and counts the selection`() {
        val stdout = "src/A.cs\nFormatted 1 of 2 files in 0.42 s.\n"

        val output = DressSharpOutput.parse(stdout, listOf("src/A.cs", "src/B.cs"))

        assertEquals(DressSharpOutput(listOf("src/A.cs"), formatted = 1, selected = 2), output)
    }

    @Test
    fun `an empty selection is reported with nothing changed`() {
        val stdout = "No eligible C# files selected.\nFormatted 0 of 0 files in 0.00 s.\n"

        val output = DressSharpOutput.parse(stdout, listOf("src/A.cs"))

        assertEquals(DressSharpOutput(emptyList(), formatted = 0, selected = 0), output)
    }

    @Test
    fun `a run that ended early has no counts`() {
        val output = DressSharpOutput.parse("", listOf("src/A.cs"))

        assertNull(output.formatted)
        assertNull(output.selected)
    }
}
