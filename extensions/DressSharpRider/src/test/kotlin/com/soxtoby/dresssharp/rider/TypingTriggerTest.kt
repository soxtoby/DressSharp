package com.soxtoby.dresssharp.rider

import com.intellij.openapi.editor.impl.DocumentImpl
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test

class TypingTriggerTest : DocumentTestContext() {
    @Test
    fun `a later newline keeps the trigger on the completed statement`() = onEdt {
        val document = DocumentImpl("int first = 1\nint second = 2;")
        TypingTrigger(document, "int first = 1".length, ';').use { sut ->
            document.insertString("int first = 1".length, ";")
            document.insertString("int first = 1;".length, "\n")

            assertEquals(0..0, sut.region(document.charsSequence))
        }
    }

    @Test
    fun `edits before the trigger move its tracked position`() = onEdt {
        val document = DocumentImpl("int first = 1")
        TypingTrigger(document, document.textLength, ';').use { sut ->
            document.insertString(document.textLength, ";")
            document.insertString(0, "int previous = 0;\n")

            assertEquals(1..1, sut.region(document.charsSequence))
        }
    }

    @Test
    fun `a deleted trigger has no formatting region`() = onEdt {
        val document = DocumentImpl("int first = 1")
        TypingTrigger(document, document.textLength, ';').use { sut ->
            document.insertString(document.textLength, ";")
            document.deleteString(document.textLength - 1, document.textLength)

            assertNull(sut.region(document.charsSequence))
        }
    }

    @Test
    fun `a deleted trigger does not attach to an existing matching character`() = onEdt {
        val document = DocumentImpl("int first = 1;")
        val offset = document.textLength - 1
        TypingTrigger(document, offset, ';').use { sut ->
            document.insertString(offset, ";")
            document.deleteString(offset, offset + 1)

            assertNull(sut.region(document.charsSequence))
        }
    }

    @Test
    fun `skipping an existing closing brace formats its block without a document edit`() = onEdt {
        val document = DocumentImpl("void M()\n{\n}")
        TypingTrigger(document, document.textLength - 1, '}').use { sut ->
            assertEquals(1..2, sut.region(document.charsSequence))
        }
    }

    @Test
    fun `deleting an existing closing brace does not attach to its neighbor`() = onEdt {
        val document = DocumentImpl("{}}")
        TypingTrigger(document, 1, '}').use { sut ->
            document.deleteString(1, 2)

            assertNull(sut.region(document.charsSequence))
        }
    }

    @Test
    fun `typing assists before insertion preserve the new trigger position`() = onEdt {
        val document = DocumentImpl("int first = 1")
        TypingTrigger(document, document.textLength, ';').use { sut ->
            document.insertString(document.textLength, "    ")
            document.insertString(document.textLength, ";")

            assertEquals(0..0, sut.region(document.charsSequence))
        }
    }

    @Test
    fun `replacing selected text captures the new trigger`() = onEdt {
        val document = DocumentImpl("int first = 1;selected")
        val offset = "int first = 1".length
        TypingTrigger(document, offset, ';', allowOvertype = false).use { sut ->
            document.deleteString(offset, document.textLength)
            document.insertString(offset, ";")

            assertEquals(0..0, sut.region(document.charsSequence))
        }
    }

}
