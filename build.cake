#tool dotnet:?package=GitVersion.Tool&version=5.8.1
#addin nuget:?package=Cake.Incubator&version=7.0.0

#r Newtonsoft.Json

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

var assemblyVersion = "1.0.0";
var packageVersion = "1.0.0";

var artifactsDir = MakeAbsolute(Directory("artifacts"));
var packagesDir = artifactsDir.Combine(Directory("packages"));
var testResultsDir = artifactsDir.Combine(Directory("test-results"));

var solutionPath = "./build.sln";

var testProjects = new List<TestProject>();

Task("Clean")
    .Does(() =>
    {
        CleanDirectory(artifactsDir);

        var settings = new DotNetCleanSettings
        {
            Configuration = configuration
        };

        DotNetClean(solutionPath, settings);

        DeleteFiles("./**/*.trx");
    });

Task("Restore")
    .IsDependentOn("Clean")
    .Does(() =>
    {
        DotNetRestore();
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
        var settings = new DotNetBuildSettings
        {
            Configuration = configuration,
            NoIncremental = true,
            NoRestore = true,
            MSBuildSettings = new DotNetMSBuildSettings()
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
                .ForEach(f => DotNetBuild(f.FullPath, settings));

            settings.Framework = "net6.0";

            GetFiles("./tests/*/*Tests.csproj")
                .ToList()
                .ForEach(f => DotNetBuild(f.FullPath, settings));
        }
        else
        {
            DotNetBuild(solutionPath, settings);
        }
    });

Task("ListTestProjectsAndFrameworkVersions")
    .IsDependentOn("Build")
    .DoesForEach(GetFiles("./tests/*/*Tests.csproj"), (testProject) =>
    {
        var parsedProject = ParseProject(testProject.FullPath, configuration: configuration);

        parsedProject.TargetFrameworkVersions.ToList().ForEach(frameworkVersion =>
        {
            if (IsRunningOnLinuxOrDarwin() && frameworkVersion != "net6.0")
            {
                Information($"Skipping test project '{parsedProject.AssemblyName}' framework '{frameworkVersion}' as we're not running on Windows");
                return;
            }

            var projectToTest = new TestProject
            {
                FullPath = testProject.FullPath,
                AssemblyName = parsedProject.AssemblyName,
                FrameworkVersion = frameworkVersion
            };
            testProjects.Add(projectToTest);
        });
    });

Task("Test")
    .IsDependentOn("ListTestProjectsAndFrameworkVersions")
    .DoesForEach(() => testProjects, (testProject) =>
    {
        var settings = new DotNetTestSettings
        {
            Configuration = configuration,
            NoBuild = true,
            Framework = testProject.FrameworkVersion
        };

        if (IsRunningOnCircleCI())
        {
            var jUnitTestResultsFile = testResultsDir
                .Combine("junit")
                .Combine("{assembly}.{framework}.xml");
            settings.Loggers.Add($"\"junit;LogFilePath={jUnitTestResultsFile}\"");
        }

        if (GitHubActions.IsRunningOnGitHubActions)
        {
            var trxTestResultsFile = testResultsDir
                .Combine("trx")
                .Combine($"{testProject.AssemblyName}.{testProject.FrameworkVersion}.trx");
            settings.Loggers.Add($"\"trx;LogFileName={trxTestResultsFile}\"");
        }

        DotNetTest(testProject.FullPath, settings);
    })
    .DeferOnError();

Task("Pack")
    .IsDependentOn("Test")
    .WithCriteria(() => HasArgument("pack"))
    .Does(() =>
    {
        var settings = new DotNetPackSettings
        {
            Configuration = configuration,
            NoBuild = true,
            NoRestore = true,
            IncludeSymbols = true,
            OutputDirectory = packagesDir,
            MSBuildSettings = new DotNetMSBuildSettings()
                .WithProperty("PackageVersion", packageVersion)
                .WithProperty("Copyright", $"Copyright Contoso {DateTime.Now.Year}")
        };

        if (IsRunningOnLinuxOrDarwin())
        {
            settings.MSBuildSettings.WithProperty("TargetFrameworks", "netstandard2.0");
        }

        GetFiles("./src/*/*.csproj")
            .ToList()
            .ForEach(f => DotNetPack(f.FullPath, settings));
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
/// - No .NET 4.6.x Framework installed, only .NET 6.x
/// </summary>
private bool IsRunningOnLinuxOrDarwin()
{
    return Context.Environment.Platform.IsUnix();
}

private bool IsRunningOnCircleCI()
{
    return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CIRCLECI"));
}

class TestProject
{
    public string FullPath { get; set; }
    public string AssemblyName { get; set; }
    public string FrameworkVersion { get; set; }
}
