package com.soxtoby.dresssharp.rider

import com.intellij.openapi.components.BaseState
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.SimplePersistentStateComponent
import com.intellij.openapi.components.State
import com.intellij.openapi.components.Storage
import com.intellij.openapi.components.service
import com.intellij.openapi.project.Project

/** Settings that belong to the solution, so a team can share them. */
@Service(Service.Level.PROJECT)
@State(name = "DressSharpSettings", storages = [Storage("dressSharp.xml")])
class DressSharpProjectSettings : SimplePersistentStateComponent<DressSharpProjectSettings.State>(State()) {
    class State : BaseState() {
        var formatOnSave by property(true)

        /** Experimental: format the statement or block just finished when `;` or `}` is typed. */
        var formatOnType by property(false)
    }

    companion object {
        fun getInstance(project: Project): DressSharpProjectSettings = project.service()
    }
}

/** Settings that belong to this machine. */
@Service(Service.Level.APP)
@State(name = "DressSharpSettings", storages = [Storage("dressSharp.xml")])
class DressSharpApplicationSettings : SimplePersistentStateComponent<DressSharpApplicationSettings.State>(State()) {
    class State : BaseState() {
        /** The dotnet executable to run, or null to find it on the PATH. */
        var dotnetPath by string()
    }

    companion object {
        fun getInstance(): DressSharpApplicationSettings = service()
    }
}
