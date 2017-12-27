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
        GitVersion gitVersion;

        if (TravisCI.IsRunningOnTravisCI)
        {
            gitVersion = SemVerForTravis();
        }
        else
        {
            gitVersion = GitVersion();
        }

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
            MSBuildSettings = new DotNetCoreMSBuildSettings()
                .SetVersion(assemblyVersion)
                .WithProperty("FileVersion", packageVersion)
                .WithProperty("InformationalVersion", packageVersion)
                .WithProperty("nowarn", "7035"),
            ArgumentCustomization = args => args.Append("--no-restore")
        };

        if (TravisCI.IsRunningOnTravisCI)
        {
            settings.Framework = "netstandard2.0";

            GetFiles("./src/*/*.csproj")
                .ToList()
                .ForEach(f => DotNetCoreBuild(f.FullPath, settings));

            settings.Framework = "netcoreapp2.0";

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
        var settings = new DotNetCoreTestSettings
        {
            Configuration = configuration,
            NoBuild = true,
            Logger = "trx",
            ArgumentCustomization = args => args
                .Append("--results-directory=" + testsResultsDir)
                .Append("--no-restore")
        };

        if (TravisCI.IsRunningOnTravisCI)
        {
            settings.Framework = "netcoreapp2.0";
        }

        GetFiles("./tests/*/*Tests.csproj")
            .ToList()
            .ForEach(f => DotNetCoreTest(f.FullPath, settings));
    });

Task("PublishAppVeyorTestResults")
    .IsDependentOn("Test")
    .WithCriteria(() => AppVeyor.IsRunningOnAppVeyor)
    .Does(() =>
    {
        var testResults = GetFiles($"{testsResultsDir}/*.trx");

        testResults
            .ToList()
            .ForEach(f => AppVeyor.UploadTestResults(f, AppVeyorTestResultsType.MSTest));
    });

Task("Pack")
    .IsDependentOn("PublishAppVeyorTestResults")
    .WithCriteria(() => HasArgument("pack"))
    .Does(() =>
    {
        var settings = new DotNetCorePackSettings
        {
            Configuration = configuration,
            NoBuild = true,
            IncludeSymbols = true,
            OutputDirectory = packagesDir,
            ArgumentCustomization = args => args.Append("--no-restore"),
            MSBuildSettings = new DotNetCoreMSBuildSettings()
                .WithProperty("PackageVersion", packageVersion)
                .WithProperty("Copyright", $"Copyright Contoso {DateTime.Now.Year}")
        };

        if (TravisCI.IsRunningOnTravisCI)
        {
            settings.MSBuildSettings.WithProperty("TargetFrameworks", "netstandard2.0");
        }

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

private GitVersion SemVerForTravis()
{
    IEnumerable<string> redirectedStandardOutput;
    IEnumerable<string> redirectedStandardError;

    try
    {
        var gitVersionBinaryPath = MakeAbsolute((FilePath) "./tools/GitVersion.CommandLine/tools/GitVersion.exe").ToString();

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
    return Newtonsoft.Json.JsonConvert.DeserializeObject<GitVersion>(json);
}