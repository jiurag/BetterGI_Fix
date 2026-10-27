#include <Windows.h>

#include <cstdio>
#include <cstdint>
#include <cstring>

namespace
{
    // BetterGI 创建的共享内存名前缀，后接原神进程 ID
    constexpr wchar_t MappingNamePrefix[] = L"Local\\BetterGI.GenshinFpsUnlock.";

    // 与原 120 帧辅助保持一致的写入周期
    constexpr DWORD WriteIntervalMilliseconds = 62;

    // BetterGI 超过该时间没有更新心跳时，恢复原帧率并停止写入
    constexpr ULONGLONG HeartbeatTimeoutMilliseconds = 5000;

    // 允许写入的最小帧率
    constexpr LONG MinimumTargetFramerate = 30;

    // 允许写入的最大帧率
    constexpr LONG MaximumTargetFramerate = 240;

    // Hook 线程当前阶段
    enum class HookState : LONG
    {
        Pending = 0,
        Ready = 1,
        Failed = 2,
        Stopped = 3,
    };

    // Hook 失败原因
    enum class HookError : LONG
    {
        None = 0,
        NotGenshinProcess = 1,
        OpenMappingFailed = 2,
        MapViewFailed = 3,
        Il2cppSectionNotFound = 4,
        FramerateSignatureNotFound = 5,
        InvalidFramerateAddress = 6,
    };

    // 与 C# GenshinFpsUnlockSharedState 保持完全一致的四字节字段布局
#pragma pack(push, 4)
    struct SharedState
    {
        volatile LONG State;
        volatile LONG ErrorCode;
        volatile LONG GameProcessId;
        volatile LONG TargetFramerate;
        volatile LONG Command;
        volatile LONG OriginalFramerate;
        volatile LONG FramerateRva;
        volatile LONG Heartbeat;
    };
#pragma pack(pop)

    // 判断当前进程内的一段地址是否已经提交且可读取
    bool IsReadableRange(const void* address, size_t size)
    {
        if (address == nullptr || size == 0)
        {
            return false;
        }

        const auto begin = reinterpret_cast<uintptr_t>(address);
        const auto end = begin + size;
        if (end < begin)
        {
            return false;
        }

        auto cursor = begin;
        while (cursor < end)
        {
            MEMORY_BASIC_INFORMATION information{};
            if (VirtualQuery(
                    reinterpret_cast<const void*>(cursor),
                    &information,
                    sizeof(information))
                == 0)
            {
                return false;
            }

            constexpr DWORD deniedProtection = PAGE_NOACCESS | PAGE_GUARD;
            if (information.State != MEM_COMMIT
                || (information.Protect & deniedProtection) != 0)
            {
                return false;
            }

            const auto regionEnd =
                reinterpret_cast<uintptr_t>(information.BaseAddress)
                + information.RegionSize;
            if (regionEnd <= cursor)
            {
                return false;
            }

            cursor = (regionEnd < end) ? regionEnd : end;
        }

        return true;
    }

    // 判断当前进程内的一段地址是否可写
    bool IsWritableRange(const void* address, size_t size)
    {
        if (!IsReadableRange(address, size))
        {
            return false;
        }

        MEMORY_BASIC_INFORMATION information{};
        if (VirtualQuery(address, &information, sizeof(information)) == 0)
        {
            return false;
        }

        const DWORD protection = information.Protect & 0xFF;
        return protection == PAGE_READWRITE
            || protection == PAGE_WRITECOPY
            || protection == PAGE_EXECUTE_READWRITE
            || protection == PAGE_EXECUTE_WRITECOPY;
    }

    // 从当前地址读取一个小端 32 位整数
    int32_t ReadInt32(const uint8_t* address)
    {
        int32_t value = 0;
        std::memcpy(&value, address, sizeof(value));
        return value;
    }

    // 判断字符串是否以指定后缀结尾，忽略大小写
    bool EndsWithIgnoreCase(const wchar_t* value, const wchar_t* suffix)
    {
        if (value == nullptr || suffix == nullptr)
        {
            return false;
        }

        const size_t valueLength = wcslen(value);
        const size_t suffixLength = wcslen(suffix);
        if (valueLength < suffixLength)
        {
            return false;
        }

        return CompareStringOrdinal(
                   value + (valueLength - suffixLength),
                   static_cast<int>(suffixLength),
                   suffix,
                   static_cast<int>(suffixLength),
                   TRUE)
            == CSTR_EQUAL;
    }

