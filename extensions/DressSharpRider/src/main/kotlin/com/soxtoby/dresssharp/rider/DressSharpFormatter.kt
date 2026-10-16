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
import java.nio.file.Path

enum class FormatTrigger {
    /** The IDE saved the file; files DressSharp isn't set up for are skipped quietly. */
    SAVE,

    /** The user asked; every reason a file wasn't formatted is reported. */
    MANUAL,
}

/**
 * Runs DressSharp over files as a local tool, one process per tool manifest. Requests that arrive
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

    /** Formats the files DressSharp is set up for: those with a local tool manifest and an `.editorconfig` in scope. */
    fun format(files: Collection<VirtualFile>, trigger: FormatTrigger) {
        scope.launch(Dispatchers.IO) {
            val skipped = ArrayList<String>()
            val roots = LinkedHashSet<Path>()
            for (file in files) {
                val path = file.toNioPathOrNull()?.toAbsolutePath()?.normalize() ?: continue
                when (val eligibility = Eligibility.of(path)) {
                    is Eligibility.Eligible -> {
                        enqueue(path, file, eligibility.manifest, trigger)
                        roots.add(eligibility.manifest.root)
                    }
                    Eligibility.NoToolManifest ->
                        skipped += "<b>${file.name.escaped()}</b>: no <code>.config/dotnet-tools.json</code> above it installs DressSharp. " +
                            "Run <code>dotnet new tool-manifest</code> and <code>dotnet tool install DressSharp</code> in the repository."
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
        synchronized(lock) { if (!running.add(root)) return }
        scope.launch {
            try {
                while (true) {
                    delay(COALESCE_DELAY_MS)
                    val batch = takePending(root) ?: break
                    try {
                        execute(batch)
                    } catch (e: CancellationException) {
                        throw e
                    } catch (e: Exception) {
                        LOG.warn("DressSharp run failed", e)
                        notify(NotificationType.ERROR, "DressSharp failed: ${e.message.orEmpty().escaped()}")
                    }
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

    /** Takes the next batch for [root], or hands the root back when nothing is waiting. */
    private fun takePending(root: Path): Batch? = synchronized(lock) {
        pending.remove(root) ?: run {
            running.remove(root)
            null
        }
    }

    private suspend fun execute(batch: Batch) {
        val dotnet = locateDotnet() ?: return
        val invocation = DressSharpInvocation(batch.manifest.root, batch.files.keys)
        val output = runProcess(invocation.commandLine(dotnet), "Formatting with DressSharp") ?: return
        val result = DressSharpOutput.parse(output.stdout, invocation.includes)

        if (output.exitCode != 0) {
            reportFailure(batch, dotnet, output)
            return
        }
        if (result.formatted != 0) {
            // DressSharp rewrote the files on disk; once the VFS sees that, the editors reload them.
            VfsUtil.markDirtyAndRefresh(true, false, false, *batch.files.values.toTypedArray())
        }
        reportWarnings(output.stderr, batch.manual)
        if (batch.manual) reportOutcome(batch, result)
    }

    /** Runs a command under a cancellable background progress. Returns null when the user cancelled it. */
    private suspend fun runProcess(commandLine: GeneralCommandLine, title: String): ProcessOutput? {
        try {
            return withBackgroundProgress(project, title, cancellable = true) {
                coroutineToIndicator { indicator -> CapturingProcessHandler(commandLine).runProcessWithProgressIndicator(indicator) }
            }
        } catch (e: ExecutionException) {
            notify(
                NotificationType.ERROR,
                "DressSharp couldn't start <code>${commandLine.exePath.escaped()}</code>: ${e.message.orEmpty().escaped()}",
                listOf(openSettingsAction()),
            )
            return null
        } catch (e: CancellationException) {
            currentCoroutineContext().ensureActive()
            return null
        }
    }

    private fun locateDotnet(): Path? = when (val located = DotnetLocator.locate(DressSharpApplicationSettings.getInstance().state.dotnetPath)) {
        is DotnetLocator.Result.Found -> located.path
        is DotnetLocator.Result.ConfiguredPathMissing -> {
            notify(
                NotificationType.ERROR,
                "The dotnet executable set in the DressSharp settings doesn't exist: <code>${located.path.escaped()}</code>",
                listOf(openSettingsAction()),
            )
            null
        }
        DotnetLocator.Result.NotFound -> {
            notify(
                NotificationType.ERROR,
                "dotnet wasn't found on the PATH. Set its location in Settings | Tools | DressSharp.",
                listOf(openSettingsAction()),
            )
            null
        }
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
    private fun reportWarnings(stderr: String, manual: Boolean) {
        val warnings = stderr.lines().map { it.trim() }.filter { it.isNotEmpty() }.toSet()
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

    private fun notify(type: NotificationType, content: String, actions: List<AnAction> = emptyList()) {
        val notification = NotificationGroupManager.getInstance().getNotificationGroup(NOTIFICATION_GROUP).createNotification("DressSharp", content, type)
        actions.forEach(notification::addAction)
        notification.notify(project)
    }

    /** The first lines a process wrote about what went wrong, ready for a notification. */
    private fun ProcessOutput.details(): String =
        stderr.ifBlank { stdout }.trim().lines().take(MAX_REPORTED_LINES).joinToString("<br>") { it.escaped() }

    private fun String.escaped(): String = StringUtil.escapeXmlEntities(this)

    companion object {
        const val NOTIFICATION_GROUP = "DressSharp"
        private const val COALESCE_DELAY_MS = 200L
        private const val MAX_REPORTED_LINES = 12
        private val LOG = logger<DressSharpFormatter>()

        fun getInstance(project: Project): DressSharpFormatter = project.service()
    }
}
