package com.soxtoby.dresssharp.rider

import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.execution.process.CapturingProcessHandler
import com.intellij.execution.process.ProcessOutput
import com.intellij.openapi.Disposable
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.service
import com.intellij.openapi.project.Project
import com.intellij.openapi.progress.coroutineToIndicator
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.IOException
import java.nio.file.Path

/** Selects a shared worker or a fresh local tool process according to the project setting. */
@Service(Service.Level.PROJECT)
class DressSharpWorkers(private val project: Project, private val scope: CoroutineScope) : Disposable {
    private val lock = Any()
    private val workers = HashMap<Path, FormattingWorker>()
    private var disposed = false

    suspend fun format(manifest: ToolManifest, file: Path, dotnet: Path, text: String): ProcessOutput = withContext(Dispatchers.IO) {
        val worker = worker(manifest.root)
        val relative = manifest.root.relativize(file).toString().replace('\\', '/')
        if (worker != null) worker.format(DressSharpInvocation.server(manifest.root, dotnet), { WorkerInputs.capture(manifest, dotnet) }, relative, text)
        else runProcess(DressSharpInvocation.standardInput(manifest.root, file, dotnet), text)
    }

    suspend fun formatFiles(manifest: ToolManifest, invocation: DressSharpInvocation, dotnet: Path): ProcessOutput = withContext(Dispatchers.IO) {
        val worker = worker(manifest.root)
        if (worker != null) worker.formatFiles(DressSharpInvocation.server(manifest.root, dotnet), { WorkerInputs.capture(manifest, dotnet) }, invocation.includes)
        else runProcess(invocation.commandLine(dotnet))
    }

    private fun worker(root: Path): FormattingWorker? = synchronized(lock) {
        if (disposed) throw IOException("The DressSharp formatter has closed.")
        if (DressSharpProjectSettings.getInstance(project).state.useBackgroundProcess) workers.getOrPut(root) { FormattingWorker(scope) }
        else null
    }

    private suspend fun runProcess(command: GeneralCommandLine, input: String? = null): ProcessOutput {
        val handler = CapturingProcessHandler(command)
        return try {
            coroutineScope {
                if (input != null) launch(Dispatchers.IO) { handler.processInput.use { it.write(input.toByteArray(Charsets.UTF_8)) } }
                coroutineToIndicator { indicator -> handler.runProcessWithProgressIndicator(indicator) }
            }
        } catch (e: Exception) {
            handler.destroyProcess()
            throw e
        }
    }

    fun stop() {
        val current = synchronized(lock) { workers.values.toList().also { workers.clear() } }
        current.forEach { worker -> scope.launch { worker.stop() } }
    }

    override fun dispose() = synchronized(lock) {
        disposed = true
        workers.values.forEach { it.close() }
        workers.clear()
    }

    companion object {
        fun getInstance(project: Project): DressSharpWorkers = project.service()
    }
}
