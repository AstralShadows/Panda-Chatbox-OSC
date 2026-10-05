#include <windows.h>
#include <shellapi.h>
#include <string>

#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "shlwapi.lib")

static bool FileExists(const std::wstring& path)
{
    DWORD attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES;
}

static std::wstring GetInstalledAppPath()
{
    wchar_t buffer[MAX_PATH] = {};
    DWORD length = GetModuleFileNameW(nullptr, buffer, MAX_PATH);
    if (length == 0 || length >= MAX_PATH)
    {
        return L"";
    }

    std::wstring path = buffer;
    const size_t lastSlash = path.find_last_of(L"\\/");
    if (lastSlash == std::wstring::npos)
    {
        return L"";
    }

    return path.substr(0, lastSlash + 1);
}

static void ShowError(const wchar_t* message)
{
    MessageBoxW(nullptr, message, L"Panda Chatbox Installer", MB_ICONERROR | MB_OK);
}

int wmain(int argc, wchar_t* argv[])
{
    UNREFERENCED_PARAMETER(argc);
    UNREFERENCED_PARAMETER(argv);

    std::wstring appDir = GetInstalledAppPath();
    if (appDir.empty())
    {
        ShowError(L"Could not determine the application directory.");
        return 1;
    }

    std::wstring exePath = appDir + L"PandaChatbox.exe";
    if (!FileExists(exePath))
    {
        ShowError(L"PandaChatbox.exe was not found in the install directory.");
        return 1;
    }

    SHELLEXECUTEINFOW info = {};
    info.cbSize = sizeof(info);
    info.fMask = SEE_MASK_NOCLOSEPROCESS;
    info.lpFile = exePath.c_str();
    info.lpParameters = L"";
    info.lpDirectory = appDir.c_str();
    info.nShow = SW_SHOWNORMAL;

    if ((int)ShellExecuteExW(&info) <= 32)
    {
        ShowError(L"Panda Chatbox could not be launched after installation.");
        return 1;
    }

    return 0;
}
