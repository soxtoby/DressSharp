package com.soxtoby.dresssharp.rider

import com.intellij.execution.ExecutionException
import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.execution.process.CapturingProcessHandler
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
import com.intellij.openapi.progress.coroutineToIndicator
import com.intellij.openapi.project.Project
import com.intellij.openapi.util.text.StringUtil
import com.intellij.openapi.vfs.toNioPathOrNull
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.nio.file.Path

/**
 * Formats the statement or block the user has just finished typing. The unsaved document goes through
 * `dotnet dress format --stdin`, and of the changes DressSharp would make to the file, only those on
 * the lines the typed `;` or `}` settled are applied, so the rest of the file stays as the user left it.
 *
 * Nothing is read until a moment after the keystroke, so that Rider's own typing assists, such as
 * indenting a closing brace, have finished first. Another trigger in the same document before then
 * starts the wait again, and a document that changes while DressSharp runs is left alone.
 */
@Service(Service.Level.PROJECT)
class DressSharpTypingFormatter(private val project: Project, private val scope: CoroutineScope) {
    private class Snapshot(val text: String, val stamp: Long, val caretOffset: Int)

    private val lock = Any()
    private val pending = HashMap<Document, Job>()
    private var reportedFailure: String? = null

    fun formatAfterTyping(editor: Editor, typed: Char) {
        val document = editor.document
        val file = FileDocumentManager.getInstance().getFile(document) ?: return
        if (!file.isCSharpSourceFile || !document.isWritable) return
        val path = file.toNioPathOrNull()?.toAbsolutePath()?.normalize() ?: return
        synchronized(lock) {
            pending.remove(document)?.cancel()
            pending[document] = scope.launch {
                try {
                    format(editor, document, path, typed)
                } catch (e: CancellationException) {
                    throw e
                } catch (e: Exception) {
                    LOG.warn("DressSharp formatting after typing failed", e)
                } finally {
                    synchronized(lock) { if (pending[document] === coroutineContext.job) pending.remove(document) }
                }
            }
        }
    }

    private suspend fun format(editor: Editor, document: Document, path: Path, typed: Char) {
        delay(SETTLE_DELAY_MS)
        val manifest = (Eligibility.of(path) as? Eligibility.Eligible)?.manifest ?: return
        val formatter = DressSharpFormatter.getInstance(project)
        val dotnet = formatter.locateDotnet() ?: return
        val snapshot = withContext(Dispatchers.EDT) {
            Snapshot(document.immutableCharSequence.toString(), document.modificationStamp, editor.caretModel.offset)
        }
        val region = TriggerRegion.of(snapshot.text, snapshot.caretOffset, typed)
        LOG.debug { "DressSharp formatting lines ${region.first + 1}-${region.last + 1} of ${path.fileName} after '$typed'" }

        val output = run(DressSharpInvocation.standardInput(manifest.root, path, dotnet), snapshot.text) ?: return
        if (output.exitCode != 0) {
            reportFailure(output)
            return
        }
        formatter.reportWarnings(output.stderr, manual = false)

        val edits = LineEdits.between(snapshot.text, StringUtil.convertLineSeparators(output.stdout)).filter { it.touches(region) }
        if (edits.isEmpty()) return
        withContext(Dispatchers.EDT) {
            if (document.modificationStamp != snapshot.stamp || !document.isWritable) return@withContext
            WriteCommandAction.runWriteCommandAction(project, "Format with DressSharp", null, {
                for (edit in edits.asReversed()) document.replaceString(edit.start, edit.end, edit.replacement)
            })
        }
    }

    /** Runs DressSharp with [input] on its standard input. Returns null when it couldn't start, or the request was superseded. */
    private suspend fun run(commandLine: GeneralCommandLine, input: String): ProcessOutput? {
        val handler = try {
            CapturingProcessHandler(commandLine)
        } catch (e: ExecutionException) {
            reportFailure("DressSharp couldn't start <code>${commandLine.exePath.escaped()}</code>: ${e.message.orEmpty().escaped()}")
            return null
        }
        return try {
            coroutineScope {
                launch(Dispatchers.IO) { handler.processInput.use { it.write(input.toByteArray(Charsets.UTF_8)) } }
                coroutineToIndicator { indicator -> handler.runProcessWithProgressIndicator(indicator) }
            }
        } catch (e: CancellationException) {
            handler.destroyProcess()
            throw e
        }
    }

    private fun reportFailure(output: ProcessOutput) {
        val details = output.stderr.ifBlank { output.stdout }.trim().lines().take(MAX_REPORTED_LINES).joinToString("<br>") { it.escaped() }
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
        private const val SETTLE_DELAY_MS = 150L
        private const val MAX_REPORTED_LINES = 12
        private val LOG = logger<DressSharpTypingFormatter>()

        fun getInstance(project: Project): DressSharpTypingFormatter = project.service()
    }
}
