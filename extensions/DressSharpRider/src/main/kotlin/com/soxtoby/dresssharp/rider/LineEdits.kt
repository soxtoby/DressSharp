package com.soxtoby.dresssharp.rider

import com.intellij.util.diff.Diff

/**
 * Whole lines of a text replaced by other lines: lines [startLine] until [endLine], at offsets [start]
 * until [end] of that text, become [replacement]. An insertion has no lines of its own, so its start and
 * end are the same line and offset.
 */
data class LineEdit(val startLine: Int, val endLine: Int, val start: Int, val end: Int, val replacement: String) {
    /** Whether the edit changes one of [lines], or inserts beside one of them. */
    fun touches(lines: IntRange): Boolean =
        if (startLine == endLine) startLine in lines.first..lines.last + 1
        else startLine <= lines.last && endLine > lines.first
}

object LineEdits {
    /** The line edits that turn [before] into [after]. Both are expected to end lines with `\n`, as editor documents do. */
    fun between(before: String, after: String): List<LineEdit> {
        val oldLines = lines(before)
        val newLines = lines(after)
        val offsets = IntArray(oldLines.size + 1)
        for (index in oldLines.indices) offsets[index + 1] = offsets[index] + oldLines[index].length

        val edits = ArrayList<LineEdit>()
        var change: Diff.Change? = Diff.buildChanges(oldLines.toTypedArray(), newLines.toTypedArray())
        while (change != null) {
            val replacement = buildString {
                for (index in change.line1 until change.line1 + change.inserted) append(newLines[index])
            }
            val endLine = change.line0 + change.deleted
            edits += LineEdit(change.line0, endLine, offsets[change.line0], offsets[endLine], replacement)
            change = change.link
        }
        return edits
    }

    /** The text split after each line feed, so that joining the lines gives the text back. */
    private fun lines(text: String): List<String> {
        val lines = ArrayList<String>()
        var start = 0
        while (start < text.length) {
            val lineFeed = text.indexOf('\n', start)
            val end = if (lineFeed < 0) text.length else lineFeed + 1
            lines += text.substring(start, end)
            start = end
        }
        return lines
    }
}
