package com.soxtoby.dresssharp.rider

import com.intellij.openapi.vfs.VirtualFile
import java.nio.file.Path

/** Whether DressSharp is set up to format a file, and why not when it isn't. */
sealed interface Eligibility {
    data class Eligible(val manifest: ToolManifest, val editorConfig: Path) : Eligibility

    data object NoToolManifest : Eligibility

    data object NoEditorConfig : Eligibility

    companion object {
        fun of(file: Path): Eligibility {
            val manifest = DressSharpToolManifest.find(file) ?: return NoToolManifest
            val editorConfig = EditorConfigScope.nearest(file) ?: return NoEditorConfig
            return Eligible(manifest, editorConfig)
        }
    }
}

/** A C# source file on disk, which is the only kind of file DressSharp formats. */
val VirtualFile.isCSharpSourceFile: Boolean
    get() = !isDirectory && isInLocalFileSystem && "cs".equals(extension, ignoreCase = true)
