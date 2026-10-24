package com.soxtoby.dresssharp.rider

import com.google.gson.JsonParser
import com.intellij.execution.configurations.GeneralCommandLine
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.async
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNotEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Assumptions.assumeTrue
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import org.junit.jupiter.params.ParameterizedTest
import org.junit.jupiter.params.provider.ValueSource
import java.nio.file.Files
import java.nio.file.Path
import java.io.IOException

class FormattingWorkerTest {
    @TempDir
    lateinit var directory: Path
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val java = Path.of(System.getProperty("java.home"), "bin", if (System.getProperty("os.name").startsWith("Windows")) "java.exe" else "java")
    private val helper = Path.of(requireNotNull(javaClass.getResource("/FormatterWorker.java")).toURI())

    @AfterEach
    fun cleanup() { scope.cancel() }

    private fun command(): GeneralCommandLine = GeneralCommandLine(
        java.toString(), helper.toString(), directory.resolve("binary").toString(), directory.resolve("received").toString(), directory.resolve("release").toString(),
    )

    private fun inputs(): WorkerInputs = WorkerInputs.capture(ToolManifest(directory.resolve("dotnet-tools.json"), directory), directory.resolve("dotnet"))

    @Test
    fun `requests reuse the process and preserve escaped document text`() = runBlocking {
        Files.writeString(directory.resolve("binary"), "first")
        FormattingWorker(scope).use { sut ->
            val first = sut.format(command(), ::inputs, "File.cs", "one")
            val text = "雪\n\"quoted\"\r\nsecond"
            val second = sut.format(command(), ::inputs, "Other.cs", text)

            assertEquals(first.stdout, second.stdout)
            assertEquals(text, second.stderr)
        }
    }

    @Test
    fun `saved batches and unsaved text share one process`() = runBlocking {
        Files.writeString(directory.resolve("binary"), "first")
        FormattingWorker(scope).use { sut ->
            val first = sut.format(command(), ::inputs, "File.cs", "one")
            val saved = sut.formatFiles(command(), ::inputs, listOf("File.cs", "src/雪.cs"))
            val last = sut.format(command(), ::inputs, "File.cs", "two")

            assertEquals(first.stdout, saved.stdout)
            assertEquals(first.stdout, last.stdout)
            val request = JsonParser.parseString(saved.stderr).asJsonObject
            assertEquals(listOf("File.cs", "src/雪.cs"), request.getAsJsonArray("includes").map { it.asString })
            assertFalse(request.has("text"))
            assertEquals("two", last.stderr)
        }
    }

    @Test
    fun `stopping the worker lets an active request finish before terminating the process`() = runBlocking {
        Files.writeString(directory.resolve("binary"), "first")
        FormattingWorker(scope).use { sut ->
            val pending = async { sut.format(command(), ::inputs, "File.cs", "slow") }
            withTimeout(15_000) { while (!Files.exists(directory.resolve("received"))) delay(10) }
            val process = ProcessHandle.of(Files.readString(directory.resolve("received")).toLong()).orElseThrow()

            val stopped = async { sut.stop() }
            assertTrue(process.isAlive)
            Files.writeString(directory.resolve("release"), "go")
            val result = pending.await()
            withTimeout(10_000) {
                stopped.await()
                while (process.isAlive) delay(10)
            }

            assertEquals("slow", result.stderr)
            assertFalse(process.isAlive)
        }
    }

    @Test
    fun `cancellation drains the old reply before another request uses the process`() = runBlocking {
        Files.writeString(directory.resolve("binary"), "first")
        FormattingWorker(scope).use { sut ->
            val first = async { sut.format(command(), ::inputs, "File.cs", "slow") }
            withTimeout(15_000) { while (!Files.exists(directory.resolve("received"))) delay(10) }
            val pid = Files.readString(directory.resolve("received"))

            first.cancel()
            val second = async { sut.format(command(), ::inputs, "File.cs", "new text") }
            Files.writeString(directory.resolve("release"), "go")
            val result = second.await()
            first.join()

            assertTrue(first.isCancelled)
            assertEquals(pid, result.stdout)
            assertEquals("new text", result.stderr)
        }
    }

