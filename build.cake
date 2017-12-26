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
    SemVer();
});

Task("Build")
    .IsDependentOn("SemVer")
    .Does(() =>
{
    var settings = new DotNetCoreBuildSettings
    {
        Configuration = configuration,
        NoIncremental = true,
        MSBuildSettings = new DotNetCoreMSBuildSettings()
            .SetVersion(assemblyVersion)
            .WithProperty("FileVersion", packageVersion)
            .WithProperty("InformationalVersion", packageVersion)
            .WithProperty("nowarn", "7035"),
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

private void SemVer()
{
    IEnumerable<string> redirectedStandardOutput;
    IEnumerable<string> redirectedStandardError;

    try
    {
        var gitVersionBinaryPath = MakeAbsolute((FilePath) "./tools/GitVersion.CommandLine/tools/GitVersion.exe").ToString();

        Information($"GitVersion path: {gitVersionBinaryPath}");

        var binary = gitVersionBinaryPath;
        var arguments =  new ProcessArgumentBuilder()
                    .Append("-nofetch");

        if (TravisCI.IsRunningOnTravisCI)
        {
            binary = "mono";
            arguments.PrependQuoted(gitVersionBinaryPath);
        }

        var exitCode = StartProcess(
            binary,
            new ProcessSettings
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Arguments = arguments
            },
            out redirectedStandardOutput,
            out redirectedStandardError);

        if (exitCode != 0)
        {
            var error = string.Join(Environment.NewLine, redirectedStandardError.ToList());
            Error($"Semver: {error}");
            throw new InvalidOperationException();
        }
    }
    catch (System.Exception ex)
    {
        Error($"Exception {ex.GetType()} - {ex.Message} - {ex.StackTrace} - Has inner exception {ex.InnerException != null}");
        throw;
    }

    var json = string.Join(Environment.NewLine, redirectedStandardOutput.ToList());
    Information($"Json returned by GitVersion: {json}");

    var gitVersion = Newtonsoft.Json.JsonConvert.DeserializeObject<GitVersion>(json);

    assemblyVersion = gitVersion.AssemblySemVer;
    packageVersion = gitVersion.NuGetVersion;

    Information($"AssemblySemVer: {assemblyVersion}");
    Information($"NuGetVersion: {packageVersion}");
}