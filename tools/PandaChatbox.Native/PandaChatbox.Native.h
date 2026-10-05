#pragma once

struct PchHardwareSnapshot
{
    double CpuUsagePercent;
    double UsedRamGb;
    double TotalRamGb;
    double TotalVramGb;
    double UsedVramGb;
    int HasUsedVram;
};

extern "C"
{
    __declspec(dllexport) void* __cdecl pch_hardware_create(wchar_t* error, int errorCapacity);
    __declspec(dllexport) void __cdecl pch_hardware_destroy(void* handle);
    __declspec(dllexport) int __cdecl pch_hardware_read_info(void* handle, int field, wchar_t* output, int capacity);
    __declspec(dllexport) int __cdecl pch_hardware_sample(void* handle, PchHardwareSnapshot* snapshot, wchar_t* error, int errorCapacity);

    __declspec(dllexport) void* __cdecl pch_osc_create();
    __declspec(dllexport) void __cdecl pch_osc_destroy(void* handle);
    __declspec(dllexport) int __cdecl pch_osc_configure(void* handle, const wchar_t* ip, int port);
    __declspec(dllexport) int __cdecl pch_osc_send(void* handle, const wchar_t* text, int immediate, int sound);
    __declspec(dllexport) int __cdecl pch_osc_read_error(void* handle, wchar_t* output, int capacity);

    __declspec(dllexport) int __cdecl pch_get_battery_status(unsigned char* acLineStatus, unsigned char* batteryFlag, unsigned char* batteryLifePercent);
    __declspec(dllexport) int __cdecl pch_get_foreground_window(wchar_t* appName, int appNameCapacity, wchar_t* executable, int executableCapacity, wchar_t* title, int titleCapacity);

    __declspec(dllexport) int __cdecl pch_expand_template(const wchar_t* text, const wchar_t* const* names, const wchar_t* const* values, int count, wchar_t* output, int capacity);
    __declspec(dllexport) int __cdecl pch_apply_style(int style, const wchar_t* text, wchar_t* output, int capacity);
    __declspec(dllexport) int __cdecl pch_pick_status(int mode, const int* flags, const wchar_t* const* collections, int count, const wchar_t* activeCollection, int currentIndex, int randomValue);
    __declspec(dllexport) int __cdecl pch_compose_lines(const wchar_t* const* keys, const wchar_t* const* texts, int count, const wchar_t* const* order, int orderCount, wchar_t* output, int capacity);
    __declspec(dllexport) int __cdecl pch_trim_chat_text(const wchar_t* text, int limit, wchar_t* output, int capacity);
}
