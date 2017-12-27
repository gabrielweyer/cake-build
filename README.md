# Cake build

| CI | Status | Platform | Framework(s)
| --- | --- | --- | --- |
| [Travis CI](#travis-ci) | [![Build Status](https://travis-ci.org/gabrielweyer/cake-build.svg?branch=master)](https://travis-ci.org/gabrielweyer/cake-build) | `Linux` | `nestandard2.0`, `netcoreapp2.0` |
| [AppVeyor](#appveyor) | N/A | `Windows` | `nestandard2.0`, `netcoreapp2.0`, `net461` |

Demonstrates a basic build of a `.NET Core` `NuGet` package using [Cake][cake].

I tried to create a somewhat realistic scenario without writing too much code:

- The solution contains two projects which will be packed as `NuGet` packages.
  - The `SuperLogic` project depends from `Logic` and when packing this project reference will be turned into a `NuGet` package reference (handled out of the box by `dotnet pack`).
  - The `Logic` project references a `NuGet` package from [nuget.org][nuget-org] via a `PackageReference`, `dotnet pack` will turn this into a package reference.
- The projects target both `nestandard2.0` and `net461` so they can be used with the `.NET Framework`.
- The solution contains a test project.
- Use [`SemVer`][semver] to version the `DLLs` and the `NuGet package`
  - **Note**: `SemVer` is implemented via [`GitVersion`][git-version]

## Benefits over a nuspec file

- A single file describing the package and the project instead of two (`*.csproj` and `*.nuspec`)
- References (projects or `Nuget` packages) are resolved automatically. There is no need to tweak a file manually anymore!

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

## Pinning the version of Cake

To pin the version of `Cake`, add the following lines to your `.gitignore` file:

```bash
tools/*
!tools/packages.config
```

Pinning the version of `Cake` guarantees you'll be using the same version of `Cake` on your machine and in the build server.

## Reusing this to bootstrap your project

- Get the latest [`build.ps1`][build-ps1]
- Copy [`build.cake`][build-cake] into the root of your directory
- [Pin](#Pinning-the-version-of-Cake) the version of `Cake`

## CI

Each time a commit is pushed to `master` or `features/*` `Travis CI` and `AppVeyor` will build the changes.

In case of a successful build `AppVeyor` will:

- On `master`
  - [Create][github-release] a `GitHub` **release**
  - Publish the `NuGet` packages to `gabrielweyer` [feed][my-get-gabrielweyer-feed]
- On `features/*`
  - [Create][github-release] a `GitHub` **pre-release**
  - Publish the `NuGet` packages to `gabrielweyer-pre-release` [feed][my-get-gabrielweyer-pre-release-feed]

### Travis CI

Build status is visible [here][travis-ci].

`Travis CI` has a few limitations:

- `Linux` only so you can't build any `net*` `Framework`
  - For this reason I'm not publishing the `NuGet` packages from `Travis CI`
  - `build.sh` (the [Cake bootstrapper][build-sh]) has been modified to support `Cake Core CLR`
  - `build.cake` has been modified
    - Targets `netstandard2.0` / `netcoreapp2.0` only on Travis (search for `TravisCI.IsRunningOnTravisCI`)
    - Custom implementation of `GitVersion` (search for `SemVer`), the built-in helper wouldn't work on `mono`
- Doesn't parse test result files
- [Artefacts][travis-artefacts] have to be uploaded to `S3`

### AppVeyor

Build status is visible [here][app-veyor].

- `Windows` only
- Can target both `.NET Core` and `.NET Framework`
  - For this reason we'll publish the `NuGet` packages using `AppVeyor`
- Can create a `GitHub` release and `tag` the `repository` if required
- You can modify `AppVeyor`'s build number programatically
  - `Cake` integrates with `AppVeyor`: publish test results, upload artifacts, update build number...

[cake]: https://cakebuild.net/
[build-ps1]: https://raw.githubusercontent.com/cake-build/example/master/build.ps1
[nuget-org]: https://www.nuget.org/
[build-cake]: build.cake
[semver]: https://semver.org/
[git-version]: https://gitversion.readthedocs.io/en/latest/
[pack-issues]: https://github.com/NuGet/Home/issues/6285
[project-reference-dll-issue]: https://github.com/NuGet/Home/issues/3891
[private-assets]: https://docs.microsoft.com/en-us/dotnet/core/tools/csproj#includeassets-excludeassets-and-privateassets
[travis-ci]: https://travis-ci.org/gabrielweyer/cake-build
[travis-artefacts]: https://docs.travis-ci.com/user/uploading-artifacts/
[build-sh]: https://raw.githubusercontent.com/cake-build/example/master/build.ps1
[app-veyor]: https://ci.appveyor.com/project/GabrielWeyer/cake-build
[my-get-gabrielweyer-feed]: https://www.myget.org/feed/Packages/gabrielweyer
[my-get-gabrielweyer-pre-release-feed]: https://www.myget.org/feed/Packages/gabrielweyer-pre-release
[github-release]: https://github.com/gabrielweyer/cake-build/releases
