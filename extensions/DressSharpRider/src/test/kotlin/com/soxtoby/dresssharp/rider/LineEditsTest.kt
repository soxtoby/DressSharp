package com.soxtoby.dresssharp.rider

import org.junit.jupiter.api.Assertions.assertEquals
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
    fun `typing applies a complete transformation within the finished lines`() {
        val before = "class C\n{\nint M() { return 1; }\n}\n"
        val after = "class C\n{\nint M() => 1;\n}\n"

        val result = apply(before, LineEdits.forTyping(before, after, 2..2))

        assertEquals(after, result)
    }

    @Test
    fun `typing preserves both imports when sorting crosses the finished line`() {
        val before = "using Acme;\nusing System;\n"
        val after = "using System;\nusing Acme;\n"

        val result = apply(before, LineEdits.forTyping(before, after, 1..1))

        assertEquals(before, result)
    }

    @Test
    fun `typing does not apply a hunk extending into a neighboring statement`() {
        val before = "int x=1;\nint y=2;\n"
        val after = "int x = 1;\nint y = 2;\n"

        val result = apply(before, LineEdits.forTyping(before, after, 1..1))

        assertEquals(before, result)
    }

    @Test
    fun `typing applies insertions and deletions together when the region contains both imports`() {
        val before = "using Acme;\nusing System;\n"
        val after = "using System;\nusing Acme;\n"

        val result = apply(before, LineEdits.forTyping(before, after, 0..1))

        assertEquals(after, result)
    }

    private fun apply(text: String, edits: List<LineEdit>): String = StringBuilder(text).apply {
        edits.asReversed().forEach { replace(it.start, it.end, it.replacement) }
    }.toString()
}
