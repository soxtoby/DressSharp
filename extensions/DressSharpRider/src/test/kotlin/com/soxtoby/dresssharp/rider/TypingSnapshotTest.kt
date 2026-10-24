package com.soxtoby.dresssharp.rider

import com.intellij.openapi.editor.impl.DocumentImpl
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

class TypingSnapshotTest : DocumentTestContext() {
    @Test
    fun `an unchanged document receives the formatted statement`() = onEdt {
        val document = DocumentImpl("int value=1;\n")
        val sut = TypingSnapshot(document.text, document.modificationStamp, 0..0)
        val formatted = "int value = 1;\n"

        sut.applyTo(document, LineEdits.forTyping(sut.text, formatted, sut.region))

        assertEquals(formatted, document.text)
    }

    @Test
    fun `a document edited while formatting keeps the newer text`() = onEdt {
        val document = DocumentImpl("int value=1;\n")
        val sut = TypingSnapshot(document.text, document.modificationStamp, 0..0)
        val edits = LineEdits.forTyping(sut.text, "int value = 1;\n", sut.region)
        document.insertString(document.textLength, "// newer edit\n")

        sut.applyTo(document, edits)

        assertEquals("int value=1;\n// newer edit\n", document.text)
    }

    @Test
    fun `a document made read only while formatting is left alone`() = onEdt {
        val document = DocumentImpl("int value=1;\n")
        val sut = TypingSnapshot(document.text, document.modificationStamp, 0..0)
        val edits = LineEdits.forTyping(sut.text, "int value = 1;\n", sut.region)
        document.setReadOnly(true)

        sut.applyTo(document, edits)

        assertEquals("int value=1;\n", document.text)
    }
}
