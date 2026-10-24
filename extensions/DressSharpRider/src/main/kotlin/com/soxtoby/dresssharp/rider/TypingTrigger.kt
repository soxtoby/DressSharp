package com.soxtoby.dresssharp.rider

import com.intellij.openapi.editor.Document
import com.intellij.openapi.editor.event.DocumentEvent
import com.intellij.openapi.editor.event.DocumentListener

/** Tracks the character through Rider's typing assists, independently of later caret movement. */
internal class TypingTrigger(private val document: Document, offset: Int, private val typed: Char, allowOvertype: Boolean = true) : AutoCloseable {
    private val lock = Any()
    private var canOvertype = allowOvertype && document.charsSequence.getOrNull(offset) == typed
    private var captured = canOvertype
    private var position = document.createRangeMarker(offset, if (captured) offset + 1 else offset)
    private var captureAt: Int? = null
    private var anchorAt: Int? = null
    private var invalidated = false
    private var closed = false
    private val listener = object : DocumentListener {
        override fun beforeDocumentChange(event: DocumentEvent) = beforeChange(event)
        override fun documentChanged(event: DocumentEvent) = afterChange()
    }

    init {
        document.addDocumentListener(listener)
    }

    fun region(text: CharSequence): IntRange? = synchronized(lock) {
        val offset = if (!closed && !invalidated && captured && position.isValid) position.startOffset else -1
        if (offset in text.indices && text[offset] == typed) TriggerRegion.of(text, offset + 1, typed) else null
    }

    override fun close() {
        val marker = synchronized(lock) {
            if (closed) null else position.also { closed = true }
        }
        if (marker != null) {
            document.removeDocumentListener(listener)
            marker.dispose()
        }
    }

    private fun beforeChange(event: DocumentEvent) = synchronized(lock) {
        if (!closed && !invalidated && position.isValid) {
            val offset = position.startOffset
            val inserted = if ((!captured || canOvertype) && event.offset == offset) event.newFragment.indexOf(typed) else -1
            if (inserted >= 0) {
                captureAt = event.offset + inserted
            } else if (!captured && event.offset == offset && event.newLength > 0) {
                anchorAt = event.offset + event.newLength
            } else if (captured && event.oldLength > 0 && event.offset <= offset && event.offset + event.oldLength > offset) {
                invalidated = true
            }
        }
    }

    private fun afterChange() = synchronized(lock) {
        val offset = captureAt ?: anchorAt
        if (!closed && !invalidated && offset != null) {
            val previous = position
            captured = captureAt != null
            position = document.createRangeMarker(offset, if (captured) offset + 1 else offset)
            canOvertype = false
            captureAt = null
            anchorAt = null
            previous.dispose()
        }
    }
}
