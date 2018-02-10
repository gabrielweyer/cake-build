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

        if (IsRunningOnLinuxOrDarwin())
        {
            gitVersion = SemVerForMono();
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
        var settings = new DotNetCoreToolSettings();

        var argumentsBuilder = new ProcessArgumentBuilder()
            .Append("-configuration")
            .Append(configuration)
            .Append("-nobuild");

        if (IsRunningOnLinuxOrDarwin())
        {
            argumentsBuilder
                .Append("-framework")
                .Append("netcoreapp2.0");
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
/// - Running GitVersion.exe (and other exes) via Mono
/// </summary>
private bool IsRunningOnLinuxOrDarwin()
{
    return TravisCI.IsRunningOnTravisCI || IsRunningOnCircleCI();
}

private bool IsRunningOnCircleCI()
{
    return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CIRCLECI"));
}

private GitVersion SemVerForMono()
{
    IEnumerable<string> redirectedStandardOutput;
    IEnumerable<string> redirectedStandardError;

    try
    {
        var gitVersionBinaryPath = MakeAbsolute((FilePath) "./tools/GitVersion.CommandLine/tools/GitVersion.exe").ToString();

        var arguments =  new ProcessArgumentBuilder()
            .AppendQuoted(gitVersionBinaryPath)
            .Append("-nofetch");

        var exitCode = StartProcess(
            "mono",
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
            Error($"GitVersion: exit code: {exitCode} - {error}");
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

private void TransformXml(FilePath inputFilePath, FilePath outputFilePath)
{
    IEnumerable<string> redirectedStandardOutput;
    IEnumerable<string> redirectedStandardError;

    try
    {
        var xUnitToJUnitBinaryPath = MakeAbsolute((FilePath) "./tools/xUnitToJUnit.CommandLine/tools/xunit-to-junit.dll").ToString();

        var arguments =  new ProcessArgumentBuilder()
            .AppendQuoted(xUnitToJUnitBinaryPath)
            .AppendQuoted(inputFilePath.FullPath)
            .AppendQuoted(outputFilePath.FullPath);

        var exitCode = StartProcess(
            "/usr/bin/dotnet",
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
            Error($"xunit-to-junit: exit code: {exitCode} - {error}");
            throw new InvalidOperationException();
        }

        var standardOutput = string.Join(Environment.NewLine, redirectedStandardOutput.ToList());
        Information(standardOutput);
    }
    catch (System.Exception ex)
    {
        Error($"Exception {ex.GetType()} - {ex.Message} - {ex.StackTrace} - Has inner exception {ex.InnerException != null}");
        throw;
    }
}

private void TransformCircleCITestResults()
{
    // CircleCi infer the name of the testing framework from the containing folder
    var testResultsCircleCIDir = artefactsDir.Combine("junit/xUnit");
    var testResultsFiles = GetFiles($"{testsResultsDir}/*.xml");

    EnsureDirectoryExists(testResultsCircleCIDir);

    foreach (var testResultsFile in testResultsFiles)
    {
        var inputFilePath = testResultsFile;
        var outputFilePath = testResultsCircleCIDir.CombineWithFilePath(testResultsFile.GetFilename());

        TransformXml(inputFilePath, outputFilePath);
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