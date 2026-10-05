#include "PandaChatbox.Native.h"

#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <dxgi1_4.h>
#include <wrl/client.h>

#include <algorithm>
#include <cstdint>
#include <cwctype>
#include <cstring>
#include <iterator>
#include <memory>
#include <new>
#include <stdexcept>
#include <string>
#include <unordered_map>
#include <vector>

#pragma comment(lib, "Advapi32.lib")
#pragma comment(lib, "Dxgi.lib")
#pragma comment(lib, "User32.lib")
#pragma comment(lib, "Ws2_32.lib")

using Microsoft::WRL::ComPtr;

namespace
{
    constexpr double BytesPerGb = 1024.0 * 1024.0 * 1024.0;
    constexpr wchar_t DisplayClassKey[] = L"SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}";

    void CopyText(const std::wstring& value, wchar_t* output, int capacity)
    {
        if (output == nullptr || capacity <= 0)
            return;

        const size_t count = std::min(value.size(), static_cast<size_t>(capacity - 1));
        std::copy_n(value.data(), count, output);
        output[count] = L'\0';
    }

    int ReturnText(const std::wstring& value, wchar_t* output, int capacity)
    {
        CopyText(value, output, capacity);
        return static_cast<int>(value.size());
    }

    std::wstring ReadRegistryString(HKEY root, const wchar_t* keyPath, const wchar_t* valueName)
    {
        DWORD size = 0;
        if (RegGetValueW(root, keyPath, valueName, RRF_RT_REG_SZ, nullptr, nullptr, &size) != ERROR_SUCCESS || size < sizeof(wchar_t))
            return {};

        std::wstring value(size / sizeof(wchar_t), L'\0');
        if (RegGetValueW(root, keyPath, valueName, RRF_RT_REG_SZ, nullptr, value.data(), &size) != ERROR_SUCCESS)
            return {};
        while (!value.empty() && value.back() == L'\0')
            value.pop_back();
        return value;
    }

    uint64_t ReadDedicatedMemory(HKEY adapter)
    {
        for (const wchar_t* valueName : { L"HardwareInformation.qwMemorySize", L"HardwareInformation.MemorySize" })
        {
            DWORD type = 0;
            DWORD size = 0;
            if (RegQueryValueExW(adapter, valueName, nullptr, &type, nullptr, &size) != ERROR_SUCCESS)
                continue;

            if (type == REG_QWORD && size >= sizeof(uint64_t))
            {
                uint64_t value = 0;
                if (RegQueryValueExW(adapter, valueName, nullptr, &type, reinterpret_cast<BYTE*>(&value), &size) == ERROR_SUCCESS)
                    return value;
            }
            else if (type == REG_DWORD && size >= sizeof(uint32_t))
            {
                uint32_t value = 0;
                if (RegQueryValueExW(adapter, valueName, nullptr, &type, reinterpret_cast<BYTE*>(&value), &size) == ERROR_SUCCESS)
                    return value;
            }
            else if (type == REG_BINARY && size >= sizeof(uint64_t))
            {
                uint64_t value = 0;
                if (RegQueryValueExW(adapter, valueName, nullptr, &type, reinterpret_cast<BYTE*>(&value), &size) == ERROR_SUCCESS)
                    return value;
            }
            else if (type == REG_BINARY && size >= sizeof(uint32_t))
            {
                uint32_t value = 0;
                if (RegQueryValueExW(adapter, valueName, nullptr, &type, reinterpret_cast<BYTE*>(&value), &size) == ERROR_SUCCESS)
                    return value;
            }
        }
        return 0;
    }

    std::wstring ToLower(std::wstring value)
    {
        std::transform(value.begin(), value.end(), value.begin(), [](wchar_t c) { return static_cast<wchar_t>(towlower(c)); });
        return value;
    }

