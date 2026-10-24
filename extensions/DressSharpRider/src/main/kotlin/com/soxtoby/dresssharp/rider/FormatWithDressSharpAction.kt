package com.soxtoby.dresssharp.rider

import com.intellij.openapi.actionSystem.ActionUpdateThread
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.CommonDataKeys
import com.intellij.openapi.fileEditor.FileDocumentManager
import com.intellij.openapi.project.DumbAware
import com.intellij.openapi.vfs.VirtualFile

/** Code | Format with DressSharp: formats the current file, or the files selected in the Solution Explorer. */
class FormatWithDressSharpAction : AnAction(), DumbAware {
    override fun getActionUpdateThread(): ActionUpdateThread = ActionUpdateThread.BGT

    override fun update(event: AnActionEvent) {
        event.presentation.isEnabled = event.project != null && selectedFiles(event).isNotEmpty()
    }

    override fun actionPerformed(event: AnActionEvent) {
        val project = event.project ?: return
        val files = selectedFiles(event)
        if (files.isEmpty()) return

        // DressSharp reads files from disk, so what the editor holds has to be there first.
        val fileDocumentManager = FileDocumentManager.getInstance()
        files.mapNotNull { fileDocumentManager.getCachedDocument(it) }
            .filter { fileDocumentManager.isDocumentUnsaved(it) }
            .forEach { fileDocumentManager.saveDocument(it) }

        DressSharpFormatter.getInstance(project).format(files, FormatTrigger.MANUAL)
    }

    private fun selectedFiles(event: AnActionEvent): List<VirtualFile> =
        event.getData(CommonDataKeys.VIRTUAL_FILE_ARRAY)?.filter { it.isCSharpSourceFile } ?: emptyList()
}
