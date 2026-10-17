import org.jetbrains.intellij.platform.gradle.TestFrameworkType
import org.xml.sax.InputSource
import java.io.StringReader
import javax.xml.parsers.DocumentBuilderFactory

plugins {
    id("java")
    id("org.jetbrains.kotlin.jvm") version "2.4.21"
    id("org.jetbrains.intellij.platform") version "2.19.0"
}

group = providers.gradleProperty("pluginGroup").get()
val formatterProject = providers.fileContents(layout.projectDirectory.file("../../DressSharp/DressSharp.csproj"))
version = formatterProject.asText.map { source ->
    DocumentBuilderFactory.newInstance().newDocumentBuilder()
        .parse(InputSource(StringReader(source)))
        .getElementsByTagName("Version")
        .item(0).textContent
}.get()

repositories {
    mavenCentral()
    intellijPlatform {
        defaultRepositories()
    }
}

dependencies {
    intellijPlatform {
        val riderPath = providers.gradleProperty("riderPath")
            .orElse(providers.environmentVariable("DRESSSHARP_RIDER_PATH"))
        if (riderPath.isPresent) {
            local(riderPath)
        } else {
            rider(providers.gradleProperty("platformVersion"))
        }
        testFramework(TestFrameworkType.JUnit5)
    }

    testImplementation("org.junit.jupiter:junit-jupiter:5.13.4")
    // The platform test runner still loads JUnit 4 rules, even for JUnit 5 tests.
    testImplementation("junit:junit:4.13.2")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
}

intellijPlatform {
    // Building searchable options launches the IDE; the settings page is small enough to do without.
    buildSearchableOptions = false

    pluginConfiguration {
        id = "com.soxtoby.dresssharp"
        name = "DressSharp"
        version = project.version.toString()
        description = """
            Formats C# files with <a href="https://github.com/soxtoby/DressSharp">DressSharp</a>, the unopinionated
            formatter that applies only the rules you set in EditorConfig.
            <p>
            A file is formatted when the repository installs DressSharp as a local .NET tool, in a
            <code>.config/dotnet-tools.json</code> above the file, and an <code>.editorconfig</code> applies to it.
            Other files are left alone, so the plugin is safe to leave enabled across every solution.
            </p>
            <ul>
            <li><b>Format on save</b>, switched on and off from Settings | Tools | Actions on Save.</li>
            <li><b>Code | Format with DressSharp</b> for the current file or the files selected in the Solution Explorer.</li>
            <li><b>Format as you type</b>, experimental and off by default: finishing a statement with <code>;</code> or a
            block with <code>}</code> formats the lines just completed.</li>
            </ul>
        """.trimIndent()
        changeNotes = """
            See the <a href="https://github.com/soxtoby/DressSharp/releases/tag/v${project.version}">DressSharp ${project.version} release notes</a>.
        """.trimIndent()
        ideaVersion {
            sinceBuild = "252"
            untilBuild = provider { null }
        }
        vendor {
            name = "Simon Oxtoby"
            url = "https://github.com/soxtoby/DressSharp"
        }
    }

    pluginVerification {
        ides {
            val riderPath = providers.gradleProperty("riderPath")
                .orElse(providers.environmentVariable("DRESSSHARP_RIDER_PATH"))
            if (riderPath.isPresent) {
                local(riderPath)
            } else {
                recommended()
            }
        }
    }
}

tasks {
    test {
        useJUnitPlatform()
    }
}
