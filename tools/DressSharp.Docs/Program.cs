using System.Reflection;
using DressSharp.Docs;

var website = Path.Combine(ContentWriter.RepositoryRoot, "website");
switch (args)
{
    case []:
        break;
    case ["--website", var path]:
        website = Path.GetFullPath(path);
        break;
    default:
        Console.Error.WriteLine("Usage: DressSharp.Docs [--website <directory>]");
        return 2;
}

var version = typeof(DressSharp.Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? typeof(DressSharp.Program).Assembly.GetName().Version?.ToString()
    ?? "0.0.0";
version = version.Split('+')[0];

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cancellation.Cancel();
    };

var groups = await CatalogReader.ReadAsync(cancellation.Token);
await new ContentWriter(website, version).WriteAsync(groups, cancellation.Token);

var rules = groups.Sum(group => group.Rules.Count());
var outcomes = groups.Sum(group => group.Rules.Sum(rule => rule.Outcomes.Length));
Console.WriteLine($"Wrote {rules} rules with {outcomes} formatted examples to {website}");
return 0;
