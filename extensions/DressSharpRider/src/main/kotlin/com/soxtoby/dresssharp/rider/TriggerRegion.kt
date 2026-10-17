package com.soxtoby.dresssharp.rider

/**
 * The lines whose layout a typed `;` or `}` has just settled, found by looking at the characters rather
 * than the syntax: after `;`, the statement back to the previous `;`, `{`, or `}`; after `}`, the block
 * back to its matching `{`. Braces in strings and comments are counted like any other, which is close
 * enough while the user is typing ordinary code.
 */
object TriggerRegion {
    /** The lines to format when [typed] was just typed and the caret rests at [caretOffset] of [text]. */
    fun of(text: CharSequence, caretOffset: Int, typed: Char): IntRange {
        val caret = caretOffset.coerceIn(0, text.length)
        val caretLine = lineOf(text, caret)
        val start = when (typed) {
            '}' -> openingBrace(text, caret) ?: return caretLine..caretLine
            else -> statementStart(text, caret)
        }
        return lineOf(text, start)..caretLine
    }

    /** The offset of the `{` matched by the `}` just before [caret] on its line, if there is one. */
    private fun openingBrace(text: CharSequence, caret: Int): Int? {
        var close = caret - 1
        while (close >= 0 && text[close] != '}' && text[close] != '\n') close--
        if (close < 0 || text[close] != '}') return null
        var depth = 0
        for (index in close downTo 0) {
            when (text[index]) {
                '}' -> depth++
                '{' -> if (--depth == 0) return index
            }
        }
        return null
    }

    /** The first non-blank character after the previous statement or block boundary before [caret]. */
    private fun statementStart(text: CharSequence, caret: Int): Int {
        var boundary = caret - 2
        while (boundary >= 0 && text[boundary] != ';' && text[boundary] != '{' && text[boundary] != '}') boundary--
        var start = boundary + 1
        while (start < caret && text[start].isWhitespace()) start++
        return start
    }

    private fun lineOf(text: CharSequence, offset: Int): Int {
        var line = 0
        for (index in 0 until offset) if (text[index] == '\n') line++
        return line
    }
}
