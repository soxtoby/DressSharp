package com.soxtoby.dresssharp.rider

import com.google.gson.JsonObject
import com.google.gson.JsonArray
import com.google.gson.JsonParser
import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.execution.process.ProcessOutput
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.Job
import kotlinx.coroutines.TimeoutCancellationException
import kotlinx.coroutines.async
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import java.io.IOException
import java.nio.file.Files
import java.nio.file.Path
import java.nio.file.attribute.BasicFileAttributes
import java.nio.file.attribute.FileTime
import java.util.concurrent.atomic.AtomicBoolean

/** Serializes requests and drains replies even when their caller has been superseded. */
internal class FormattingWorker(private val scope: CoroutineScope) : AutoCloseable {
    private val requests = Mutex()
    private val lifecycle = Any()
    private var connection: Connection? = null
    private var closed = false

    suspend fun format(command: GeneralCommandLine, inputs: () -> WorkerInputs, path: String, text: String): ProcessOutput =
        request(command, inputs, JsonObject().apply {
            addProperty("path", path)
            addProperty("text", text)
        })

    suspend fun formatFiles(command: GeneralCommandLine, inputs: () -> WorkerInputs, includes: Collection<String>): ProcessOutput =
        request(command, inputs, JsonObject().apply {
            add("includes", JsonArray().apply { includes.forEach { add(it) } })
        })

    private suspend fun request(command: GeneralCommandLine, inputs: () -> WorkerInputs, request: JsonObject): ProcessOutput = requests.withLock {
        currentCoroutineContext().ensureActive()
        val result = withContext(NonCancellable + Dispatchers.IO) {
            val currentInputs = inputs()
            val current = synchronized(lifecycle) {
                if (closed) throw IOException("The DressSharp formatter has closed.")
                connection?.takeIf { it.reusable(currentInputs) } ?: run {
                    connection?.close()
                    Connection(command.createProcess(), currentInputs, scope).also { connection = it }
                }
            }
            try {
                withTimeout(60_000) { current.format(request) }
            } catch (e: Exception) {
                synchronized(lifecycle) {
                    if (connection === current) {
                        current.close()
                        connection = null
                    }
                }
                if (e is TimeoutCancellationException) throw IOException("DressSharp did not answer within 60 seconds; it will restart on the next request.", e)
                throw e
            }
        }
        currentCoroutineContext().ensureActive()
        result
    }

    /** Finish requests already using this worker before releasing its process. */
    suspend fun stop() = requests.withLock { close() }

    override fun close() = synchronized(lifecycle) {
        closed = true
        connection?.close()
        connection = null
    }

    private class Connection(val process: Process, val inputs: WorkerInputs, private val scope: CoroutineScope) : AutoCloseable {
        private val diagnostics = StringBuilder()
        private val responses = Channel<String>(1)
        private val writer = process.outputStream.bufferedWriter(Charsets.UTF_8)
        @Volatile private var assembly: FileStamp? = null
        private var ready = false
        private val stopped = AtomicBoolean()
        private var watcher: Job? = null

        init {
            val errors = scope.launch(Dispatchers.IO) {
                try {
                    process.errorStream.bufferedReader(Charsets.UTF_8).useLines { lines ->
                        lines.forEach { line -> synchronized(diagnostics) { if (diagnostics.length < 16_384) diagnostics.appendLine(line) } }
                    }
                } catch (_: IOException) {
                    // Terminating the process also closes its pipes.
                }
            }
            scope.launch(Dispatchers.IO) {
                try {
                    process.inputStream.bufferedReader(Charsets.UTF_8).use { reader ->
                        var line = reader.readLine()
                        while (line != null) {
                            responses.send(line)
                            line = reader.readLine()
                        }
                    }
                    val exitCode = process.waitFor()
                    errors.join()
                    responses.close(IOException("DressSharp exited with code $exitCode. ${synchronized(diagnostics) { diagnostics.toString().trim() }}"))
                } catch (e: Exception) {
                    responses.close(e)
                    if (e is CancellationException) throw e
                }
            }
            watcher = scope.launch(Dispatchers.IO) {
                try {
                    while (isActive && reusable(inputs.refresh())) delay(250)
                } finally {
                    // Natural exit is reported after the readers drain stderr.
                    if (!isActive || process.toHandle().isAlive) close()
                }
            }
        }

        fun reusable(current: WorkerInputs): Boolean =
            !stopped.get() && process.toHandle().isAlive && inputs == current && assembly?.let { it == FileStamp.capture(it.path) } != false

        suspend fun format(request: JsonObject): ProcessOutput {
            if (!ready) initialize()
            // A separate writer lets the deadline terminate a process whose stdin stopped draining.
            scope.async(Dispatchers.IO) {
                writer.write(request.toString())
                writer.newLine()
                writer.flush()
            }.await()
            val response = JsonParser.parseString(responses.receive()).asJsonObject
            return ProcessOutput().apply {
                exitCode = response.get("exitCode").asInt
                appendStdout(response.get("stdout").asString)
                appendStderr(response.get("stderr").asString)
            }
        }

        private suspend fun initialize() {
            try {
                val greeting = JsonParser.parseString(responses.receive()).asJsonObject
                if (greeting.get("protocol")?.asInt != 2) throw IOException("Unsupported formatter version.")
                assembly = FileStamp.capture(Path.of(greeting.get("assemblyPath").asString))
                ready = true
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                val message = e.message.orEmpty()
                val errors = synchronized(diagnostics) { diagnostics.toString().trim() }
                val details = listOf(message, errors.takeUnless { message.contains(it) }.orEmpty())
                    .filter { it.isNotBlank() }.joinToString("\n")
                throw IOException(
                    "The installed DressSharp does not support background formatting. Update the repository's local tool " +
                        "or disable the background process in Settings | Tools | DressSharp.\n$details",
                    e,
                )
            }
        }

        override fun close() {
            if (stopped.getAndSet(true)) return
            watcher?.cancel()
            // The dotnet launcher may own the formatter child; terminate the whole owned tree.
            process.descendants().use { children -> children.forEach { it.destroyForcibly() } }
            process.destroyForcibly()
            responses.close(IOException("The DressSharp formatter has closed."))
        }
    }
}

internal data class WorkerInputs(val files: List<FileStamp>) {
    fun refresh(): WorkerInputs = WorkerInputs(files.map { FileStamp.capture(it.path) })

    companion object {
        fun capture(manifest: ToolManifest, dotnet: Path): WorkerInputs {
            val executable = try { dotnet.toRealPath() } catch (_: IOException) { dotnet.toAbsolutePath().normalize() }
            val configuration = generateSequence(manifest.root) { it.parent }.map { it.resolve("global.json") }
            return WorkerInputs((listOf(manifest.file, dotnet, executable.parent.resolve("sdk")) + configuration).map(FileStamp::capture))
        }
    }
}

internal data class FileStamp(val path: Path, val modified: FileTime?, val size: Long) {
    companion object {
        fun capture(path: Path): FileStamp = try {
            val attributes = Files.readAttributes(path, BasicFileAttributes::class.java)
            FileStamp(path, attributes.lastModifiedTime(), attributes.size())
        } catch (e: IOException) {
            FileStamp(path, null, -1)
        }
    }
}