    @ParameterizedTest
    @ValueSource(strings = ["dotnet-tools.json", "global.json", "binary", "sdk"])
    fun `a changed tool or SDK restarts the process`(changed: String) = runBlocking {
        Files.writeString(directory.resolve("binary"), "first")
        FormattingWorker(scope).use { sut ->
            val first = sut.format(command(), ::inputs, "File.cs", "one")

            val process = ProcessHandle.of(first.stdout.toLong()).orElseThrow()
            if (changed == "sdk") Files.createDirectory(directory.resolve(changed))
            else Files.writeString(directory.resolve(changed), "changed contents")
            withTimeout(10_000) { while (process.isAlive) delay(10) }
            val second = sut.format(command(), ::inputs, "File.cs", "two")

            assertNotEquals(first.stdout, second.stdout)
            assertEquals("two", second.stderr)
        }
    }

    @Test
    fun `SDK installation changes are detected through a dotnet symlink`() {
        val installation = Files.createDirectory(directory.resolve("installation"))
        val executable = Files.writeString(installation.resolve("dotnet"), "fixture")
        val aliases = Files.createDirectory(directory.resolve("aliases"))
        val alias = aliases.resolve("dotnet")
        val linked = runCatching { Files.createSymbolicLink(alias, executable); true }.getOrDefault(false)
        assumeTrue(linked, "Symbolic links are unavailable in this environment")
        val manifest = ToolManifest(directory.resolve("dotnet-tools.json"), directory)
        val first = WorkerInputs.capture(manifest, alias)

        Files.createDirectory(installation.resolve("sdk"))
        val second = WorkerInputs.capture(manifest, alias)

        assertNotEquals(first, second)
    }

    @Test
    fun `an exited formatter is restarted for the next request`() = runBlocking {
        Files.writeString(directory.resolve("binary"), "first")
        FormattingWorker(scope).use { sut ->
            val first = sut.format(command(), ::inputs, "File.cs", "one")
            val process = ProcessHandle.of(first.stdout.toLong()).orElseThrow()
            process.destroyForcibly()
            withTimeout(10_000) { while (process.isAlive) delay(10) }

            val second = sut.format(command(), ::inputs, "File.cs", "two")

            assertNotEquals(first.stdout, second.stdout)
        }
    }

    @Test
    fun `closing a launcher also terminates its formatter child`() = runBlocking {
        Files.writeString(directory.resolve("binary"), "first")
        FormattingWorker(scope).use { sut ->
            val result = sut.format(command().apply { addParameter(helper.toString()) }, ::inputs, "File.cs", "one")
            val child = ProcessHandle.of(result.stdout.toLong()).orElseThrow()
            val launcher = child.parent().orElseThrow()
            assertEquals(Files.readString(directory.resolve("received.launcher")), launcher.pid().toString())

            sut.close()
            withTimeout(10_000) { while (child.isAlive || launcher.isAlive) delay(10) }

            assertFalse(child.isAlive)
            assertFalse(launcher.isAlive)
        }
    }

    @Test
    fun `closing an unresponsive formatter releases the pending request`() = runBlocking {
        Files.writeString(directory.resolve("binary"), "first")
        FormattingWorker(scope).use { sut ->
            val pending = async { runCatching { sut.format(command(), ::inputs, "File.cs", "slow") } }
            withTimeout(15_000) { while (!Files.exists(directory.resolve("received"))) delay(10) }
            val process = ProcessHandle.of(Files.readString(directory.resolve("received")).toLong()).orElseThrow()

            sut.close()
            val result = withTimeout(10_000) { pending.await() }

            assertTrue(result.exceptionOrNull() is IOException)
            withTimeout(10_000) { while (process.isAlive) delay(10) }
        }
    }

    @Test
    fun `closing the worker terminates the process and prevents another launch`() = runBlocking {
        Files.writeString(directory.resolve("binary"), "first")
        val sut = FormattingWorker(scope)
        val first = sut.format(command(), ::inputs, "File.cs", "one")
        val process = ProcessHandle.of(first.stdout.toLong()).orElseThrow()

        sut.close()
        withTimeout(10_000) { while (process.isAlive) delay(10) }
        val failure = runCatching { sut.format(command(), ::inputs, "File.cs", "two") }.exceptionOrNull()

        assertFalse(process.isAlive)
        assertTrue(failure is IOException)
    }
}
