#include "pch.h"
#include "IljipCommands.h"

namespace IljipShell
{
    // ===== 베이스 유틸 =====

    template <typename T>
    std::wstring IljipCommandBase<T>::GetIljipExePath()
    {
        // 셸 확장 DLL이 위치한 폴더 + "Iljip.exe"
        wchar_t dllPath[MAX_PATH] = {};
        HMODULE hMod = nullptr;
        GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&GetIljipExePath),
            &hMod);
        GetModuleFileNameW(hMod, dllPath, MAX_PATH);

        // dllPath = "...\Iljip.ShellExtension.dll". 동명 폴더 → Iljip.exe
        PathCchRemoveFileSpec(dllPath, MAX_PATH);
        std::wstring exe = dllPath;
        exe += L"\\Iljip.exe";
        return exe;
    }

    template <typename T>
    std::vector<std::wstring> IljipCommandBase<T>::GetSelectedPaths(IShellItemArray* items)
    {
        std::vector<std::wstring> result;
        if (!items) return result;

        DWORD count = 0;
        if (FAILED(items->GetCount(&count))) return result;

        for (DWORD i = 0; i < count; ++i)
        {
            Microsoft::WRL::ComPtr<IShellItem> item;
            if (FAILED(items->GetItemAt(i, &item))) continue;

            LPWSTR raw = nullptr;
            if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &raw)) && raw)
            {
                result.emplace_back(raw);
                CoTaskMemFree(raw);
            }
        }
        return result;
    }

    template <typename T>
    std::wstring IljipCommandBase<T>::QuoteArg(const std::wstring& path)
    {
        std::wstring r;
        r.reserve(path.size() + 2);
        r.push_back(L'"');
        r.append(path);
        r.push_back(L'"');
        return r;
    }

    template <typename T>
    HRESULT IljipCommandBase<T>::LaunchIljip(const std::wstring& argLine)
    {
        std::wstring exe = GetIljipExePath();
        std::wstring cmd = QuoteArg(exe) + L" " + argLine;

        STARTUPINFOW si{};
        si.cb = sizeof(si);
        PROCESS_INFORMATION pi{};

        // CreateProcessW는 lpCommandLine이 변경 가능한 버퍼여야 함
        std::vector<wchar_t> buffer(cmd.begin(), cmd.end());
        buffer.push_back(L'\0');

        if (!CreateProcessW(
                nullptr, buffer.data(), nullptr, nullptr, FALSE,
                CREATE_UNICODE_ENVIRONMENT, nullptr, nullptr, &si, &pi))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return S_OK;
    }

    // 명시적 인스턴스화 (linker 에러 방지)
    template class IljipCommandBase<OpenWithIljipCommand>;
    template class IljipCommandBase<ExtractHereCommand>;
    template class IljipCommandBase<ExtractToCommand>;
    template class IljipCommandBase<CompressWithIljipCommand>;

    // ===== 명령 실행 =====

    HRESULT OpenWithIljipCommand::Execute(IShellItemArray* items)
    {
        auto paths = GetSelectedPaths(items);
        if (paths.empty()) return S_OK;
        // 단일 압축 파일만 처리 (다중 선택은 첫 항목만)
        return LaunchIljip(L"--open " + QuoteArg(paths[0]));
    }

    HRESULT ExtractHereCommand::Execute(IShellItemArray* items)
    {
        auto paths = GetSelectedPaths(items);
        for (const auto& p : paths)
        {
            LaunchIljip(L"--extract-here " + QuoteArg(p));
        }
        return S_OK;
    }

    HRESULT ExtractToCommand::Execute(IShellItemArray* items)
    {
        auto paths = GetSelectedPaths(items);
        if (paths.empty()) return S_OK;
        return LaunchIljip(L"--extract " + QuoteArg(paths[0]));
    }

    HRESULT CompressWithIljipCommand::Execute(IShellItemArray* items)
    {
        auto paths = GetSelectedPaths(items);
        if (paths.empty()) return S_OK;

        std::wstring args = L"--compress";
        for (const auto& p : paths)
        {
            args.append(L" ");
            args.append(QuoteArg(p));
        }
        return LaunchIljip(args);
    }
}

// COM 클래스 등록 — WRL의 CoCreatableClass 매크로는 네임스페이스 접두사가 인자에 들어가면
// 내부적으로 `__object_NAMESPACE::Class` 같은 깨진 이름을 만든다.
// using namespace로 클래스 이름만 그대로 노출.
using namespace IljipShell;

CoCreatableClass(OpenWithIljipCommand);
CoCreatableClass(ExtractHereCommand);
CoCreatableClass(ExtractToCommand);
CoCreatableClass(CompressWithIljipCommand);
