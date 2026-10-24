package com.soxtoby.dresssharp.rider

import com.intellij.execution.process.ProcessOutput
import com.intellij.notification.NotificationType
import com.intellij.openapi.application.EDT
import com.intellij.openapi.command.WriteCommandAction
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.service
import com.intellij.openapi.diagnostic.debug
import com.intellij.openapi.diagnostic.logger
import com.intellij.openapi.editor.Document
import com.intellij.openapi.editor.Editor
import com.intellij.openapi.fileEditor.FileDocumentManager
import com.intellij.openapi.project.Project
import com.intellij.openapi.util.text.StringUtil
import com.intellij.openapi.vfs.toNioPathOrNull
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.nio.file.Path

/**
 * Formats the statement or block the user has just finished typing. The unsaved document goes through
 * DressSharp, and of the changes it would make to the file, only those on
 * the lines the typed `;` or `}` settled are applied, so the rest of the file stays as the user left it.
 *
 * Nothing is read until a moment after the keystroke, so that Rider's own typing assists, such as
 * indenting a closing brace, have finished first. Another trigger in the same document before then
 * starts the wait again, and a document that changes while DressSharp runs is left alone.
 */
@Service(Service.Level.PROJECT)
class DressSharpTypingFormatter(private val project: Project, private val scope: CoroutineScope) {
    private val lock = Any()
    private val pending = HashMap<Document, Job>()
    private var reportedFailure: String? = null

    fun formatAfterTyping(editor: Editor, typed: Char) {
        val document = editor.document
        val file = FileDocumentManager.getInstance().getFile(document) ?: return
        if (!file.isCSharpSourceFile || !document.isWritable) return
        val path = file.toNioPathOrNull()?.toAbsolutePath()?.normalize() ?: return
        val trigger = TypingTrigger(document, editor.selectionModel.selectionStart, typed, allowOvertype = !editor.selectionModel.hasSelection())
        synchronized(lock) {
            pending.remove(document)?.cancel()
            val job = scope.launch(Dispatchers.IO, start = CoroutineStart.LAZY) {
                try {
                    format(document, path, trigger)
                } catch (e: CancellationException) {
                    throw e
                } catch (e: Exception) {
                    log.warn("DressSharp formatting after typing failed", e)
                }
            }
            pending[document] = job
            job.invokeOnCompletion {
                trigger.close()
                synchronized(lock) { if (pending[document] === job) pending.remove(document) }
            }
            job.start()
        }
    }

    private suspend fun format(document: Document, path: Path, trigger: TypingTrigger) {
        delay(settleDelayMs)
        val manifest = (Eligibility.of(path) as? Eligibility.Eligible)?.manifest ?: return
        val formatter = DressSharpFormatter.getInstance(project)
        val dotnet = formatter.locateDotnet() ?: return
        val snapshot = withContext(Dispatchers.EDT) {
            val text = document.immutableCharSequence.toString()
            trigger.region(text)?.let { TypingSnapshot(text, document.modificationStamp, it) }
        } ?: return
        log.debug { "DressSharp formatting lines ${snapshot.region.first + 1}-${snapshot.region.last + 1} of ${path.fileName}" }

        val output = run(manifest, path, dotnet, snapshot.text) ?: return
        if (output.exitCode == 0) {
            formatter.reportWarnings(output.stderr, manual = false)
            apply(document, snapshot, output.stdout)
        } else {
            reportFailure(output)
        }
    }

    private suspend fun apply(document: Document, snapshot: TypingSnapshot, formatted: String) {
        val edits = LineEdits.forTyping(snapshot.text, StringUtil.convertLineSeparators(formatted), snapshot.region)
        if (edits.isEmpty()) return
        withContext(Dispatchers.EDT) {
            WriteCommandAction.runWriteCommandAction(project, "Format with DressSharp", null, {
                snapshot.applyTo(document, edits)
            })
        }
    }

    private suspend fun run(manifest: ToolManifest, path: Path, dotnet: Path, input: String): ProcessOutput? = try {
        DressSharpWorkers.getInstance(project).format(manifest, path, dotnet, input)
    } catch (e: CancellationException) {
        throw e
    } catch (e: Exception) {
        reportFailure("DressSharp formatting failed: ${e.message.orEmpty().escaped()}")
        null
    }

    private fun reportFailure(output: ProcessOutput) {
        val details = output.stderr.ifBlank { output.stdout }.trim().lines().take(maxReportedLines).joinToString("<br>") { it.escaped() }
        reportFailure("DressSharp failed with exit code ${output.exitCode}.<br>$details")
    }

    /** The same failure on every keystroke is reported once. */
    private fun reportFailure(message: String) {
        val isNew = synchronized(lock) {
            val changed = message != reportedFailure
            reportedFailure = message
            changed
        }
        if (isNew) DressSharpFormatter.getInstance(project).notify(NotificationType.ERROR, message)
    }

    private fun String.escaped(): String = StringUtil.escapeXmlEntities(this)

    companion object {
        /** Long enough for Rider's typing assists to apply, short enough to read as part of the keystroke. */
        private const val settleDelayMs = 150L
        private const val maxReportedLines = 12
        private val log = logger<DressSharpTypingFormatter>()

        fun getInstance(project: Project): DressSharpTypingFormatter = project.service()
    }
}

internal class TypingSnapshot(val text: String, private val stamp: Long, val region: IntRange) {
    fun applyTo(document: Document, edits: List<LineEdit>) {
        if (document.modificationStamp == stamp && document.isWritable) {
            edits.asReversed().forEach { document.replaceString(it.start, it.end, it.replacement) }
        }
    }
}
