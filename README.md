# Cake build

| Package | Release | Pre-release |
| --- | --- | --- |
| `Contoso.Hello.Logic` | [![MyGet][my-get-logic-r-badge]][my-get-logic-r] | [![MyGet][my-get-logic-pre-badge]][my-get-logic-pre] |
| `Contoso.Hello.SuperLogic` | [![MyGet][my-get-super-r-badge]][my-get-super-r] | [![MyGet][my-get-super-pre-badge]][my-get-super-pre] |

| CI | Status | Platform(s) | Framework(s) | Test Framework(s) |
| --- | --- | --- | --- | --- |
| [AppVeyor](#appveyor) | [![Build Status][app-veyor-shield]][app-veyor] | `Windows` | `netstandard2.0`, `net461` | `net6.0`, `net461` |
| [Azure DevOps](#azure-devops) | [![Build Status][azure-devops-shield]][azure-devops] | `Linux` | `netstandard2.0` | `net6.0` |
| [CircleCI](#circleci) | [![Build Status][circle-ci-shield]][circle-ci] | `Docker`: `mcr.microsoft.com/dotnet/sdk:6.0-focal` | `netstandard2.0` | `net6.0` |
| [GitHub](#github) | [![Build Status][github-actions-shield]][github-actions] | `Windows` | `netstandard2.0`, `net461` | `net6.0`, `net461` |

Demonstrates a basic build of a `.NET` `NuGet` package using [Cake][cake].

I tried to create a *somewhat* realistic scenario without writing too much code:

- The solution contains two projects which will be packed as `NuGet` packages.
  - The `SuperLogic` project depends from `Logic` and when packing this project reference will be turned into a `NuGet` package reference (handled out of the box by `dotnet pack`).
  - The `Logic` project references a `NuGet` package from [nuget.org][nuget-org] via a `PackageReference`, `dotnet pack` will turn this into a package reference.
- The projects target both `netstandard2.0` and `net461` so they can be used with the `.NET Framework` (`net461` and above).
- The solution contains a test project.
- Use [`SemVer`][semver] to version the `DLLs` and the `NuGet` packages.
  - **Note**: `SemVer` is implemented via [`GitVersion`][git-version].

I wrote a detailed [blog post][cake-build-post] about this experiment.

## Table of contents

- [Pinning the version of Cake](#pinning-the-version-of-cake)
- [Running locally](#running-locally)
- [Benefits over a nuspec file](#benefits-over-a-nuspec-file)
- [Referencing a project without turning it into a package reference](#referencing-a-project-without-turning-it-into-a-package-reference)
- [CI](#ci)
- [Status checks](#status-checks)

## Pinning the version of Cake

Pinning the version of `Cake` guarantees you'll be using the same version of `Cake` on your machine and in the build server.

This is done by using `Cake` as a `.NET` **local** tool. The version is specified in `.config\dotnet-tools.json`.

## Running locally

### Pre-requisites

- [.NET SDK 6.0.x][dotnet-sdk] and higher

### Initial setup on Windows

```powershell
.\bootstrap.ps1
```

### Initial setup on Linux / OS X

```bash
./bootstrap.sh
```

### Run build script

```bash
dotnet cake build.cake
```

## Benefits over a nuspec file

- A single file describing the package and the project instead of two (`*.csproj` and `*.nuspec`)
- References (projects or `NuGet` packages) are resolved automatically. There is no need to tweak a file manually any more!

## Referencing a project without turning it into a package reference

The `SuperLogic` project depends on the `ExtraLogic` project but we don't want to ship `ExtraLogic` as a package. Instead we want to include `Contoso.Hello.ExtraLogic.dll` in the `SuperLogic` package directly. Currently this is not supported out of the box but the team is [tracking it][pack-issues].

Luckily [this issue][project-reference-dll-issue] provides a workaround. All the modifications will take place in `SuperLogic.csproj`.

- In the `<PropertyGroup>` section add the following line:

```xml
<TargetsForTfmSpecificBuildOutput>$(TargetsForTfmSpecificBuildOutput);IncludeReferencedProjectInPackage</TargetsForTfmSpecificBuildOutput>
```

- Prevent the project to be added as a package reference by making [all assets private][private-assets].

```xml
<ProjectReference Include="..\ExtraLogic\ExtraLogic.csproj">
  <PrivateAssets>all</PrivateAssets>
</ProjectReference>
```

- Finally add the target responsible of copying the `DLL`:

```xml
<Target Name="IncludeReferencedProjectInPackage">
  <ItemGroup>
    <BuildOutputInPackage Include="$(OutputPath)Contoso.Hello.ExtraLogic.dll" />
  </ItemGroup>
</Target>
```

## CI

Each time a commit is pushed to `main` or `features/*`; `AppVeyor`, `Azure DevOps` and `CircleCI` will build the changes.

In case of a successful build `AppVeyor` will:

- On `main`
  - [Create][github-release] a `GitHub` **release**
  - Publish the `NuGet` packages (including symbols) to `gabrielweyer` [feed][my-get-gabrielweyer-feed]
- On `features/*`
  - [Create][github-release] a `GitHub` **pre-release**
  - Publish the `NuGet` packages (including symbols) to `gabrielweyer-pre-release` [feed][my-get-gabrielweyer-pre-release-feed]

When running on a platform that is not Windows, we can't target the `.NET` full Framework, hence the build script is calling `IsRunningOnLinuxOrDarwin` to detect the available capabilities.

### AppVeyor

Build status is visible [here][app-veyor].

- Supports `Linux`, `macOS` and `Windows` hosted agents
- Can create a `GitHub` release and `tag` the `repository` if required
- Supports artifacts and test results
- You can modify `AppVeyor`'s build number programatically
  - `Cake` integrates with `AppVeyor`: publish test results, upload artifacts, update build number...
- Partially supports files exclusion (commits are skipped as soon as they contain one file in the excluded
list)

### Azure DevOps

Build status is visible [here][azure-devops].

- Supports `Linux`, `macOS` and `Windows` hosted agents
- Supports artifacts and test results
- Supports files exclusion

### CircleCI

Build status is visible [here][circle-ci].

- Supports `Docker`, `Linux`, `macOS` and `Windows` hosted agents
- Supports artifacts

`CircleCI` has a few limitations:

- Test results have to be in `JUnit` format, you can use the package [XunitXml.TestLogger][xunit-xml-test-logger] for a `xUnit` logger and then convert the file using the package [dotnet-xunit-to-junit][xunit-to-junit]
- Can't exclude files easily

### GitHub

Build status is visible [here][github-actions].

- Supports `Linux`, `macOS` and `Windows` hosted agents
- Supports artifacts
- Supports files exclusion

`GitHub` has a few limitations:

- A third-party / custom Action is required to display test results
- A third-party / custom Action is required to create a GitHub release

## Status checks

The `main` branch is [`protected`][github-protected-branch]:

- Force push is disabled on `main`
- `main` cannot be deleted
- Non-protected branches (such as `features/*`) cannot be merged into `main` until they satisfy:
  - An `AppVeyor` passing build
  - An `Azure DevOps` passing build
  - A `CircleCI` passing build
  - A `GitHub` passing build

After a branch was configured as `protected`, `GitHub` will suggest available [status checks][github-status-checks].

[cake]: https://cakebuild.net/
[nuget-org]: https://www.nuget.org/
[build-cake]: build.cake
[semver]: https://semver.org/
[git-version]: https://gitversion.net/docs/
[pack-issues]: https://github.com/NuGet/Home/issues/6285
[project-reference-dll-issue]: https://github.com/NuGet/Home/issues/3891
[private-assets]: https://docs.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#controlling-dependency-assets
[app-veyor]: https://ci.appveyor.com/project/GabrielWeyer/cake-build
[app-veyor-shield]: https://ci.appveyor.com/api/projects/status/github/gabrielweyer/cake-build?branch=main&svg=true
[my-get-gabrielweyer-feed]: https://www.myget.org/feed/Packages/gabrielweyer
[my-get-gabrielweyer-pre-release-feed]: https://www.myget.org/feed/Packages/gabrielweyer-pre-release
[github-release]: https://github.com/gabrielweyer/cake-build/releases
[my-get-logic-r-badge]: https://img.shields.io/myget/gabrielweyer/v/Contoso.Hello.Logic.svg?label=MyGet
[my-get-logic-r]: https://www.myget.org/feed/gabrielweyer/package/nuget/Contoso.Hello.Logic
[my-get-logic-pre-badge]: https://img.shields.io/myget/gabrielweyer-pre-release/v/Contoso.Hello.Logic.svg?label=MyGet
[my-get-logic-pre]: https://www.myget.org/feed/gabrielweyer-pre-release/package/nuget/Contoso.Hello.Logic
[my-get-super-r-badge]: https://img.shields.io/myget/gabrielweyer/v/Contoso.Hello.SuperLogic.svg?label=MyGet
[my-get-super-r]: https://www.myget.org/feed/gabrielweyer/package/nuget/Contoso.Hello.SuperLogic
[my-get-super-pre-badge]: https://img.shields.io/myget/gabrielweyer-pre-release/v/Contoso.Hello.SuperLogic.svg?label=MyGet
[my-get-super-pre]: https://www.myget.org/feed/gabrielweyer-pre-release/package/nuget/Contoso.Hello.SuperLogic
[github-protected-branch]: https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/defining-the-mergeability-of-pull-requests/about-protected-branches
[github-status-checks]: https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/defining-the-mergeability-of-pull-requests/about-protected-branches#require-status-checks-before-merging
[circle-ci]: https://app.circleci.com/pipelines/github/gabrielweyer/cake-build
[circle-ci-shield]: https://circleci.com/gh/gabrielweyer/cake-build/tree/main.svg?style=shield
[xunit-to-junit]: https://www.nuget.org/packages/dotnet-xunit-to-junit/
[dotnet-sdk]: https://dotnet.microsoft.com/download/dotnet/6.0
[azure-devops-shield]: https://dev.azure.com/gabrielweyer/cake-build/_apis/build/status/Cake?branchName=main
[azure-devops]: https://dev.azure.com/gabrielweyer/cake-build/_build/latest?definitionId=12&branchName=main
[cake-build-post]: https://gabrielweyer.github.io/2018/04/22/cake-build/
[xunit-xml-test-logger]: https://www.nuget.org/packages/XunitXml.TestLogger/
[github-actions]: https://github.com/gabrielweyer/cake-build/actions/workflows/build.yml
[github-actions-shield]: https://github.com/gabrielweyer/cake-build/actions/workflows/build.yml/badge.svg
