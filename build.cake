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

    Information($"AssemblySemVer: {assemblyVersion}");
    Information($"NuGetVersion: {packageVersion}");
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

    GetFiles("./tests/*/*Tests.csproj")
        .ToList()
        .ForEach(f => DotNetCoreTest(f.FullPath, settings));
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
        ArgumentCustomization = args => args.Append("--no-restore"),
        MSBuildSettings = new DotNetCoreMSBuildSettings()
            .WithProperty("PackageVersion", packageVersion)
            .WithProperty("Copyright", $"Copyright Contoso {DateTime.Now.Year}")
    };

    GetFiles("./src/*/*.csproj")
        .ToList()
        .ForEach(f => DotNetCorePack(f.FullPath, settings));
});

Task("Default")
    .IsDependentOn("Pack");

RunTarget(target);