    // 读取当前模块的完整文件路径
    bool TryGetCurrentModulePath(wchar_t* buffer, DWORD bufferLength)
    {
        if (buffer == nullptr || bufferLength == 0)
        {
            return false;
        }

        const DWORD length =
            GetModuleFileNameW(nullptr, buffer, bufferLength);
        return length > 0 && length < bufferLength;
    }

    // 在当前原神主模块中定位名为 il2cpp 的节
    bool TryFindIl2cppSection(uint8_t*& sectionBegin, uint8_t*& sectionEnd)
    {
        const auto imageBase =
            reinterpret_cast<uint8_t*>(GetModuleHandleW(nullptr));
        if (imageBase == nullptr)
        {
            return false;
        }

        const auto dosHeader =
            reinterpret_cast<const IMAGE_DOS_HEADER*>(imageBase);
        if (dosHeader->e_magic != IMAGE_DOS_SIGNATURE)
        {
            return false;
        }

        const auto ntHeaders = reinterpret_cast<const IMAGE_NT_HEADERS64*>(
            imageBase + dosHeader->e_lfanew);
        if (ntHeaders->Signature != IMAGE_NT_SIGNATURE)
        {
            return false;
        }

        const auto sections = IMAGE_FIRST_SECTION(ntHeaders);
        for (WORD index = 0; index < ntHeaders->FileHeader.NumberOfSections; ++index)
        {
            const auto& section = sections[index];
            char name[IMAGE_SIZEOF_SHORT_NAME + 1]{};
            std::memcpy(name, section.Name, IMAGE_SIZEOF_SHORT_NAME);
            if (std::strcmp(name, "il2cpp") == 0)
            {
                sectionBegin = imageBase + section.VirtualAddress;
                sectionEnd = sectionBegin + section.Misc.VirtualSize;
                return IsReadableRange(
                    sectionBegin,
                    section.Misc.VirtualSize);
            }
        }

        return false;
    }

    // 在游戏进程内部定位动态帧率字段
    int32_t* FindFramerateField()
    {
        uint8_t* sectionBegin = nullptr;
        uint8_t* sectionEnd = nullptr;
        if (!TryFindIl2cppSection(sectionBegin, sectionEnd))
        {
            return nullptr;
        }

        constexpr uint8_t signature[] = {
            0xB9, 0x3C, 0x00, 0x00, 0x00, 0xE8,
        };

        uint8_t* cursor = sectionBegin;
        while (cursor < sectionEnd)
        {
            const auto remaining =
                static_cast<size_t>(sectionEnd - cursor);
            auto* found = static_cast<uint8_t*>(
                std::memchr(cursor, signature[0], remaining));
            if (found == nullptr)
            {
                break;
            }

            if (static_cast<size_t>(sectionEnd - found) >= sizeof(signature)
                && std::memcmp(found, signature, sizeof(signature)) == 0)
            {
                uint8_t* rip = found + 5;
                const auto firstDisplacement = ReadInt32(rip + 1);
                auto* callTarget = rip + 5 + firstDisplacement;

                if (IsReadableRange(callTarget, 1) && *callTarget == 0xE9)
                {
                    bool validChain = true;
                    for (int step = 0; step < 32; ++step)
                    {
                        if (!IsReadableRange(rip, 5))
                        {
                            validChain = false;
                            break;
                        }

                        if (*rip != 0xE8 && *rip != 0xE9)
                        {
                            break;
                        }

                        auto* next = rip + 5 + ReadInt32(rip + 1);
                        if (next == rip || !IsReadableRange(next, 1))
                        {
                            validChain = false;
                            break;
                        }
                        rip = next;
                    }

                    if (validChain
                        && IsReadableRange(rip, 6)
                        && rip[0] == 0x89
                        && rip[1] == 0x0D)
                    {
                        auto* framerate = reinterpret_cast<int32_t*>(
                            rip + 6 + ReadInt32(rip + 2));
                        if (IsWritableRange(framerate, sizeof(*framerate)))
                        {
                            return framerate;
                        }
                    }
                }
            }

            cursor = found + 1;
        }

        return nullptr;
    }

    // 写入 Hook 当前状态
    void SetHookState(SharedState* state, HookState hookState)
    {
        if (state != nullptr)
        {
            InterlockedExchange(
                &state->State,
                static_cast<LONG>(hookState));
        }
    }

