package com.soxtoby.dresssharp.rider

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

class TriggerRegionTest {
    private val text = """
        class C
        {
            void M()
            {
                var x =
                    1;
                var y = 2;
            }
        }
    """.trimIndent()

    /** The offset just after the first occurrence of [marker]. */
    private fun after(marker: String): Int = text.indexOf(marker).also { check(it >= 0) } + marker.length

    @Test
    fun `a semicolon settles the statement back to the previous boundary`() {
        assertEquals(4..5, TriggerRegion.of(text, after("1;"), ';'))
        assertEquals(6..6, TriggerRegion.of(text, after("var y = 2;"), ';'))
    }

    @Test
    fun `a closing brace settles the block back to its opening brace`() {
        assertEquals(3..7, TriggerRegion.of(text, after("2;\n    }"), '}'))
        assertEquals(1..8, TriggerRegion.of(text, text.length, '}'))
    }

    @Test
    fun `a brace without a match settles only its own line`() {
        assertEquals(0..0, TriggerRegion.of("}", 1, '}'))
        assertEquals(1..1, TriggerRegion.of("a\n  b }", 7, '}'))
    }

    @Test
    fun `a caret with no brace before it on the line settles only its own line`() {
        assertEquals(2..2, TriggerRegion.of("{\n}\nx", 5, '}'))
    }

    @Test
    fun `the first statement of a file starts at its first character`() {
        assertEquals(0..1, TriggerRegion.of("using\n  System;", 15, ';'))
    }
}
