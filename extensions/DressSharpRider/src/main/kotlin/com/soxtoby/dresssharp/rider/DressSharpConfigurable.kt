package com.soxtoby.dresssharp.rider

import com.intellij.openapi.fileChooser.FileChooserDescriptorFactory
import com.intellij.openapi.options.BoundSearchableConfigurable
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.DialogPanel
import com.intellij.ui.components.JBCheckBox
import com.intellij.ui.dsl.builder.AlignX
import com.intellij.ui.dsl.builder.bindSelected
import com.intellij.ui.dsl.builder.bindText
import com.intellij.ui.dsl.builder.panel

/** Settings | Tools | DressSharp. */
class DressSharpConfigurable(private val project: Project) : BoundSearchableConfigurable("DressSharp", ID, ID) {
    /** Exposed so the Actions on Save page can mirror the checkbox while this page is open. */
    var formatOnSaveCheckBox: JBCheckBox? = null
        private set

    override fun createPanel(): DialogPanel {
        val projectState = DressSharpProjectSettings.getInstance(project).state
        val applicationState = DressSharpApplicationSettings.getInstance().state
        return panel {
            row {
                formatOnSaveCheckBox = checkBox("Format C# files with DressSharp on save")
                    .bindSelected(projectState::formatOnSave)
                    .comment(
                        "Runs <code>dotnet dress</code> on a saved file when a local tool manifest above it installs DressSharp " +
                            "and an <code>.editorconfig</code> applies to it. Other files are left alone.",
                    )
                    .component
            }
            row {
                checkBox("Format the statement or block just finished when you type ; or }")
                    .bindSelected(projectState::formatOnType)
                    .comment(
                        "Formats the unsaved file after each <code>;</code> or <code>}</code>, " +
                            "and applies only the changes to the lines just completed.",
                    )
            }
            row {
                checkBox("Keep a formatter process running in the background")
                    .bindSelected(projectState::useBackgroundProcess)
                    .comment(
                        "Reuses one process per repository for save, typing, and manual formatting until this Rider project closes. " +
                            "Uses more idle memory and requires a formatter with background process support. " +
                            "When disabled, each formatting run starts a new process.",
                    )
            }
            row("dotnet executable:") {
                textFieldWithBrowseButton(FileChooserDescriptorFactory.singleFile().withTitle("Select dotnet Executable"), project)
                    .bindText({ applicationState.dotnetPath.orEmpty() }, { applicationState.dotnetPath = it.trim().ifEmpty { null } })
                    .comment("Leave empty to use the dotnet found on the PATH.")
                    .align(AlignX.FILL)
            }
        }
    }

    override fun apply() {
        super.apply()
        if (!DressSharpProjectSettings.getInstance(project).state.useBackgroundProcess) DressSharpWorkers.getInstance(project).stop()
    }

    companion object {
        const val ID = "dresssharp.settings"
    }
}
