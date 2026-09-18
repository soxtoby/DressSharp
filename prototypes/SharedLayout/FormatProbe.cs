using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DressSharp.Architecture;
using DressSharp.Execution;
using Microsoft.CodeAnalysis.CSharp;

static class FormatProbe
{
    internal static int Run(Input[] inputs, FormattingConfiguration configuration, string output)
    {
        var formatter = new DocumentFormatter(configuration, new BenchmarkTiming());
        var hashes = new string[inputs.Length];
        var rows = new List<object>();
        for (var round = -1; round < 3; round++)
        {
            var allocated = GC.GetTotalAllocatedBytes(true);
            var cpu = Process.GetCurrentProcess().TotalProcessorTime;
            var watch = Stopwatch.StartNew();
            Parallel.For(0, inputs.Length, new ParallelOptions { MaxDegreeOfParallelism = 11 }, index =>
            {
                var input = inputs[index];
                var root = CSharpSyntaxTree.ParseText(input.Source, input.Options).GetRoot();
                var text = formatter.FormatSyntax(root, input.Source, input.Options);
                hashes[index] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            });
            var row = new { round, milliseconds = watch.Elapsed.TotalMilliseconds,
                cpuMilliseconds = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds,
                allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated };
            rows.Add(row);
            Console.WriteLine(JsonSerializer.Serialize(row));
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new { files = inputs.Select(input => input.Name), hashes, rows }));
        return 0;
    }
}
