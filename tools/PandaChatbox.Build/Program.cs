using System.Diagnostics;

const string ProjectFile = "PandaChatbox.csproj";
const string InstallerFile = "PandaChatbox.iss";
const string RuntimeIdentifier = "win-x64";

var projectDirectory = FindProjectDirectory();
if (projectDirectory is null)
{
    Console.Error.WriteLine($"Could not find {ProjectFile}. Run this tool from the project or its subdirectories.");
    return 1;
}

var command = args.FirstOrDefault()?.ToLowerInvariant();
if (command is null or "help" or "--help" or "-h")
{
    PrintUsage();
    return command is null ? 1 : 0;
}

var projectPath = Path.Combine(projectDirectory, ProjectFile);
switch (command)
{
    case "build":
        return RunDotnet(projectDirectory, "build", projectPath, "-c", "Debug");
    case "run":
        return RunDotnet(projectDirectory, "run", "--project", projectPath, "-c", "Release");
    case "publish":
        return Publish(projectDirectory, projectPath);
    case "installer":
        return BuildInstaller(projectDirectory, projectPath);
    default:
        Console.Error.WriteLine($"Unknown command: {args[0]}");
        PrintUsage();
        return 1;
}

static string? FindProjectDirectory()
{
    var starts = new[] { Environment.CurrentDirectory, AppContext.BaseDirectory };
    foreach (var start in starts)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, ProjectFile))
                && File.Exists(Path.Combine(directory.FullName, InstallerFile)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }
    }

    return null;
}

static int Publish(string projectDirectory, string projectPath)
{
    Console.WriteLine("Publishing Panda Chatbox V3.4.0 as a self-contained single-file app...");
    return RunDotnet(
        projectDirectory,
        "publish",
        projectPath,
        "-c", "Release",
        "-r", RuntimeIdentifier,
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-o", Path.Combine(projectDirectory, "publish"));
}

static int BuildInstaller(string projectDirectory, string projectPath)
{
    var publishResult = Publish(projectDirectory, projectPath);
    if (publishResult != 0)
    {
        Console.Error.WriteLine("App publish failed; installer was not created.");
        return publishResult;
    }

    var compiler = FindInnoSetupCompiler();
    if (compiler is null)
    {
        Console.Error.WriteLine("Inno Setup 6 was not found. Install it from https://jrsoftware.org/isinfo.php and retry.");
        return 1;
    }

    Console.WriteLine("Creating the Panda Chatbox installer...");
    var result = RunProcess(projectDirectory, compiler, Path.Combine(projectDirectory, InstallerFile));
    if (result == 0)
    {
        Console.WriteLine($"Installer created: {Path.Combine(projectDirectory, "installer-output", "PandaChatbox-Setup.exe")}");
    }

    return result;
}

static int RunDotnet(string workingDirectory, params string[] arguments) =>
    RunProcess(workingDirectory, "dotnet", arguments);

static int RunProcess(string workingDirectory, string executable, params string[] arguments)
{
    var startInfo = new ProcessStartInfo(executable)
    {
        WorkingDirectory = workingDirectory,
        UseShellExecute = false
    };

    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    try
    {
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            Console.Error.WriteLine($"Could not start: {executable}");
            return 1;
        }

        process.WaitForExit();
        return process.ExitCode;
    }
    catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
    {
        Console.Error.WriteLine($"Could not run {executable}: {exception.Message}");
        return 1;
    }
}

static string? FindInnoSetupCompiler()
{
    var candidates = new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Inno Setup 6", "ISCC.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Inno Setup 6", "ISCC.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Inno Setup 6", "ISCC.exe")
    };

    return candidates.FirstOrDefault(File.Exists);
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        Panda Chatbox build tool

        Usage: dotnet run --project tools/PandaChatbox.Build -- <command>

        Commands:
          build      Build the app for development
          run        Run the app
          publish    Publish a self-contained single-file Windows app
          installer  Publish the app and compile the Windows installer
        """);
}
