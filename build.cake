#tool "nuget:?package=GitVersion.CommandLine&version=3.6.5"

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

var assemblyVersion = "1.0.0";
var packageVersion = "1.0.0";

var artefactsDir = MakeAbsolute(Directory("artefacts"));
var testsResultsDir = artefactsDir.Combine(Directory("tests-results"));
var packagesDir = artefactsDir.Combine(Directory("packages"));

var solutionPath = "./build.sln";

Task("Clean")
    .Does(() =>
{
    CleanDirectory(artefactsDir);

    var settings = new DotNetCoreCleanSettings
    {
        Configuration = configuration
    };

    DotNetCoreClean(solutionPath, settings);
});

Task("Restore")
    .IsDependentOn("Clean")
    .Does(() =>
{
    DotNetCoreRestore();
});

Task("SemVer")
    .IsDependentOn("Restore")
    .Does(() =>
{
    var gitVersion = GitVersion();
    assemblyVersion = gitVersion.AssemblySemVer;
    packageVersion = gitVersion.NuGetVersion;
});

Task("Build")
    .IsDependentOn("SemVer")
    .Does(() =>
{
    var settings = new DotNetCoreBuildSettings
    {
        Configuration = configuration,
        NoIncremental = true,
        MSBuildSettings = new DotNetCoreMSBuildSettings().SetVersion(assemblyVersion),
        ArgumentCustomization = args => args.Append("--no-restore")
    };

    DotNetCoreBuild(solutionPath, settings);
});

Task("Tests")
    .IsDependentOn("Build")
    .Does(() =>
{
    var settings = new DotNetCoreTestSettings
    {
        Configuration = configuration,
        NoBuild = true,
        Logger = "trx",
        ArgumentCustomization = args => args.Append("--results-directory=" + testsResultsDir)
    };

    var projectFiles = GetFiles("./tests/*/*Tests.csproj");

    foreach(var file in projectFiles)
    {
        DotNetCoreTest(file.FullPath, settings);
    }
});

Task("Pack")
    .IsDependentOn("Tests")
    .WithCriteria(() => HasArgument("pack"))
    .Does(() =>
{
    var settings = new DotNetCorePackSettings
    {
        Configuration = configuration,
        NoBuild = true,
        OutputDirectory = packagesDir,
        MSBuildSettings = new DotNetCoreMSBuildSettings()
            .WithProperty("PackageVersion", new [] { packageVersion })
            .WithProperty("Copyright", new [] { $"Copyright Contoso {DateTime.Now.Year}" })
    };

    var projectFiles = GetFiles("./src/*/*.csproj");

    foreach(var file in projectFiles)
    {
        DotNetCorePack(file.FullPath, settings);
    }
});

Task("Default")
    .IsDependentOn("Pack");

RunTarget(target);