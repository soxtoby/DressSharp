using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using DressSharp.Execution;
using DressSharp.TestSupport;
using EasyAssertions;
using Xunit;

namespace DressSharp.IntegrationTests;

[Collection("Formatting server")]
public sealed class FormattingServerTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), "DressSharp.ServerTests", Guid.NewGuid().ToString("N"));

    string Project => Path.Combine(_directory, "App.csproj");
    string EditorConfig => Path.Combine(_directory, ".editorconfig");
    string Source => Path.Combine(_directory, "App.cs");

    public FormattingServerTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Project, ProjectText("FIRST"));
        File.WriteAllText(EditorConfig, Preferences("true"));
        File.WriteAllText(Source, "class Saved { }\n");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Server_formats_multiple_unsaved_documents_without_writing_the_file()
    {
        await using var sut = await Session.Start(_directory);

        var first = await sut.Format("class C { void M(int a,int b) { } }\n");
        var second = await sut.Format("class C { string M(int a,int b) => \"雪\\n\\\"\"; }\n");

        first.GetProperty("exitCode").GetInt32().ShouldBe(0);
        first.GetProperty("stdout").GetString().ShouldBe("class C { void M(int a, int b) { } }\n");
        second.GetProperty("exitCode").GetInt32().ShouldBe(0);
        second.GetProperty("stdout").GetString().ShouldBe("class C { string M(int a, int b) => \"雪\\n\\\"\"; }\n");
        File.ReadAllText(Source).ShouldBe("class Saved { }\n");
    }

    [Fact]
    public async Task Server_reads_changed_preferences_on_the_next_request()
    {
        await using var sut = await Session.Start(_directory);
        const string source = "class C { void M(int a,int b) { } }\n";

        var first = await sut.Format(source);
        File.WriteAllText(EditorConfig, Preferences("false"));
        var second = await sut.Format(source);

        first.GetProperty("stdout").GetString().ShouldBe("class C { void M(int a, int b) { } }\n");
        second.GetProperty("stdout").GetString().ShouldBe(source);
    }

    [Fact]
    public async Task Server_reads_changed_project_parse_context_on_the_next_request()
    {
        await using var sut = await Session.Start(_directory);
        const string source = "#if FIRST\nclass A { void M(int a,int b) { } }\n#else\nclass B { void M(int a,int b) { } }\n#endif\n";

        var first = await sut.Format(source);
        File.WriteAllText(Project, ProjectText("SECOND"));
        var second = await sut.Format(source);

        first.GetProperty("stdout").GetString().ShouldBe(source.Replace("class A { void M(int a,int b)", "class A { void M(int a, int b)"));
        second.GetProperty("stdout").GetString().ShouldBe(source.Replace("class B { void M(int a,int b)", "class B { void M(int a, int b)"));
    }

    [Fact]
    public async Task Server_formats_saved_batches_and_unsaved_text_in_the_same_process()
    {
        const string source = "class C { string M(int a,int b) => \"雪\"; }\n";
        const string formatted = "class C { string M(int a, int b) => \"雪\"; }\r\n";
        File.WriteAllText(EditorConfig, Preferences("true").Replace("end_of_line = lf", "end_of_line = crlf") + "charset = utf-8-bom\n");
        File.WriteAllText(Source, source);
        File.WriteAllText(Path.Combine(_directory, "Other.cs"), source);
        File.WriteAllText(Path.Combine(_directory, "Generated.g.cs"), source);
        File.WriteAllText(Path.Combine(_directory, "Untouched.cs"), source);
        await using var sut = await Session.Start(_directory);

        var before = await sut.Format(source);
        File.ReadAllText(Source).ShouldBe(source);
        var saved = await sut.FormatFiles("App.cs", "Other.cs", "Generated.g.cs");
        var after = await sut.Format(source);

        before.GetProperty("stdout").GetString().ShouldBe(formatted);
        saved.GetProperty("exitCode").GetInt32().ShouldBe(0, saved.GetProperty("stderr").GetString());
        saved.GetProperty("stdout").GetString()!.Contains("Formatted 2 of 2 files").ShouldBe(true);
        var expected = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(formatted)).ToArray();
        ExactAssert.Bytes(expected, File.ReadAllBytes(Source));
        ExactAssert.Bytes(expected, File.ReadAllBytes(Path.Combine(_directory, "Other.cs")));
        File.ReadAllText(Path.Combine(_directory, "Generated.g.cs")).ShouldBe(source);
        File.ReadAllText(Path.Combine(_directory, "Untouched.cs")).ShouldBe(source);
        after.GetProperty("stdout").GetString().ShouldBe(formatted);
    }

    [Fact]
    public async Task Server_reads_changed_preferences_for_saved_files_on_the_next_request()
    {
        const string source = "class C { void M(int a,int b) { } }\n";
        File.WriteAllText(Source, source);
        await using var sut = await Session.Start(_directory);

        var first = await sut.FormatFiles("App.cs");
        File.ReadAllText(Source).ShouldBe("class C { void M(int a, int b) { } }\n");
        File.WriteAllText(EditorConfig, Preferences("false"));
        var second = await sut.FormatFiles("App.cs");

        first.GetProperty("exitCode").GetInt32().ShouldBe(0);
        second.GetProperty("exitCode").GetInt32().ShouldBe(0);
        File.ReadAllText(Source).ShouldBe(source);
    }

    [Theory]
    [InlineData("{bad json}")]
    [InlineData("{}")]
    [InlineData("{\"includes\":[]}")]
    [InlineData("{\"includes\":[\"\"]}")]
    [InlineData("{\"includes\":[null]}")]
    [InlineData("{\"includes\":[\"App.cs\"],\"path\":\"App.cs\",\"text\":\"\"}")]
    public async Task Server_reports_a_bad_request_without_writing_files_and_accepts_the_next_one(string request)
    {
        await using var sut = await Session.Start(_directory);

        var invalid = await sut.Send(request);
        var valid = await sut.Format("class C { void M(int a,int b) { } }\n");

        invalid.GetProperty("exitCode").GetInt32().ShouldBe(2);
        string.IsNullOrEmpty(invalid.GetProperty("stderr").GetString()).ShouldBe(false);
        valid.GetProperty("exitCode").GetInt32().ShouldBe(0, valid.GetProperty("stderr").GetString());
        valid.GetProperty("stdout").GetString().ShouldBe("class C { void M(int a, int b) { } }\n");
        File.ReadAllText(Source).ShouldBe("class Saved { }\n");
    }

    static string ProjectText(string symbol) => $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><DefineConstants>{symbol}</DefineConstants></PropertyGroup></Project>";
    static string Preferences(string afterComma) => $"root = true\n[*.cs]\ncsharp_space_after_comma = {afterComma}\nend_of_line = lf\ninsert_final_newline = true\n";

    sealed class Session : IAsyncDisposable
    {
        readonly Channel<string?> _requests = Channel.CreateUnbounded<string?>();
        readonly Channel<string> _responses = Channel.CreateUnbounded<string>();
        readonly Task _server;

        Session(string directory) => _server = FormattingServer.Run(directory, new RequestReader(_requests.Reader), new ResponseWriter(_responses.Writer));

        public static async Task<Session> Start(string directory)
        {
            var session = new Session(directory);
            var ready = await session.Read();
            ready.GetProperty("protocol").GetInt32().ShouldBe(2);
            return session;
        }

        public Task<JsonElement> Format(string text) => Send(new JsonObject { ["path"] = "App.cs", ["text"] = text }.ToJsonString());

        public Task<JsonElement> FormatFiles(params string[] includes) => Send(
            new JsonObject { ["includes"] = JsonSerializer.SerializeToNode(includes) }.ToJsonString());

        public async Task<JsonElement> Send(string line)
        {
            await _requests.Writer.WriteAsync(line);
            return await Read();
        }

        async Task<JsonElement> Read()
        {
            var line = await _responses.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }

        public async ValueTask DisposeAsync()
        {
            await _requests.Writer.WriteAsync(null);
            await _server;
        }
    }

    sealed class RequestReader(ChannelReader<string?> reader) : TextReader
    {
        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => reader.ReadAsync(cancellationToken);
    }

    sealed class ResponseWriter(ChannelWriter<string> writer) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override Task WriteLineAsync(string? value) => writer.WriteAsync(value!).AsTask();
    }
}

// MSBuild changes process-wide state; production requests run in their own process.
[CollectionDefinition("Formatting server", DisableParallelization = true)]
public sealed class FormattingServerCollection;
