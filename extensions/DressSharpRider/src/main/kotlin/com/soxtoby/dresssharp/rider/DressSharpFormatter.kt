package com.soxtoby.dresssharp.rider

import com.intellij.execution.ExecutionException
import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.execution.process.CapturingProcessHandler
import com.intellij.execution.process.ProcessOutput
import com.intellij.notification.NotificationAction
import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.application.EDT
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.service
import com.intellij.openapi.diagnostic.logger
import com.intellij.openapi.options.ShowSettingsUtil
import com.intellij.openapi.progress.coroutineToIndicator
import com.intellij.openapi.project.Project
import com.intellij.openapi.util.text.StringUtil
import com.intellij.openapi.vfs.VfsUtil
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.openapi.vfs.toNioPathOrNull
import com.intellij.openapi.wm.StatusBar
import com.intellij.platform.ide.progress.withBackgroundProgress
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.IOException
import java.nio.file.Path

enum class FormatTrigger {
    /** The IDE saved the file; files DressSharp isn't set up for are skipped quietly. */
    SAVE,

    /** The user asked; every reason a file wasn't formatted is reported. */
    MANUAL,
}

/**
 * Runs DressSharp over files as a local tool. Requests that arrive
 * while a run is in progress, or within a moment of each other, are formatted together in the next run.
 */
@Service(Service.Level.PROJECT)
class DressSharpFormatter(private val project: Project, private val scope: CoroutineScope) {
    private class Batch(val manifest: ToolManifest) {
        val files = LinkedHashMap<Path, VirtualFile>()
        var manual = false
    }

    private val lock = Any()
    private val pending = HashMap<Path, Batch>()
    private val running = HashSet<Path>()
    private var reportedWarnings = emptySet<String>()
    private var reportedDotnetFailure: String? = null

    /** Formats the files DressSharp is set up for: those with a local tool manifest and an `.editorconfig` in scope. */
    fun format(files: Collection<VirtualFile>, trigger: FormatTrigger) {
        scope.launch(Dispatchers.IO) {
            val skipped = ArrayList<String>()
            val roots = LinkedHashSet<Path>()
            files.mapNotNull { file -> file.toNioPathOrNull()?.toAbsolutePath()?.normalize()?.let { file to it } }.forEach { (file, path) ->
                when (val eligibility = Eligibility.of(path)) {
                    is Eligibility.Eligible -> {
                        enqueue(path, file, eligibility.manifest, trigger)
                        roots.add(eligibility.manifest.root)
                    }
                    Eligibility.NoToolManifest ->
                        skipped += "<b>${file.name.escaped()}</b>: no <code>.config/dotnet-tools.json</code> above it installs DressSharp. " +
                            "Run <code>dotnet tool install DressSharp</code> in the repository."
                    Eligibility.NoEditorConfig ->
                        skipped += "<b>${file.name.escaped()}</b>: no <code>.editorconfig</code> applies to it. Run <code>dotnet dress init</code> to create one."
                }
            }
            if (trigger == FormatTrigger.MANUAL && skipped.isNotEmpty()) notify(NotificationType.WARNING, skipped.joinToString("<br>"))
            roots.forEach(::scheduleRun)
        }
    }

    private fun enqueue(path: Path, file: VirtualFile, manifest: ToolManifest, trigger: FormatTrigger) = synchronized(lock) {
        val batch = pending.getOrPut(manifest.root) { Batch(manifest) }
        batch.files[path] = file
        if (trigger == FormatTrigger.MANUAL) batch.manual = true
    }

    private fun scheduleRun(root: Path) {
        if (synchronized(lock) { running.add(root) }) {
            scope.launch {
                try {
                    var batch = takeAfterDelay(root)
                    while (batch != null) {
                        executeSafely(batch)
                        batch = takeAfterDelay(root)
                    }
                } catch (e: CancellationException) {
                    synchronized(lock) {
                        running.remove(root)
                        pending.remove(root)
                    }
                    throw e
                }
            }
        }
    }

    private suspend fun takeAfterDelay(root: Path): Batch? {
        delay(coalesceDelayMs)
        return takePending(root)
    }

    /** Takes the next batch for [root], or hands the root back when nothing is waiting. */
    private fun takePending(root: Path): Batch? = synchronized(lock) {
        pending.remove(root) ?: run {
            running.remove(root)
            null
        }
    }

    private suspend fun executeSafely(batch: Batch) {
        try {
            execute(batch)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            log.warn("DressSharp run failed", e)
            notify(NotificationType.ERROR, "DressSharp failed: ${e.message.orEmpty().escaped()}")
        }
    }

    private suspend fun execute(batch: Batch) {
        val dotnet = locateDotnet(manual = batch.manual) ?: return
        val invocation = DressSharpInvocation(batch.manifest.root, batch.files.keys)
        val output = runProcess(invocation.commandLine(dotnet), "Formatting with DressSharp") {
            try {
                DressSharpWorkers.getInstance(project).formatFiles(batch.manifest, invocation, dotnet)
            } catch (e: IOException) {
                ProcessOutput().apply {
                    exitCode = 2
                    appendStderr(e.message.orEmpty())
                }
            }
        } ?: return
        val result = DressSharpOutput.parse(output.stdout, invocation.includes)
        val changed = result.changed.mapNotNull { batch.files[batch.manifest.root.resolve(it).normalize()] }
        if (changed.isNotEmpty()) VfsUtil.markDirtyAndRefresh(true, false, false, *changed.toTypedArray())

        if (output.exitCode != 0) {
            reportFailure(batch, dotnet, output)
        } else {
            reportWarnings(output.stderr, batch.manual)
            if (batch.manual) reportOutcome(batch, result)
        }
    }