    // 写入 Hook 失败原因
    void SetHookError(SharedState* state, HookError error)
    {
        if (state != nullptr)
        {
            InterlockedExchange(
                &state->ErrorCode,
                static_cast<LONG>(error));
        }
    }

    // 将目标帧率限制在允许范围内
    LONG ClampFramerate(LONG value)
    {
        if (value < MinimumTargetFramerate)
        {
            return MinimumTargetFramerate;
        }
        if (value > MaximumTargetFramerate)
        {
            return MaximumTargetFramerate;
        }
        return value;
    }

    // 在游戏进程内持续写入帧率，并在停止或心跳超时后恢复原值
    DWORD WINAPI HookWorker(void*)
    {
        wchar_t processPath[32768]{};
        if (!TryGetCurrentModulePath(processPath, ARRAYSIZE(processPath))
            || (!EndsWithIgnoreCase(processPath, L"\\YuanShen.exe")
                && !EndsWithIgnoreCase(processPath, L"\\GenshinImpact.exe")))
        {
            return 1;
        }

        wchar_t mappingName[128]{};
        swprintf_s(
            mappingName,
            L"%s%lu",
            MappingNamePrefix,
            GetCurrentProcessId());

        HANDLE mapping = OpenFileMappingW(
            FILE_MAP_READ | FILE_MAP_WRITE,
            FALSE,
            mappingName);
        if (mapping == nullptr)
        {
            return 2;
        }

        auto* state = static_cast<SharedState*>(
            MapViewOfFile(
                mapping,
                FILE_MAP_READ | FILE_MAP_WRITE,
                0,
                0,
                sizeof(SharedState)));
        if (state == nullptr)
        {
            CloseHandle(mapping);
            return 3;
        }

        HMODULE pinnedModule = nullptr;
        GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS
                | GET_MODULE_HANDLE_EX_FLAG_PIN,
            reinterpret_cast<LPCWSTR>(&HookWorker),
            &pinnedModule);

        int32_t* framerate = FindFramerateField();
        if (framerate == nullptr)
        {
            SetHookError(state, HookError::FramerateSignatureNotFound);
            SetHookState(state, HookState::Failed);
            UnmapViewOfFile(state);
            CloseHandle(mapping);
            return 4;
        }

        const auto imageBase = reinterpret_cast<uintptr_t>(
            GetModuleHandleW(nullptr));
        const LONG originalValue =
            InterlockedCompareExchange(
                reinterpret_cast<volatile LONG*>(framerate),
                0,
                0);
        InterlockedExchange(
            &state->OriginalFramerate,
            originalValue);
        InterlockedExchange(
            &state->FramerateRva,
            static_cast<LONG>(
                reinterpret_cast<uintptr_t>(framerate) - imageBase));
        SetHookState(state, HookState::Ready);

        LONG previousHeartbeat = state->Heartbeat;
        ULONGLONG lastHeartbeatChange = GetTickCount64();

        for (;;)
        {
            const LONG heartbeat = state->Heartbeat;
            if (heartbeat != previousHeartbeat)
            {
                previousHeartbeat = heartbeat;
                lastHeartbeatChange = GetTickCount64();
            }

            const ULONGLONG now = GetTickCount64();
            if (state->Command == 0
                || now - lastHeartbeatChange > HeartbeatTimeoutMilliseconds)
            {
                InterlockedExchange(
                    reinterpret_cast<volatile LONG*>(framerate),
                    originalValue);
                SetHookState(state, HookState::Stopped);
                break;
            }

            const LONG targetFramerate =
                ClampFramerate(state->TargetFramerate);
            InterlockedExchange(
                reinterpret_cast<volatile LONG*>(framerate),
                targetFramerate);
            Sleep(WriteIntervalMilliseconds);
        }

        UnmapViewOfFile(state);
        CloseHandle(mapping);
        return 0;
    }
}

// DLL 被窗口消息钩子载入游戏进程后创建独立工作线程
BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(module);
        HANDLE thread = CreateThread(
            nullptr,
            0,
            HookWorker,
            nullptr,
            0,
            nullptr);
        if (thread != nullptr)
        {
            CloseHandle(thread);
        }
    }
    return TRUE;
}

// 窗口消息钩子导出函数；实际工作由 DllMain 创建的工作线程完成
extern "C" __declspec(dllexport) LRESULT CALLBACK
GenshinFpsUnlockHookProc(int code, WPARAM wParam, LPARAM lParam)
{
    return CallNextHookEx(nullptr, code, wParam, lParam);
}
