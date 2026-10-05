using System.Diagnostics;

const string ProjectFile = "PandaChatbox.csproj";
const string RuntimeIdentifier = "win-x64";
const string InstallerScriptFile = "PandaChatbox.iss";
const string InstallerOutputFile = "PandaChatbox-Setup.exe";

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
            if (File.Exists(Path.Combine(directory.FullName, ProjectFile)))
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
    Console.WriteLine("Publishing Panda Chatbox V3.5.1 as a self-contained single-file app...");
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
    var installerScriptPath = Path.Combine(projectDirectory, InstallerScriptFile);
    var installerOutputPath = Path.Combine(projectDirectory, "installer-output", InstallerOutputFile);

    if (!File.Exists(installerScriptPath))
    {
        Console.Error.WriteLine($"Inno Setup script is missing: {installerScriptPath}");
        return 1;
    }

    var compiler = FindInnoSetupCompiler();
    if (compiler is null)
    {
        Console.Error.WriteLine("Inno Setup 6 was not found. Install Inno Setup 6 or set INNO_SETUP_COMPILER to the full path to ISCC.exe, then retry.");
        return 1;
    }

    if (FindVisualCppCompiler() is null)
    {
        Console.Error.WriteLine("A Visual Studio C++ toolchain was not found. Install 'Desktop development with C++' from Visual Studio 2022 Build Tools and retry.");
        return 1;
    }

    if (FindWindowsSdkHeaders() is null)
    {
        Console.Error.WriteLine("The Windows SDK was not found. In Visual Studio Installer, install a Windows 10 or Windows 11 SDK component, then retry.");
        return 1;
    }

    var publishResult = Publish(projectDirectory, projectPath);
    if (publishResult != 0)
    {
        Console.Error.WriteLine("App publish failed; installer was not created.");
        return publishResult;
    }

    var outputDirectory = Path.GetDirectoryName(installerOutputPath)!;
    Directory.CreateDirectory(outputDirectory);
    Console.WriteLine("Compiling the Inno Setup installer...");
    var result = RunProcess(projectDirectory, compiler, installerScriptPath);
    if (result == 0)
    {
        Console.WriteLine($"Installer created: {installerOutputPath}");
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
        if (argument is not null)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    return RunStartedProcess(startInfo);
}

static int RunStartedProcess(ProcessStartInfo startInfo)
{
    try
    {
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            Console.Error.WriteLine($"Could not start: {startInfo.FileName}");
            return 1;
        }

        process.WaitForExit();
        return process.ExitCode;
    }
    catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
    {
        Console.Error.WriteLine($"Could not run {startInfo.FileName}: {exception.Message}");
        return 1;
    }
}

static string? FindVisualCppCompiler()
{
    var vswherePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "Microsoft Visual Studio",
        "Installer",
        "vswhere.exe");

    if (!File.Exists(vswherePath))
    {
        vswherePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Microsoft Visual Studio",
            "Installer",
            "vswhere.exe");
    }

    if (File.Exists(vswherePath))
    {
        var result = RunVswhere(vswherePath);
        if (result is not null)
        {
            var vcVars = Path.Combine(result, "VC", "Auxiliary", "Build", "vcvars64.bat");
            if (File.Exists(vcVars))
            {
                return vcVars;
            }
        }
    }

    var literalCandidates = new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Visual Studio", "2022", "Enterprise", "VC", "Auxiliary", "Build", "vcvars64.bat"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Visual Studio", "2022", "BuildTools", "VC", "Auxiliary", "Build", "vcvars64.bat"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "2022", "Enterprise", "VC", "Auxiliary", "Build", "vcvars64.bat"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "2022", "BuildTools", "VC", "Auxiliary", "Build", "vcvars64.bat")
    };

    return literalCandidates.FirstOrDefault(File.Exists);
}

static string? FindInnoSetupCompiler()
{
    var configuredPath = Environment.GetEnvironmentVariable("INNO_SETUP_COMPILER");
    if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
    {
        return configuredPath;
    }

    var candidates = new List<string>
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Inno Setup 6", "ISCC.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Inno Setup 6", "ISCC.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Inno Setup 6", "ISCC.exe")
    };

    using var uninstallKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
    if (uninstallKey is not null)
    {
        foreach (var subKeyName in uninstallKey.GetSubKeyNames())
        {
            using var appKey = uninstallKey.OpenSubKey(subKeyName);
            if (appKey?.GetValue("DisplayName") is not string displayName
                || !displayName.Contains("Inno Setup", StringComparison.OrdinalIgnoreCase)
                || !displayName.Contains("6", StringComparison.Ordinal))
            {
                continue;
            }

            if (appKey.GetValue("InstallLocation") is string installLocation
                && !string.IsNullOrWhiteSpace(installLocation))
            {
                candidates.Add(Path.Combine(installLocation, "ISCC.exe"));
            }

            if (appKey.GetValue("DisplayIcon") is string displayIcon
                && !string.IsNullOrWhiteSpace(displayIcon))
            {
                var iconPath = displayIcon.Trim().Trim('"');
                if (iconPath.Contains(','))
                {
                    iconPath = iconPath[..iconPath.LastIndexOf(',')].Trim().Trim('"');
                }

                var directory = Path.GetDirectoryName(iconPath);
                if (directory is not null)
                {
                    candidates.Add(Path.Combine(directory, "ISCC.exe"));
                }
            }
        }
    }

    return candidates.FirstOrDefault(File.Exists);
}

static string? FindWindowsSdkHeaders()
{
    var sdkIncludeDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "Windows Kits",
        "10",
        "Include");

    if (!Directory.Exists(sdkIncludeDirectory))
    {
        return null;
    }

    return Directory.GetDirectories(sdkIncludeDirectory)
        .FirstOrDefault(versionDirectory => File.Exists(Path.Combine(versionDirectory, "um", "Windows.h")));
}

static string? RunVswhere(string vswherePath)
{
    var startInfo = new ProcessStartInfo(vswherePath)
    {
        Arguments = "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath",
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    try
    {
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output.Trim() : null;
    }
    catch
    {
        return null;
    }
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
          installer  Publish the app and compile the Inno Setup installer
        """);
}
