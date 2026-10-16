package com.soxtoby.dresssharp.rider

import java.nio.file.Files
import java.nio.file.Path

/** Where EditorConfig rules for a file come from. */
object EditorConfigScope {
    const val FILE_NAME = ".editorconfig"

    /** The nearest `.editorconfig` in [file]'s directory or any directory above it, or null when none applies. */
    fun nearest(file: Path): Path? = generateSequence(file.toAbsolutePath().normalize().parent) { it.parent }
        .map { it.resolve(FILE_NAME) }
        .firstOrNull { Files.isRegularFile(it) }
}
