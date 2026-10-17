package com.soxtoby.dresssharp.rider

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

class LineEditsTest {
    @Test
    fun `identical texts need no edits`() {
        assertEquals(emptyList<LineEdit>(), LineEdits.between("a\nb\n", "a\nb\n"))
    }

    @Test
    fun `separate changes become separate edits with offsets into the original`() {
        val before = "class C\n{\nint x=1;\nint y = 2;\nint z=3;\n}\n"
        val after = "class C\n{\nint x = 1;\nint y = 2;\nint z = 3;\n}\n"

        val edits = LineEdits.between(before, after)

        assertEquals(
            listOf(
                LineEdit(startLine = 2, endLine = 3, start = 10, end = 19, replacement = "int x = 1;\n"),
                LineEdit(startLine = 4, endLine = 5, start = 30, end = 39, replacement = "int z = 3;\n"),
            ),
            edits,
        )
    }

    @Test
    fun `applying every edit in reverse reproduces the formatted text`() {
        val before = "a\nb\nc\nd"
        val after = "a\nB\nb2\nd\ne\n"

        val edits = LineEdits.between(before, after)

        val builder = StringBuilder(before)
        for (edit in edits.asReversed()) builder.replace(edit.start, edit.end, edit.replacement)
        assertEquals(after, builder.toString())
    }

    @Test
    fun `an inserted line has no lines of its own`() {
        val edits = LineEdits.between("a\nb\n", "a\n\nb\n")

        assertEquals(listOf(LineEdit(startLine = 1, endLine = 1, start = 2, end = 2, replacement = "\n")), edits)
    }

    @Test
    fun `an edit touches the lines it replaces and an insertion touches the lines around it`() {
        val replacement = LineEdit(startLine = 2, endLine = 4, start = 0, end = 0, replacement = "")
        assertTrue(replacement.touches(3..3))
        assertTrue(replacement.touches(0..2))
        assertFalse(replacement.touches(4..6))
        assertFalse(replacement.touches(0..1))

        val insertion = LineEdit(startLine = 5, endLine = 5, start = 0, end = 0, replacement = "\n")
        assertTrue(insertion.touches(5..7), "inserted before the first line")
        assertTrue(insertion.touches(2..4), "inserted after the last line")
        assertFalse(insertion.touches(6..8))
        assertFalse(insertion.touches(1..3))
    }
}
