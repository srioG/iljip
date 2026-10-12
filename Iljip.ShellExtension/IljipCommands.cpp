#include "pch.h"
#include "IljipCommands.h"

#include <cwctype>

namespace IljipShell
{
    // ===== 이름 규칙 (C# ArchiveNaming.FolderNameFor 와 같게) =====

    namespace
    {
        bool IsAsciiDigit(wchar_t c) noexcept { return c >= L'0' && c <= L'9'; }

        wchar_t AsciiLower(wchar_t c) noexcept { return (c >= L'A' && c <= L'Z') ? static_cast<wchar_t>(c - L'A' + L'a') : c; }

        bool EndsWithNoCase(const std::wstring& s, const wchar_t* suffix)
        {
            const size_t n = wcslen(suffix);
            if (s.size() < n) return false;
            for (size_t i = 0; i < n; ++i)
                if (AsciiLower(s[s.size() - n + i]) != AsciiLower(suffix[i])) return false;
            return true;
        }

        bool MatchesNoCaseAt(const std::wstring& s, size_t pos, const wchar_t* word)
        {
            const size_t n = wcslen(word);
            if (pos + n > s.size()) return false;
            for (size_t i = 0; i < n; ++i)
                if (AsciiLower(s[pos + i]) != AsciiLower(word[i])) return false;
            return true;
        }
    }

    std::wstring FolderNameFor(const std::wstring& fileName)
    {
        std::wstring name = fileName;

        // ① 숫자 분할 접미사 '.001'(정확히 숫자 3개) — '보고서.7z.001' → '보고서.7z', '보고서.zip.001' → '보고서.zip'
        if (name.size() >= 4 && name[name.size() - 4] == L'.'
            && IsAsciiDigit(name[name.size() - 3]) && IsAsciiDigit(name[name.size() - 2]) && IsAsciiDigit(name[name.size() - 1]))
        {
            name.erase(name.size() - 4);
        }

        // ② 마지막 확장자 바로 앞의 '.partN'(rar)·'.volN'(egg) — '보고서.part1.rar' → '보고서.rar'
        const size_t lastDot = name.find_last_of(L'.');
        if (lastDot != std::wstring::npos && lastDot > 0)
        {
            size_t digitsEnd = lastDot;
            size_t d = digitsEnd;
            while (d > 0 && IsAsciiDigit(name[d - 1])) --d;
            if (d < digitsEnd)   // 숫자가 하나 이상
            {
                for (const wchar_t* word : { L".part", L".vol" })
                {
                    const size_t len = wcslen(word);
                    if (d >= len && MatchesNoCaseAt(name, d - len, word))
                    {
                        name.erase(d - len, digitsEnd - (d - len));
                        break;
                    }
                }
            }
        }

        // ③ 묶음 확장자 '.tar.gz'·'.tar.bz2'·'.tar.xz' 는 통째로, 아니면 마지막 확장자 하나
        bool compound = false;
        for (const wchar_t* ext : { L".tar.gz", L".tar.bz2", L".tar.xz" })
        {
            if (EndsWithNoCase(name, ext))
            {
                name.erase(name.size() - wcslen(ext));
                compound = true;
                break;
            }
        }
        if (!compound)
        {
            const size_t dot = name.find_last_of(L'.');
            if (dot != std::wstring::npos) name.erase(dot);
        }

        // 끝의 점·공백 지우기(Windows 가 어차피 지움), 비면 '압축풀림'
        while (!name.empty() && (name.back() == L'.' || name.back() == L' ')) name.pop_back();
        bool blank = true;
        for (wchar_t c : name)
            if (!iswspace(c)) { blank = false; break; }
        return blank ? std::wstring(L"압축풀림") : name;
    }

    std::wstring ShortenForMenu(const std::wstring& name)
    {
        constexpr size_t MaxChars = 40;
        if (name.size() <= MaxChars) return name;
        size_t cut = MaxChars - 1;
        if (cut > 0 && IS_HIGH_SURROGATE(name[cut - 1])) --cut;   // 이모지 등 서로게이트 쌍을 반으로 자르지 않음
        return name.substr(0, cut) + L"…";
    }

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
    template class IljipCommandBase<ExtractToFolderCommand>;
    template class IljipCommandBase<ExtractToCommand>;
    template class IljipCommandBase<CompressWithIljipCommand>;

    // ===== 제목 =====

    std::wstring ExtractToFolderCommand::TitleFor(IShellItemArray* items)
    {
        DWORD count = 0;
        if (!items || FAILED(items->GetCount(&count)) || count == 0) return Title();
        if (count > 1) return L"각각 이름 폴더에 풀기 (일집)";

        // 이름만 본다(파일을 열지 않음). 확장자 숨김 설정과 상관없이 실제 파일 이름이 필요해 SIGDN_PARENTRELATIVEPARSING.
        Microsoft::WRL::ComPtr<IShellItem> item;
        if (FAILED(items->GetItemAt(0, &item))) return Title();
        LPWSTR raw = nullptr;
        if (FAILED(item->GetDisplayName(SIGDN_PARENTRELATIVEPARSING, &raw)) || !raw) return Title();
        std::wstring fileName = raw;
        CoTaskMemFree(raw);

        // 화면: "보고서"\ 에 풀기 (일집)
        return L"\"" + ShortenForMenu(FolderNameFor(fileName)) + L"\"\\ 에 풀기 (일집)";
    }

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
        // 압축 파일이 있는 폴더에 바로 푼다(반디집 '여기에 풀기')
        auto paths = GetSelectedPaths(items);
        for (const auto& p : paths)
        {
            LaunchIljip(L"--extract-here " + QuoteArg(p));
        }
        return S_OK;
    }

    HRESULT ExtractToFolderCommand::Execute(IShellItemArray* items)
    {
        // 고른 압축마다 '부모\이름' 폴더에 푼다(이미 있으면 Iljip.exe 가 '이름 (2)' 새 폴더)
        auto paths = GetSelectedPaths(items);
        for (const auto& p : paths)
        {
            LaunchIljip(L"--extract-to-folder " + QuoteArg(p));
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
CoCreatableClass(ExtractToFolderCommand);
CoCreatableClass(ExtractToCommand);
CoCreatableClass(CompressWithIljipCommand);