    /** Runs a command under a cancellable background progress. Returns null when the user canceled it. */
    private suspend fun runProcess(
        commandLine: GeneralCommandLine,
        title: String,
        execute: suspend () -> ProcessOutput = { coroutineToIndicator { indicator -> CapturingProcessHandler(commandLine).runProcessWithProgressIndicator(indicator) } },
    ): ProcessOutput? = try {
        withBackgroundProgress(project, title, cancellable = true) {
            execute()
        }
    } catch (e: ExecutionException) {
        notify(
            NotificationType.ERROR,
            "DressSharp couldn't start <code>${commandLine.exePath.escaped()}</code>: ${e.message.orEmpty().escaped()}",
            listOf(openSettingsAction()),
        )
        null
    } catch (e: CancellationException) {
        currentCoroutineContext().ensureActive()
        null
    }

    internal fun locateDotnet(manual: Boolean = false): Path? = when (val located = DotnetLocator.locate(DressSharpApplicationSettings.getInstance().state.dotnetPath)) {
        is DotnetLocator.Result.Found -> {
            synchronized(lock) { reportedDotnetFailure = null }
            located.path
        }
        is DotnetLocator.Result.ConfiguredPathMissing -> {
            reportDotnetFailure(
                "The dotnet executable set in the DressSharp settings doesn't exist: <code>${located.path.escaped()}</code>",
                manual,
            )
            null
        }
        DotnetLocator.Result.NotFound -> {
            reportDotnetFailure(
                "dotnet wasn't found on the PATH. Set its location in Settings | Tools | DressSharp.",
                manual,
            )
            null
        }
    }

    private fun reportDotnetFailure(message: String, manual: Boolean) {
        val isNew = synchronized(lock) {
            val changed = message != reportedDotnetFailure
            reportedDotnetFailure = message
            changed
        }
        if (manual || isNew) notify(NotificationType.ERROR, message, listOf(openSettingsAction()))
    }

    private fun reportFailure(batch: Batch, dotnet: Path, output: ProcessOutput) {
        val needsRestore = "dotnet tool restore" in output.stderr || "dotnet tool restore" in output.stdout
        val actions = if (needsRestore) listOf(restoreToolsAction(batch, dotnet)) else emptyList()
        notify(NotificationType.ERROR, "DressSharp failed with exit code ${output.exitCode}.<br>${output.details()}", actions)
    }

    /** The local tool is in the manifest but hasn't been restored on this machine yet. */
    private fun restoreToolsAction(batch: Batch, dotnet: Path): AnAction = NotificationAction.createSimpleExpiring("Run 'dotnet tool restore'") {
        scope.launch {
            val restore = GeneralCommandLine(dotnet.toString(), "tool", "restore")
                .withWorkingDirectory(batch.manifest.root)
                .withCharset(Charsets.UTF_8)
                .withEnvironment("DOTNET_NOLOGO", "1")
            val output = runProcess(restore, "Restoring .NET tools") ?: return@launch
            if (output.exitCode == 0) {
                format(batch.files.values, FormatTrigger.MANUAL)
            } else {
                notify(NotificationType.ERROR, "<code>dotnet tool restore</code> failed with exit code ${output.exitCode}.<br>${output.details()}")
            }
        }
    }

    /** Warnings such as an EditorConfig with no DressSharp rules. Each new set is shown once per session, or every time the user asks. */
    internal fun reportWarnings(stderr: String, manual: Boolean) {
        val warnings = DressSharpOutput.parseWarnings(stderr)
        if (warnings.isEmpty()) return
        val isNew = synchronized(lock) {
            val changed = warnings != reportedWarnings
            reportedWarnings = warnings
            changed
        }
        if (manual || isNew) notify(NotificationType.WARNING, warnings.joinToString("<br>") { it.removePrefix("warning: ").escaped() })
    }

    private suspend fun reportOutcome(batch: Batch, result: DressSharpOutput) {
        val names = batch.files.values.joinToString { it.name }
        when {
            result.selected == 0 -> notify(
                NotificationType.INFORMATION,
                "DressSharp didn't select ${names.escaped()}. Files that Git ignores, and generated files, are skipped.",
            )
            result.formatted == 0 -> showStatus("DressSharp: $names already matched your EditorConfig rules")
            else -> showStatus("DressSharp formatted ${result.formatted} of ${result.selected} ${if (result.selected == 1) "file" else "files"}")
        }
    }

    private suspend fun showStatus(text: String) = withContext(Dispatchers.EDT) {
        StatusBar.Info.set(text, project)
    }

    private fun openSettingsAction(): AnAction = NotificationAction.createSimpleExpiring("Open settings") {
        ShowSettingsUtil.getInstance().showSettingsDialog(project, DressSharpConfigurable::class.java)
    }

    internal fun notify(type: NotificationType, content: String, actions: List<AnAction> = emptyList()) {
        val notification = NotificationGroupManager.getInstance().getNotificationGroup(notificationGroup).createNotification("DressSharp", content, type)
        actions.forEach(notification::addAction)
        notification.notify(project)
    }

    /** The first lines a process wrote about what went wrong, ready for a notification. */
    private fun ProcessOutput.details(): String =
        stderr.ifBlank { stdout }.trim().lines().take(maxReportedLines).joinToString("<br>") { it.escaped() }

    private fun String.escaped(): String = StringUtil.escapeXmlEntities(this)

    companion object {
        const val notificationGroup = "DressSharp"
        private const val coalesceDelayMs = 200L
        private const val maxReportedLines = 12
        private val log = logger<DressSharpFormatter>()

        fun getInstance(project: Project): DressSharpFormatter = project.service()
    }
}