    std::string ToUtf8(const std::wstring& value)
    {
        if (value.empty())
            return {};
        const int count = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
        if (count <= 0)
            return {};
        std::string result(static_cast<size_t>(count), '\0');
        if (WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), result.data(), count, nullptr, nullptr) != count)
            return {};
        return result;
    }

    void AddOscString(std::vector<char>& packet, const std::string& text)
    {
        packet.insert(packet.end(), text.begin(), text.end());
        packet.push_back('\0');
        while (packet.size() % 4 != 0)
            packet.push_back('\0');
    }

    std::wstring ApplyCase(const std::wstring& text, DWORD flags)
    {
        if (text.empty())
            return {};
        const int required = LCMapStringEx(LOCALE_NAME_INVARIANT, flags, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr, 0);
        if (required <= 0)
            return text;
        std::wstring result(static_cast<size_t>(required), L'\0');
        const int written = LCMapStringEx(LOCALE_NAME_INVARIANT, flags, text.data(), static_cast<int>(text.size()), result.data(), required, nullptr, nullptr, 0);
        if (written <= 0)
            return text;
        result.resize(static_cast<size_t>(written));
        return result;
    }

    std::wstring ApplyStyle(int style, const std::wstring& text)
    {
        if (style == 1)
            return ApplyCase(text, LCMAP_UPPERCASE);
        if (style == 2)
            return ApplyCase(text, LCMAP_LOWERCASE);
        if (style == 3)
        {
            std::wstring result;
            result.reserve(text.size());
            for (wchar_t c : text)
                result.push_back(c == L' ' ? L'\u3000' : c >= L'!' && c <= L'~' ? static_cast<wchar_t>(c - L'!' + 0xFF01) : c);
            return result;
        }
        if (style == 4)
        {
            static constexpr wchar_t Small[] = L"\u1D00\u0299\u1D04\u1D05\u1D07\uA730\u0262\u029C\u026A\u1D0A\u1D0B\u029F\u1D0D\u0274\u1D0F\u1D18\u01EB\u0280\uA731\u1D1B\u1D1C\u1D20\u1D21x\u028F\u1D22";
            std::wstring result;
            result.reserve(text.size());
            for (wchar_t c : text)
            {
                const wchar_t lower = static_cast<wchar_t>(towlower(c));
                result.push_back(lower >= L'a' && lower <= L'z' ? Small[lower - L'a'] : c);
            }
            return result;
        }
        if (style == 5)
            return L"\u2728 " + text + L" \u2728";
        return text;
    }

    struct HardwareMonitor
    {
        std::wstring cpuName;
        std::wstring gpuName;
        std::wstring vramError;
        double cpuUsagePercent = 0;
        double usedRamGb = 0;
        double totalRamGb = 0;
        double totalVramGb = 0;
        double usedVramGb = 0;
        bool hasUsedVram = false;
        uint64_t lastIdle = 0;
        uint64_t lastKernel = 0;
        uint64_t lastUser = 0;

        void Initialize()
        {
            cpuName = ReadRegistryString(HKEY_LOCAL_MACHINE, L"HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0", L"ProcessorNameString");
            if (cpuName.empty())
                throw std::runtime_error("Windows did not report a CPU model.");

            ReadGraphicsAdapter();
            ReadMemory();
        }

        void ReadGraphicsAdapter()
        {
            HKEY displayClass = nullptr;
            if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, DisplayClassKey, 0, KEY_READ, &displayClass) != ERROR_SUCCESS)
                throw std::runtime_error("Windows did not report any display adapters.");

            std::wstring bestName;
            uint64_t bestMemory = 0;
            for (DWORD index = 0; ; ++index)
            {
                wchar_t subkey[256] = {};
                DWORD length = static_cast<DWORD>(std::size(subkey));
                if (RegEnumKeyExW(displayClass, index, subkey, &length, nullptr, nullptr, nullptr, nullptr) != ERROR_SUCCESS)
                    break;
                if (length != 4)
                    continue;

                HKEY adapter = nullptr;
                if (RegOpenKeyExW(displayClass, subkey, 0, KEY_READ, &adapter) != ERROR_SUCCESS)
                    continue;

                std::wstring name = ReadRegistryString(adapter, nullptr, L"DriverDesc");
                if (name.empty())
                    name = ReadRegistryString(adapter, nullptr, L"Device Description");
                const uint64_t memory = ReadDedicatedMemory(adapter);
                RegCloseKey(adapter);

                if (!name.empty() && (bestName.empty() || memory > bestMemory))
                {
                    bestName = name;
                    bestMemory = memory;
                }
            }
            RegCloseKey(displayClass);

            if (bestName.empty())
                throw std::runtime_error("Windows did not report a display adapter model.");
            gpuName = bestName;
            totalVramGb = static_cast<double>(bestMemory) / BytesPerGb;
        }

        void ReadMemory()
        {
            MEMORYSTATUSEX status = {};
            status.dwLength = sizeof(status);
            if (!GlobalMemoryStatusEx(&status))
                throw std::runtime_error("Couldn't read system memory usage.");
            totalRamGb = static_cast<double>(status.ullTotalPhys) / BytesPerGb;
            usedRamGb = static_cast<double>(status.ullTotalPhys - status.ullAvailPhys) / BytesPerGb;
        }

        void SampleCpu()
        {
            FILETIME idle = {}, kernel = {}, user = {};
            if (!GetSystemTimes(&idle, &kernel, &user))
                throw std::runtime_error("Couldn't read CPU usage.");

            const auto toValue = [](const FILETIME& value)
            {
                return (static_cast<uint64_t>(value.dwHighDateTime) << 32) | value.dwLowDateTime;
            };
            const uint64_t idleNow = toValue(idle);
            const uint64_t kernelNow = toValue(kernel);
            const uint64_t userNow = toValue(user);
            if (lastKernel != 0 || lastUser != 0)
            {
                const uint64_t total = (kernelNow - lastKernel) + (userNow - lastUser);
                const uint64_t busy = total - (idleNow - lastIdle);
                cpuUsagePercent = total == 0 ? 0 : std::clamp(static_cast<double>(busy) * 100.0 / total, 0.0, 100.0);
            }
            lastIdle = idleNow;
            lastKernel = kernelNow;
            lastUser = userNow;
        }

        void ReadVramUsage()
        {
            ComPtr<IDXGIFactory1> factory;
            HRESULT result = CreateDXGIFactory1(IID_PPV_ARGS(&factory));
            if (FAILED(result))
            {
                vramError = L"Windows GPU memory query failed.";
                hasUsedVram = false;
                return;
            }

            ComPtr<IDXGIAdapter1> selected;
            for (UINT index = 0; ; ++index)
            {
                ComPtr<IDXGIAdapter1> adapter;
                result = factory->EnumAdapters1(index, &adapter);
                if (result == DXGI_ERROR_NOT_FOUND)
                    break;
                if (FAILED(result))
                    break;

                DXGI_ADAPTER_DESC1 description = {};
                if (FAILED(adapter->GetDesc1(&description)))
                    continue;
                if (_wcsicmp(description.Description, gpuName.c_str()) == 0)
                {
                    selected = adapter;
                    break;
                }
            }

            ComPtr<IDXGIAdapter3> adapter3;
            if (!selected || FAILED(selected.As(&adapter3)))
            {
                vramError = L"The graphics driver does not expose VRAM usage.";
                hasUsedVram = false;
                return;
            }

            DXGI_QUERY_VIDEO_MEMORY_INFO info = {};
            result = adapter3->QueryVideoMemoryInfo(0, DXGI_MEMORY_SEGMENT_GROUP_LOCAL, &info);
            if (FAILED(result))
            {
                vramError = L"The graphics driver could not report VRAM usage.";
                hasUsedVram = false;
                return;
            }
            usedVramGb = static_cast<double>(info.CurrentUsage) / BytesPerGb;
            vramError.clear();
            hasUsedVram = true;
        }

        void Sample()
        {
            SampleCpu();
            ReadMemory();
            ReadVramUsage();
        }
    };

    struct OscClient
    {
        SOCKET socket = INVALID_SOCKET;
        sockaddr_storage endpoint = {};
        int endpointLength = 0;
        std::wstring error;

        ~OscClient()
        {
            if (socket != INVALID_SOCKET)
                closesocket(socket);
            WSACleanup();
        }

        int Configure(const wchar_t* ip, int port)
        {
            if (socket != INVALID_SOCKET)
            {
                closesocket(socket);
                socket = INVALID_SOCKET;
            }
            endpoint = {};
            endpointLength = 0;

            if (ip == nullptr || port < 1 || port > 65535)
            {
                error = L"Bad IP address";
                return 0;
            }

            SOCKADDR_IN address4 = {};
            address4.sin_family = AF_INET;
            address4.sin_port = htons(static_cast<u_short>(port));
            SOCKADDR_IN6 address6 = {};
            address6.sin6_family = AF_INET6;
            address6.sin6_port = htons(static_cast<u_short>(port));

            int family = AF_INET;
            if (InetPtonW(AF_INET, ip, &address4.sin_addr) == 1)
            {
                std::memcpy(&endpoint, &address4, sizeof(address4));
                endpointLength = sizeof(address4);
            }
            else if (InetPtonW(AF_INET6, ip, &address6.sin6_addr) == 1)
            {
                family = AF_INET6;
                std::memcpy(&endpoint, &address6, sizeof(address6));
                endpointLength = sizeof(address6);
            }
            else
            {
                error = L"Bad IP address";
                return 0;
            }

            socket = ::socket(family, SOCK_DGRAM, IPPROTO_UDP);
            if (socket == INVALID_SOCKET)
            {
                error = L"Couldn't open socket";
                endpointLength = 0;
                return 0;
            }
            error.clear();
            return 1;
        }

        int Send(const wchar_t* text, bool immediate, bool sound)
        {
            if (socket == INVALID_SOCKET || endpointLength == 0)
                return 0;
            const std::string utf8 = ToUtf8(text == nullptr ? L"" : text);
            if (text != nullptr && *text != L'\0' && utf8.empty())
            {
                error = L"Send failed";
                return 0;
            }

            std::vector<char> packet;
            AddOscString(packet, "/chatbox/input");
            AddOscString(packet, std::string(",s") + (immediate ? 'T' : 'F') + (sound ? 'T' : 'F'));
            AddOscString(packet, utf8);
            const int sent = sendto(socket, packet.data(), static_cast<int>(packet.size()), 0,
                reinterpret_cast<const sockaddr*>(&endpoint), endpointLength);
            if (sent == SOCKET_ERROR)
            {
                error = L"Send failed";
                return 0;
            }
            error.clear();
            return 1;
        }
    };
}

