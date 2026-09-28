# DressSharp

DressSharp is an unopinionated C# formatter. It formats your code to the rules you choose in EditorConfig, and nothing else: every rule is yours to set, and any rule you leave unset leaves that part of your code alone. With no preferences set, it leaves every file as it found it.

**[Read the documentation](https://soxtoby.github.io/DressSharp/)**

## Install

DressSharp is a .NET tool and needs the .NET 10 SDK. Install it into your repository so everyone on the team, and your CI, runs the same version:

```sh
dotnet new tool-manifest
dotnet tool install DressSharp
```

Skip the first command if your repository already has a `.config/dotnet-tools.json`. Commit that file. Anyone who clones the repository then runs `dotnet tool restore` once to get the tool.

To use DressSharp everywhere on your machine instead, install it globally:

```sh
dotnet tool install --global DressSharp
```

## Get started

Add every rule DressSharp supports to `.editorconfig`, each set to its default:

```sh
dotnet dress init
```

Try the rules on against your own code, with a live preview in the browser:

```sh
dotnet dress interactive
```

See which files don't match your rules, without changing anything:

```sh
dotnet dress check
```

Then format every C# file in the current directory and below:

```sh
dotnet dress
```

## Learn more

- [Getting started](https://soxtoby.github.io/DressSharp/getting-started/): install, choose your rules, and format for the first time.
- [Choosing your rules](https://soxtoby.github.io/DressSharp/choosing-rules/): find the settings that match how your team writes C#.
- [Formatting automatically](https://soxtoby.github.io/DressSharp/formatting-automatically/): format on every commit, check in CI, and tidy up after coding agents.
- [Rules](https://soxtoby.github.io/DressSharp/rules/): every setting, with examples of what each value does.
- [Command line](https://soxtoby.github.io/DressSharp/cli/): every command and option, and their exit codes.
- [Compatibility and versioning](https://soxtoby.github.io/DressSharp/compatibility/): supported platforms and C# versions, and how to keep formatting stable.
