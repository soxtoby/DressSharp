package com.soxtoby.dresssharp.rider

import com.intellij.core.CoreApplicationEnvironment
import com.intellij.openapi.Disposable
import com.intellij.openapi.application.WriteAction
import com.intellij.openapi.command.CommandProcessor
import com.intellij.openapi.editor.impl.DocumentWriteAccessGuard
import com.intellij.openapi.util.Disposer
import org.junit.jupiter.api.AfterEach
import org.junit.jupiter.api.BeforeEach
import javax.swing.SwingUtilities

abstract class DocumentTestContext {
    private lateinit var disposable: Disposable

    @BeforeEach
    fun setup() {
        disposable = Disposer.newDisposable()
        CoreApplicationEnvironment(disposable)
        CoreApplicationEnvironment.registerApplicationExtensionPoint(DocumentWriteAccessGuard.EP_NAME, DocumentWriteAccessGuard::class.java)
    }

    @AfterEach
    fun cleanup() = Disposer.dispose(disposable)

    protected fun onEdt(action: () -> Unit) {
        SwingUtilities.invokeAndWait {
            WriteAction.run<RuntimeException> { CommandProcessor.getInstance().runUndoTransparentAction(action) }
        }
    }
}