extern "C"
{
    void* __cdecl pch_hardware_create(wchar_t* error, int errorCapacity)
    {
        try
        {
            auto monitor = std::make_unique<HardwareMonitor>();
            monitor->Initialize();
            CopyText(L"", error, errorCapacity);
            return monitor.release();
        }
        catch (const std::exception& exception)
        {
            const int length = MultiByteToWideChar(CP_UTF8, 0, exception.what(), -1, nullptr, 0);
            std::wstring message(length > 0 ? static_cast<size_t>(length) : 0, L'\0');
            if (length > 1)
            {
                MultiByteToWideChar(CP_UTF8, 0, exception.what(), -1, message.data(), length);
                message.resize(static_cast<size_t>(length - 1));
            }
            CopyText(message, error, errorCapacity);
            return nullptr;
        }
    }

    void __cdecl pch_hardware_destroy(void* handle)
    {
        delete static_cast<HardwareMonitor*>(handle);
    }

    int __cdecl pch_hardware_read_info(void* handle, int field, wchar_t* output, int capacity)
    {
        if (handle == nullptr)
            return -1;
        const auto& monitor = *static_cast<HardwareMonitor*>(handle);
        switch (field)
        {
        case 0: return ReturnText(monitor.cpuName, output, capacity);
        case 1: return ReturnText(monitor.gpuName, output, capacity);
        case 2: return ReturnText(monitor.vramError, output, capacity);
        default: return -1;
        }
    }

    int __cdecl pch_hardware_sample(void* handle, PchHardwareSnapshot* snapshot, wchar_t* error, int errorCapacity)
    {
        if (handle == nullptr || snapshot == nullptr)
            return 0;
        auto& monitor = *static_cast<HardwareMonitor*>(handle);
        try
        {
            monitor.Sample();
            snapshot->CpuUsagePercent = monitor.cpuUsagePercent;
            snapshot->UsedRamGb = monitor.usedRamGb;
            snapshot->TotalRamGb = monitor.totalRamGb;
            snapshot->TotalVramGb = monitor.totalVramGb;
            snapshot->UsedVramGb = monitor.usedVramGb;
            snapshot->HasUsedVram = monitor.hasUsedVram ? 1 : 0;
            CopyText(L"", error, errorCapacity);
            return 1;
        }
        catch (const std::exception& exception)
        {
            const int length = MultiByteToWideChar(CP_UTF8, 0, exception.what(), -1, nullptr, 0);
            std::wstring message(length > 0 ? static_cast<size_t>(length) : 0, L'\0');
            if (length > 1)
            {
                MultiByteToWideChar(CP_UTF8, 0, exception.what(), -1, message.data(), length);
                message.resize(static_cast<size_t>(length - 1));
            }
            CopyText(message, error, errorCapacity);
            return 0;
        }
    }

    void* __cdecl pch_osc_create()
    {
        WSADATA data = {};
        if (WSAStartup(MAKEWORD(2, 2), &data) != 0)
            return nullptr;
        auto client = new (std::nothrow) OscClient();
        if (client == nullptr)
            WSACleanup();
        return client;
    }

    void __cdecl pch_osc_destroy(void* handle)
    {
        delete static_cast<OscClient*>(handle);
    }

    int __cdecl pch_osc_configure(void* handle, const wchar_t* ip, int port)
    {
        return handle != nullptr && static_cast<OscClient*>(handle)->Configure(ip, port);
    }

    int __cdecl pch_osc_send(void* handle, const wchar_t* text, int immediate, int sound)
    {
        return handle != nullptr && static_cast<OscClient*>(handle)->Send(text, immediate != 0, sound != 0);
    }

    int __cdecl pch_osc_read_error(void* handle, wchar_t* output, int capacity)
    {
        return handle == nullptr ? -1 : ReturnText(static_cast<OscClient*>(handle)->error, output, capacity);
    }

    int __cdecl pch_get_battery_status(unsigned char* acLineStatus, unsigned char* batteryFlag, unsigned char* batteryLifePercent)
    {
        if (acLineStatus == nullptr || batteryFlag == nullptr || batteryLifePercent == nullptr)
            return 0;
        SYSTEM_POWER_STATUS status = {};
        if (!GetSystemPowerStatus(&status))
            return 0;
        *acLineStatus = status.ACLineStatus;
        *batteryFlag = status.BatteryFlag;
        *batteryLifePercent = status.BatteryLifePercent;
        return 1;
    }

    int __cdecl pch_get_foreground_window(wchar_t* appName, int appNameCapacity, wchar_t* executable, int executableCapacity, wchar_t* title, int titleCapacity)
    {
        const HWND window = GetForegroundWindow();
        if (window == nullptr)
            return 0;
        DWORD processId = 0;
        GetWindowThreadProcessId(window, &processId);
        if (processId == 0 || processId == GetCurrentProcessId())
            return 0;

        HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, processId);
        if (process == nullptr)
            return 0;
        wchar_t path[MAX_PATH] = {};
        DWORD pathLength = static_cast<DWORD>(std::size(path));
        const BOOL gotPath = QueryFullProcessImageNameW(process, 0, path, &pathLength);
        CloseHandle(process);
        if (!gotPath || pathLength == 0)
            return 0;

        std::wstring name(path, pathLength);
        const size_t slash = name.find_last_of(L"\\/");
        if (slash != std::wstring::npos)
            name.erase(0, slash + 1);
        const size_t dot = name.find_last_of(L'.');
        const std::wstring processName = dot == std::wstring::npos ? name : name.substr(0, dot);
        if (processName.empty())
            return 0;

        std::wstring friendly = processName;
        friendly[0] = static_cast<wchar_t>(towupper(friendly[0]));
        wchar_t windowTitle[512] = {};
        const int titleLength = GetWindowTextW(window, windowTitle, static_cast<int>(std::size(windowTitle)));
        CopyText(friendly, appName, appNameCapacity);
        CopyText(name, executable, executableCapacity);
        CopyText(titleLength > 0 ? std::wstring(windowTitle, static_cast<size_t>(titleLength)) : std::wstring(), title, titleCapacity);
        return 1;
    }

    int __cdecl pch_expand_template(const wchar_t* text, const wchar_t* const* names, const wchar_t* const* values, int count, wchar_t* output, int capacity)
    {
        if (text == nullptr || count < 0 || (count > 0 && (names == nullptr || values == nullptr)))
            return -1;

        std::unordered_map<std::wstring, std::wstring> replacements;
        for (int index = 0; index < count; ++index)
            if (names[index] != nullptr && values[index] != nullptr)
                replacements[ToLower(names[index])] = values[index];

        const std::wstring source(text);
        std::wstring result;
        result.reserve(source.size());
        for (size_t index = 0; index < source.size();)
        {
            if (source[index] != L'{')
            {
                result.push_back(source[index++]);
                continue;
            }
            const size_t end = source.find(L'}', index + 1);
            if (end == std::wstring::npos || end == index + 1)
            {
                result.push_back(source[index++]);
                continue;
            }
            const std::wstring token = source.substr(index + 1, end - index - 1);
            const bool lettersOnly = std::all_of(token.begin(), token.end(), [](wchar_t c)
            {
                return (c >= L'a' && c <= L'z') || (c >= L'A' && c <= L'Z');
            });
            const auto replacement = lettersOnly ? replacements.find(ToLower(token)) : replacements.end();
            if (replacement == replacements.end())
                result.append(source, index, end - index + 1);
            else
                result.append(replacement->second);
            index = end + 1;
        }
        return ReturnText(result, output, capacity);
    }

    int __cdecl pch_apply_style(int style, const wchar_t* text, wchar_t* output, int capacity)
    {
        if (text == nullptr)
            return -1;
        return ReturnText(ApplyStyle(style, text), output, capacity);
    }

    int __cdecl pch_pick_status(int mode, const int* flags, const wchar_t* const* collections, int count, const wchar_t* activeCollection, int currentIndex, int randomValue)
    {
        if (count < 0 || (count > 0 && (flags == nullptr || collections == nullptr)))
            return -1;

        std::vector<int> eligible;
        std::vector<int> favorites;
        for (int index = 0; index < count; ++index)
        {
            if ((flags[index] & 3) != 3)
                continue;
            if (activeCollection != nullptr && *activeCollection != L'\0'
                && (collections[index] == nullptr || _wcsicmp(collections[index], activeCollection) != 0))
                continue;
            eligible.push_back(index);
            if ((flags[index] & 4) != 0)
                favorites.push_back(index);
        }

        if (mode == 2 && !favorites.empty())
            eligible.swap(favorites);
        if (eligible.empty())
            return -1;

        if (mode == 1 && eligible.size() > 1)
        {
            eligible.erase(std::remove(eligible.begin(), eligible.end(), currentIndex), eligible.end());
            if (eligible.empty())
                return currentIndex;
            return eligible[static_cast<size_t>(randomValue) % eligible.size()];
        }

        const int start = currentIndex >= 0 && currentIndex < count ? currentIndex : -1;
        for (int offset = 1; offset <= count; ++offset)
        {
            const int candidate = (start + offset) % count;
            if (std::find(eligible.begin(), eligible.end(), candidate) != eligible.end())
                return candidate;
        }
        return eligible.front();
    }

    int __cdecl pch_compose_lines(const wchar_t* const* keys, const wchar_t* const* texts, int count, const wchar_t* const* order, int orderCount, wchar_t* output, int capacity)
    {
        if (count < 0 || orderCount < 0 || (count > 0 && (keys == nullptr || texts == nullptr)) || (orderCount > 0 && order == nullptr))
            return -1;

        std::vector<std::wstring> result;
        for (int orderIndex = 0; orderIndex < orderCount; ++orderIndex)
        {
            if (order[orderIndex] == nullptr)
                continue;
            for (int entryIndex = 0; entryIndex < count; ++entryIndex)
            {
                if (keys[entryIndex] != nullptr && texts[entryIndex] != nullptr
                    && _wcsicmp(keys[entryIndex], order[orderIndex]) == 0 && *texts[entryIndex] != L'\0')
                {
                    result.emplace_back(texts[entryIndex]);
                    break;
                }
            }
        }
        for (int entryIndex = 0; entryIndex < count; ++entryIndex)
        {
            if (texts[entryIndex] == nullptr || *texts[entryIndex] == L'\0')
                continue;
            const std::wstring value(texts[entryIndex]);
            if (std::find(result.begin(), result.end(), value) == result.end())
                result.push_back(value);
        }

        std::wstring composed;
        for (const auto& line : result)
        {
            if (!composed.empty())
                composed.push_back(L'\n');
            composed.append(line);
        }
        return ReturnText(composed, output, capacity);
    }

    int __cdecl pch_trim_chat_text(const wchar_t* text, int limit, wchar_t* output, int capacity)
    {
        if (text == nullptr || limit < 0)
            return -1;
        std::wstring value(text);
        if (value.size() > static_cast<size_t>(limit))
        {
            size_t length = static_cast<size_t>(limit);
            if (length > 0 && value[length - 1] >= 0xD800 && value[length - 1] <= 0xDBFF)
                --length;
            value.resize(length);
        }
        return ReturnText(value, output, capacity);
    }
}
