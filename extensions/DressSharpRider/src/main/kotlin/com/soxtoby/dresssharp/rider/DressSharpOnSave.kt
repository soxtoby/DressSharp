package com.soxtoby.dresssharp.rider

import com.intellij.ide.actionsOnSave.ActionOnSaveBackedByOwnConfigurable
import com.intellij.ide.actionsOnSave.ActionOnSaveComment
import com.intellij.ide.actionsOnSave.ActionOnSaveContext
import com.intellij.ide.actionsOnSave.ActionOnSaveInfo
import com.intellij.ide.actionsOnSave.ActionOnSaveInfoProvider
import com.intellij.ide.actionsOnSave.impl.ActionsOnSaveFileDocumentManagerListener
import com.intellij.openapi.editor.Document
import com.intellij.openapi.fileEditor.FileDocumentManager
import com.intellij.openapi.project.Project
import com.intellij.ui.components.ActionLink

/** Formats the C# files the IDE has just saved. */
class DressSharpOnSave : ActionsOnSaveFileDocumentManagerListener.ActionOnSave() {
    override fun isEnabledForProject(project: Project): Boolean = DressSharpProjectSettings.getInstance(project).state.formatOnSave

    override fun processDocuments(project: Project, documents: Array<Document>) {
        val fileDocumentManager = FileDocumentManager.getInstance()
        val files = documents.mapNotNull { fileDocumentManager.getFile(it) }.filter { it.isCSharpSourceFile }
        if (files.isNotEmpty()) DressSharpFormatter.getInstance(project).format(files, FormatTrigger.SAVE)
    }
}

/** The DressSharp row on Settings | Tools | Actions on Save. */
class DressSharpOnSaveInfoProvider : ActionOnSaveInfoProvider() {
    override fun getActionOnSaveInfos(context: ActionOnSaveContext): Collection<ActionOnSaveInfo> = listOf(DressSharpOnSaveInfo(context))

    override fun getSearchableOptions(): Collection<String> = listOf("DressSharp")
}

private class DressSharpOnSaveInfo(context: ActionOnSaveContext) :
    ActionOnSaveBackedByOwnConfigurable<DressSharpConfigurable>(context, DressSharpConfigurable.ID, DressSharpConfigurable::class.java) {

    override fun getActionOnSaveName(): String = "Format with DressSharp"

    override fun isActionOnSaveEnabledAccordingToStoredState(): Boolean = DressSharpProjectSettings.getInstance(project).state.formatOnSave

    override fun isActionOnSaveEnabledAccordingToUiState(configurable: DressSharpConfigurable): Boolean =
        configurable.formatOnSaveCheckBox?.isSelected ?: isActionOnSaveEnabledAccordingToStoredState()

    override fun setActionOnSaveEnabled(configurable: DressSharpConfigurable, enabled: Boolean) {
        configurable.formatOnSaveCheckBox?.isSelected = enabled
    }

    override fun getCommentAccordingToStoredState(): ActionOnSaveComment =
        ActionOnSaveComment.info("C# files with DressSharp in a local tool manifest and an .editorconfig in scope")

    override fun getActionLinks(): List<ActionLink> = listOf(createGoToPageInSettingsLink(DressSharpConfigurable.ID))
}
