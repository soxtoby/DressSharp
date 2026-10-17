package com.soxtoby.dresssharp.rider

import com.intellij.codeInsight.editorActions.TypedHandlerDelegate
import com.intellij.openapi.editor.Editor
import com.intellij.openapi.project.Project
import com.intellij.psi.PsiFile

/**
 * Notices a `;` or `}` being typed and asks for the finished statement or block to be formatted.
 *
 * Rider's own typed handler sends C# keystrokes to its backend and can end the chain of delegates
 * there, so this one is registered ahead of it, in the phase before anything is changed. The document
 * is read later, once the keystroke and Rider's typing assists have been applied.
 */
class DressSharpTypedHandler : TypedHandlerDelegate() {
    override fun beforeSelectionRemoved(c: Char, project: Project, editor: Editor, file: PsiFile): Result {
        if ((c == ';' || c == '}') && DressSharpProjectSettings.getInstance(project).state.formatOnType) {
            DressSharpTypingFormatter.getInstance(project).formatAfterTyping(editor, c)
        }
        return Result.CONTINUE
    }
}
