#module nuget:?package=Cake.DotNetTool.Module&version=0.1.0

#tool dotnet:?package=GitVersion.Tool&version=4.0.1-beta1-58
#tool dotnet:?package=dotnet-xunit-to-junit&version=1.0.0

#r Newtonsoft.Json

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

var assemblyVersion = "1.0.0";
var packageVersion = "1.0.0";

var artifactsDir = MakeAbsolute(Directory("artifacts"));
var testsResultsDir = artifactsDir.Combine(Directory("tests-results"));
var packagesDir = artifactsDir.Combine(Directory("packages"));

var solutionPath = "./build.sln";

Task("Clean")
    .Does(() =>
    {
        CleanDirectory(artifactsDir);

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
        var gitVersionSettings = new GitVersionSettings
        {
            NoFetch = true,
        };

        var gitVersion = GitVersion(gitVersionSettings);

        assemblyVersion = gitVersion.AssemblySemVer;
        packageVersion = gitVersion.NuGetVersion;

        Information($"AssemblySemVer: {assemblyVersion}");
        Information($"NuGetVersion: {packageVersion}");
    });

Task("SetAppVeyorVersion")
    .IsDependentOn("Semver")
    .WithCriteria(() => AppVeyor.IsRunningOnAppVeyor)
    .Does(() =>
    {
        AppVeyor.UpdateBuildVersion(packageVersion);
    });

Task("Build")
    .IsDependentOn("SetAppVeyorVersion")
    .Does(() =>
    {
        var settings = new DotNetCoreBuildSettings
        {
            Configuration = configuration,
            NoIncremental = true,
            NoRestore = true,
            MSBuildSettings = new DotNetCoreMSBuildSettings()
                .SetVersion(assemblyVersion)
                .WithProperty("FileVersion", packageVersion)
                .WithProperty("InformationalVersion", packageVersion)
                .WithProperty("nowarn", "7035")
        };

        if (IsRunningOnLinuxOrDarwin())
        {
            settings.Framework = "netstandard2.0";

            GetFiles("./src/*/*.csproj")
                .ToList()
                .ForEach(f => DotNetCoreBuild(f.FullPath, settings));

            settings.Framework = "netcoreapp2.2";

            GetFiles("./tests/*/*Tests.csproj")
                .ToList()
                .ForEach(f => DotNetCoreBuild(f.FullPath, settings));
        }
        else
        {
            DotNetCoreBuild(solutionPath, settings);
        }
    });

Task("Test")
    .IsDependentOn("Build")
    .Does(() =>
    {
        var settings = new DotNetCoreToolSettings();

        var argumentsBuilder = new ProcessArgumentBuilder()
            .Append("-configuration")
            .Append(configuration)
            .Append("-nobuild");

        if (IsRunningOnLinuxOrDarwin())
        {
            argumentsBuilder
                .Append("-framework")
                .Append("netcoreapp2.2");
        }

        var projectFiles = GetFiles("./tests/*/*Tests.csproj");

        foreach (var projectFile in projectFiles)
        {
            var testResultsFile = testsResultsDir.Combine($"{projectFile.GetFilenameWithoutExtension()}.xml");
            var arguments = $"{argumentsBuilder.Render()} -xml \"{testResultsFile}\"";

            DotNetCoreTool(projectFile, "xunit", arguments, settings);
        }
    })
    .Does(() =>
    {
        if (IsRunningOnCircleCI())
        {
            TransformCircleCITestResults();
        }
    })
    .DeferOnError();

Task("Pack")
    .IsDependentOn("Test")
    .WithCriteria(() => HasArgument("pack"))
    .Does(() =>
    {
        var settings = new DotNetCorePackSettings
        {
            Configuration = configuration,
            NoBuild = true,
            NoRestore = true,
            IncludeSymbols = true,
            OutputDirectory = packagesDir,
            MSBuildSettings = new DotNetCoreMSBuildSettings()
                .WithProperty("PackageVersion", packageVersion)
                .WithProperty("Copyright", $"Copyright Contoso {DateTime.Now.Year}")
        };

        if (IsRunningOnLinuxOrDarwin())
        {
            settings.MSBuildSettings.WithProperty("TargetFrameworks", "netstandard2.0");
        }

        FixProps();

        GetFiles("./src/*/*.csproj")
            .ToList()
            .ForEach(f => DotNetCorePack(f.FullPath, settings));
    });

Task("PublishAppVeyorArtifacts")
    .IsDependentOn("Pack")
    .WithCriteria(() => HasArgument("pack") && AppVeyor.IsRunningOnAppVeyor)
    .Does(() =>
    {
        CopyFiles($"{packagesDir}/*.nupkg", MakeAbsolute(Directory("./")), false);

        GetFiles($"./*.nupkg")
            .ToList()
            .ForEach(f => AppVeyor.UploadArtifact(f, new AppVeyorUploadArtifactsSettings { DeploymentName = "packages" }));
    });

Task("Default")
    .IsDependentOn("PublishAppVeyorArtifacts");

RunTarget(target);

/// <summary>
/// - No .NET Framework installed, only .NET Core
/// </summary>
private bool IsRunningOnLinuxOrDarwin()
{
    return TravisCI.IsRunningOnTravisCI || IsRunningOnCircleCI();
}

private bool IsRunningOnCircleCI()
{
    return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CIRCLECI"));
}

private void TransformCircleCITestResults()
{
    // CircleCI infer the name of the testing framework from the containing folder
    var testResultsCircleCIDir = artifactsDir.Combine("junit/xUnit");
    EnsureDirectoryExists(testResultsCircleCIDir);

    var testResultsFiles = GetFiles($"{testsResultsDir}/*.xml");

    foreach (var testResultsFile in testResultsFiles)
    {
        var inputFilePath = testResultsFile;
        var outputFilePath = testResultsCircleCIDir.CombineWithFilePath(testResultsFile.GetFilename());

        var arguments = new ProcessArgumentBuilder()
            .AppendQuoted(inputFilePath.ToString())
            .AppendQuoted(outputFilePath.ToString())
            .Render();

        DotNetCoreTool($"xunit-to-junit {arguments}");
    }
}

private void FixProps()
{
    /* Workaround this issue: https://github.com/NuGet/Home/issues/4337
       `pack` does not respect the `Version` and ends up generating invalid
       `NuGet` packages when same-solution project dependencies
    */

    var restoreSettings = new DotNetCoreRestoreSettings
    {
        MSBuildSettings = new DotNetCoreMSBuildSettings()
            .WithProperty("Version", packageVersion)
            .WithProperty("Configuration", configuration)
    };

    DotNetCoreRestore(restoreSettings);
}