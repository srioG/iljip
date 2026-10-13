#include "pch.h"
#include "IljipCommands.h"

// Iljip.exe 부르기 공통(W2-12). 명령줄 모양·목록 파일 규칙은 C# Iljip.Services.ShellCommandLine 이 거울이다 —
// 한쪽을 고치면 다른 쪽도 고칠 것(ShellExtensionContractTests 가 MaxCommandLineChars·목록 폴더·'--list-file' 을 소스로 맞춰 봄).

namespace IljipShell
{
    std::wstring ModuleFolder()
    {
        // 이 DLL 이 있는 폴더(긴 경로도 되게 버퍼를 늘려 가며)
        HMODULE module = nullptr;
        GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&ModuleFolder),
            &module);
        std::vector<wchar_t> buffer(MAX_PATH);
        for (;;)
        {
            DWORD n = GetModuleFileNameW(module, buffer.data(), static_cast<DWORD>(buffer.size()));
            if (n == 0) return std::wstring();
            if (n < buffer.size()) break;
            buffer.resize(buffer.size() * 2);
        }
        std::wstring path(buffer.data());
        const size_t slash = path.find_last_of(L'\\');
        return slash == std::wstring::npos ? std::wstring() : path.substr(0, slash);
    }

    std::wstring IljipExePath()
    {
        // dll 과 같은 폴더의 Iljip.exe(패키지 외부 위치 — packaging\Build.ps1 이 같이 둠)
        return ModuleFolder() + L"\\Iljip.exe";
    }

    std::wstring QuoteCommandArg(const std::wstring& arg)
    {
        // CommandLineToArgvW 규칙: 따옴표 바로 앞의 역슬래시 n개는 2n개로(닫는 따옴표 포함), 그 밖의 역슬래시는 그대로
        std::wstring r;
        r.reserve(arg.size() + 2);
        r.push_back(L'"');
        size_t slashes = 0;
        for (wchar_t c : arg)
        {
            if (c == L'\\')
            {
                ++slashes;
                continue;
            }
            if (c == L'"')
            {
                r.append(slashes * 2 + 1, L'\\');
                r.push_back(L'"');
                slashes = 0;
                continue;
            }
            r.append(slashes, L'\\');
            r.push_back(c);
            slashes = 0;
        }
        r.append(slashes * 2, L'\\');
        r.push_back(L'"');
        return r;
    }

    namespace
    {
        HRESULT StartProcess(const std::wstring& commandLine)
        {
            STARTUPINFOW si{};
            si.cb = sizeof(si);
            PROCESS_INFORMATION pi{};
            // CreateProcessW 는 lpCommandLine 이 바꿀 수 있는 버퍼여야 함
            std::vector<wchar_t> buffer(commandLine.begin(), commandLine.end());
            buffer.push_back(L'\0');
            if (!CreateProcessW(nullptr, buffer.data(), nullptr, nullptr, FALSE, CREATE_UNICODE_ENVIRONMENT, nullptr, nullptr, &si, &pi))
                return HRESULT_FROM_WIN32(GetLastError());
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            return S_OK;
        }
    }

    std::wstring ListFileFolder()
    {
        PWSTR local = nullptr;
        if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, KF_FLAG_DEFAULT, nullptr, &local)) || !local)
            return std::wstring();
        std::wstring folder = local;
        CoTaskMemFree(local);
        // C# AppPaths.TempRoot\args 와 같은 곳(일집이 읽고 지우며, 남으면 임시 폴더 청소 대상)
        folder += L"\\Iljip\\temp\\args";
        return folder;
    }

    std::wstring WriteListFile(const std::vector<std::wstring>& paths)
    {
        std::wstring folder = ListFileFolder();
        if (folder.empty()) return std::wstring();
        int made = SHCreateDirectoryExW(nullptr, folder.c_str(), nullptr);
        if (made != ERROR_SUCCESS && made != ERROR_ALREADY_EXISTS && made != ERROR_FILE_EXISTS) return std::wstring();

        GUID guid{};
        if (FAILED(CoCreateGuid(&guid))) return std::wstring();
        wchar_t text[64] = {};
        if (StringFromGUID2(guid, text, 64) == 0) return std::wstring();
        std::wstring name(text);
        if (name.size() >= 2) name = name.substr(1, name.size() - 2);   // { } 떼기
        for (auto& c : name) c = static_cast<wchar_t>(towlower(c));
        std::wstring file = folder + L"\\" + name + L".lst";

        // UTF-8(BOM 없음), 한 줄에 경로 하나, 줄 끝 CRLF
        std::string utf8;
        for (const auto& p : paths)
        {
            if (!p.empty())
            {
                int n = WideCharToMultiByte(CP_UTF8, 0, p.c_str(), static_cast<int>(p.size()), nullptr, 0, nullptr, nullptr);
                if (n <= 0) return std::wstring();
                size_t at = utf8.size();
                utf8.resize(at + static_cast<size_t>(n));
                WideCharToMultiByte(CP_UTF8, 0, p.c_str(), static_cast<int>(p.size()), utf8.data() + at, n, nullptr, nullptr);
            }
            utf8 += "\r\n";
        }

        HANDLE h = CreateFileW(file.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (h == INVALID_HANDLE_VALUE) return std::wstring();
        DWORD written = 0;
        BOOL ok = WriteFile(h, utf8.data(), static_cast<DWORD>(utf8.size()), &written, nullptr) && written == utf8.size();
        CloseHandle(h);
        if (!ok)
        {
            DeleteFileW(file.c_str());   // 반쪽 목록을 남기지 않음
            return std::wstring();
        }
        return file;
    }

    HRESULT LaunchIljipCommand(const std::wstring& verb, const std::vector<std::wstring>& leading, const std::vector<std::wstring>& paths)
    {
        std::wstring head = QuoteCommandArg(IljipExePath());
        head += L' ';
        head += verb;
        for (const auto& a : leading)
        {
            head += L' ';
            head += QuoteCommandArg(a);
        }
        std::wstring tail;
        for (const auto& p : paths)
        {
            tail += L' ';
            tail += QuoteCommandArg(p);
        }
        if (head.size() + tail.size() <= MaxCommandLineChars)
            return StartProcess(head + tail);

        // 너무 길다(아주 많이 고름) — 경로들은 목록 파일로
        std::wstring list = WriteListFile(paths);
        if (list.empty()) return E_FAIL;
        std::wstring line = head;
        line += L' ';
        line += L"--list-file";
        line += L' ';
        line += QuoteCommandArg(list);
        return StartProcess(line);
    }

    HRESULT LaunchIljipAlone()
    {
        return StartProcess(QuoteCommandArg(IljipExePath()));
    }

    std::vector<std::wstring> SelectedPaths(IShellItemArray* items)
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

    std::vector<std::wstring> DropFiles(IDataObject* data)
    {
        std::vector<std::wstring> result;
        if (!data) return result;
        FORMATETC format = { CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL };
        STGMEDIUM medium{};
        if (FAILED(data->GetData(&format, &medium))) return result;
        if (auto drop = static_cast<HDROP>(GlobalLock(medium.hGlobal)))
        {
            const UINT count = DragQueryFileW(drop, 0xFFFFFFFF, nullptr, 0);
            for (UINT i = 0; i < count; ++i)
            {
                const UINT length = DragQueryFileW(drop, i, nullptr, 0);
                if (length == 0) continue;
                std::wstring path(static_cast<size_t>(length) + 1, L'\0');
                DragQueryFileW(drop, i, path.data(), length + 1);
                path.resize(length);
                result.push_back(std::move(path));
            }
            GlobalUnlock(medium.hGlobal);
        }
        ReleaseStgMedium(&medium);
        return result;
    }
}
