#include "pch.h"
#include "IljipCommands.h"

#include <cwctype>

// '일집 ▶' 하위 메뉴(W2-12, I1-2). 규칙(어떤 하위 메뉴·제목·동사)은 C# Iljip.Services.ShellMenu 와 같다 — 한쪽을 고치면 다른 쪽도 고칠 것.
// 빌드된 DLL 이 있으면 ShellExtensionContractTests 가 실제 GetTitle·EnumSubCommands 를 C# 규칙과 맞춰 본다(Invoke 는 부르지 않음).
// 셸 메뉴 단계에서는 확장자와 '폴더인가'만 본다(파일 내용을 읽지 않음).

namespace IljipShell
{
    namespace
    {
        wchar_t AsciiLower(wchar_t c) noexcept { return (c >= L'A' && c <= L'Z') ? static_cast<wchar_t>(c - L'A' + L'a') : c; }

        bool EqualsNoCase(const std::wstring& a, const wchar_t* b)
        {
            const size_t n = wcslen(b);
            if (a.size() != n) return false;
            for (size_t i = 0; i < n; ++i)
                if (AsciiLower(a[i]) != AsciiLower(b[i])) return false;
            return true;
        }

        bool IsBlank(const std::wstring& s)
        {
            for (wchar_t c : s)
                if (!iswspace(c)) return false;
            return true;
        }

        /// <summary>경로의 마지막 부분(파일·폴더 이름).</summary>
        std::wstring FileNameOf(const std::wstring& path)
        {
            const size_t slash = path.find_last_of(L"\\/");
            return slash == std::wstring::npos ? path : path.substr(slash + 1);
        }

        /// <summary>C# Path.GetDirectoryName 과 같게: 'C:\a\b.txt' → 'C:\a', 'C:\b.txt' → 'C:\', '\\srv\share\x' → '\\srv\share'.</summary>
        std::wstring DirectoryOf(const std::wstring& path)
        {
            const size_t slash = path.find_last_of(L"\\/");
            if (slash == std::wstring::npos) return std::wstring();
            std::wstring dir = path.substr(0, slash);
            if (dir.size() == 2 && dir[1] == L':') dir += L'\\';   // 드라이브 뿌리
            return dir;
        }

        /// <summary>C# new DirectoryInfo(dir).Name 과 같게: 뿌리('C:\'·'\\srv\share')는 그대로, 아니면 마지막 부분.</summary>
        std::wstring DirectoryNameOf(const std::wstring& dir)
        {
            if (dir.empty()) return dir;
            if (PathCchIsRoot(dir.c_str())) return dir;
            return FileNameOf(dir);
        }

        /// <summary>C# CompressNaming.Sanitize 와 같게: 못 쓰는 글자를 빼고 앞뒤 공백·끝 점을 지움, 비면 'archive'.</summary>
        std::wstring SanitizeName(const std::wstring& raw)
        {
            if (IsBlank(raw)) return L"archive";
            std::wstring name;
            for (wchar_t c : raw)
                if (c >= 32 && !wcschr(L"\"<>|:*?\\/", c)) name.push_back(c);
            auto trim = [](std::wstring& s)
            {
                while (!s.empty() && iswspace(s.back())) s.pop_back();
                size_t i = 0;
                while (i < s.size() && iswspace(s[i])) ++i;
                s.erase(0, i);
            };
            trim(name);
            while (!name.empty() && name.back() == L'.') name.pop_back();
            trim(name);
            return IsBlank(name) ? std::wstring(L"archive") : name;
        }

        /// <summary>C# Path.GetFileNameWithoutExtension 과 같게(마지막 점부터 뗌 — '.gitignore' 는 빈 문자열).</summary>
        std::wstring WithoutExtension(const std::wstring& fileName)
        {
            const size_t dot = fileName.find_last_of(L'.');
            return dot == std::wstring::npos ? fileName : fileName.substr(0, dot);
        }

        /// <summary>압축 파일로 보는 끝 확장자 — C# ShellMenu.ArchiveExtensions 와 같은 차례.</summary>
        const wchar_t* const ArchiveExtensions[] =
        {
            L".zip", L".7z", L".rar", L".tar", L".gz", L".bz2", L".xz", L".tgz", L".tbz2", L".tbz", L".txz",
            L".zipx", L".001", L".iso", L".cab", L".lzh", L".lha", L".wim", L".egg", L".alz",
        };
    }

    std::vector<SelectedItem> SelectedItems(IShellItemArray* items)
    {
        std::vector<SelectedItem> result;
        for (auto& path : SelectedPaths(items))
        {
            const DWORD attributes = GetFileAttributesW(path.c_str());
            SelectedItem item;
            item.isFolder = attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
            item.path = std::move(path);
            result.push_back(std::move(item));
        }
        return result;
    }

    bool IsArchiveName(const std::wstring& path)
    {
        const std::wstring name = FileNameOf(path);
        const size_t dot = name.find_last_of(L'.');
        if (dot == std::wstring::npos) return false;
        const std::wstring ext = name.substr(dot);
        for (const wchar_t* e : ArchiveExtensions)
            if (EqualsNoCase(ext, e)) return true;
        return false;
    }

    SelectionKind Classify(const std::vector<SelectedItem>& items)
    {
        if (items.empty()) return SelectionKind::None;
        bool allArchives = true;
        for (const auto& i : items)
            if (i.isFolder || !IsArchiveName(i.path)) { allArchives = false; break; }
        if (allArchives) return items.size() == 1 ? SelectionKind::OneArchive : SelectionKind::ManyArchives;
        return SelectionKind::FilesOrFolders;
    }

    std::wstring CompressBaseName(const std::vector<SelectedItem>& items)
    {
        if (items.empty()) return L"archive";
        if (items.size() == 1)
        {
            std::wstring trimmed = items[0].path;
            while (!trimmed.empty() && (trimmed.back() == L'\\' || trimmed.back() == L'/')) trimmed.pop_back();
            const std::wstring fileName = FileNameOf(trimmed);
            std::wstring name;
            if (items[0].isFolder)
                name = fileName;
            else
            {
                name = WithoutExtension(fileName);
                if (IsBlank(name)) name = fileName;
            }
            if (IsBlank(name)) name = items[0].path;   // 드라이브 뿌리 등(C# DirectoryInfo.Name)
            return SanitizeName(name);
        }
        return SanitizeName(DirectoryNameOf(DirectoryOf(items[0].path)));
    }

    std::vector<SubCommandId> SubCommandsFor(SelectionKind kind)
    {
        switch (kind)
        {
        case SelectionKind::OneArchive:
            return { SubCommandId::Open, SubCommandId::ExtractHere, SubCommandId::ExtractToFolder, SubCommandId::ExtractSmart, SubCommandId::ExtractTo };
        case SelectionKind::ManyArchives:
            return { SubCommandId::ExtractMany, SubCommandId::ExtractSmartMany, SubCommandId::ExtractToFolderMany };
        case SelectionKind::FilesOrFolders:
            return { SubCommandId::Compress, SubCommandId::CompressZip, SubCommandId::Compress7z };
        default:
            return {};
        }
    }

    std::wstring SubCommandTitle(SubCommandId id, const std::vector<SelectedItem>& items)
    {
        switch (id)
        {
        case SubCommandId::Open: return L"일집으로 열기";
        case SubCommandId::ExtractHere: return L"여기에 풀기";
        case SubCommandId::ExtractToFolder:
            // 화면: "보고서"\ 에 풀기 — 실제 폴더 이름과 같은 규칙(FolderNameFor)
            return items.empty() ? std::wstring(L"이름 폴더에 풀기")
                                 : L"\"" + ShortenForMenu(FolderNameFor(FileNameOf(items[0].path))) + L"\"\\ 에 풀기";
        case SubCommandId::ExtractSmart: return L"알아서 풀기";
        case SubCommandId::ExtractTo: return L"다른 곳에 풀기...";
        case SubCommandId::ExtractMany: return L"압축 풀기...";
        case SubCommandId::ExtractSmartMany: return L"각각 알아서 풀기";
        case SubCommandId::ExtractToFolderMany: return L"각각 이름 폴더에 풀기";
        case SubCommandId::Compress: return L"압축하기...";
        case SubCommandId::CompressZip: return L"\"" + ShortenForMenu(CompressBaseName(items)) + L".zip\"으로 압축";
        case SubCommandId::Compress7z: return L"\"" + ShortenForMenu(CompressBaseName(items)) + L".7z\"로 압축";
        }
        return std::wstring();
    }

    const wchar_t* SubCommandVerb(SubCommandId id)
    {
        switch (id)
        {
        case SubCommandId::Open: return L"--open";
        case SubCommandId::ExtractHere: return L"--extract-here";
        case SubCommandId::ExtractToFolder:
        case SubCommandId::ExtractToFolderMany: return L"--extract-to-folder";
        case SubCommandId::ExtractSmart:
        case SubCommandId::ExtractSmartMany: return L"--extract-smart";
        case SubCommandId::ExtractTo:
        case SubCommandId::ExtractMany: return L"--extract";
        case SubCommandId::Compress: return L"--compress";
        case SubCommandId::CompressZip: return L"--compress-zip";
        case SubCommandId::Compress7z: return L"--compress-7z";
        }
        return L"";
    }

    HRESULT RunSubCommand(SubCommandId id, const std::vector<SelectedItem>& items)
    {
        if (items.empty()) return S_OK;
        std::vector<std::wstring> paths;
        if (id == SubCommandId::Open)
            paths.push_back(items[0].path);   // 여는 것은 하나
        else
            for (const auto& i : items) paths.push_back(i.path);
        // 고른 것을 모두 한 명령줄로(예전엔 파일마다 Iljip.exe 를 띄웠다 — 이제 '--extract a b c' 하나로 풀기 창 한 번·묶음 풀기)
        return LaunchIljipCommand(SubCommandVerb(id), {}, paths);
    }

    // ===== 루트 '일집 ▶' =====

    IFACEMETHODIMP IljipRootCommand::GetTitle(IShellItemArray*, LPWSTR* title) noexcept
    {
        return SHStrDupW(L"일집", title);
    }

    IFACEMETHODIMP IljipRootCommand::GetIcon(IShellItemArray*, LPWSTR* icon) noexcept
    {
        try
        {
            // 일집 아이콘 파일(설치 폴더\icons\iljip.ico — exe 리소스가 아니라 파일), 없으면 Iljip.exe 의 첫 아이콘
            std::wstring ico = ModuleFolder() + L"\\icons\\iljip.ico";
            if (GetFileAttributesW(ico.c_str()) == INVALID_FILE_ATTRIBUTES)
                ico = IljipExePath() + L",0";
            return SHStrDupW(ico.c_str(), icon);
        }
        catch (...)
        {
            *icon = nullptr;
            return E_OUTOFMEMORY;
        }
    }

    IFACEMETHODIMP IljipRootCommand::GetToolTip(IShellItemArray*, LPWSTR* tooltip) noexcept
    {
        *tooltip = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP IljipRootCommand::GetCanonicalName(GUID* name) noexcept
    {
        *name = CLSID_IljipRoot;
        return S_OK;
    }

    IFACEMETHODIMP IljipRootCommand::GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) noexcept
    {
        try
        {
            m_items = SelectedItems(items);
            m_known = true;
            *state = SubCommandsFor(Classify(m_items)).empty() ? ECS_HIDDEN : ECS_ENABLED;
            return S_OK;
        }
        catch (...)
        {
            *state = ECS_HIDDEN;
            return S_OK;
        }
    }

    IFACEMETHODIMP IljipRootCommand::GetFlags(EXPCMDFLAGS* flags) noexcept
    {
        *flags = ECF_HASSUBCOMMANDS;
        return S_OK;
    }

    IFACEMETHODIMP IljipRootCommand::EnumSubCommands(IEnumExplorerCommand** commands) noexcept
    {
        if (!commands) return E_POINTER;
        *commands = nullptr;
        try
        {
            std::vector<SubCommandId> ids;
            if (m_known)
                ids = SubCommandsFor(Classify(m_items));
            else
                for (auto kind : { SelectionKind::OneArchive, SelectionKind::ManyArchives, SelectionKind::FilesOrFolders })
                    for (auto id : SubCommandsFor(kind)) ids.push_back(id);   // 고른 것을 모름 — 다 주고 각자 GetState 에서 숨음

            std::vector<Microsoft::WRL::ComPtr<IExplorerCommand>> list;
            for (auto id : ids)
            {
                Microsoft::WRL::ComPtr<IljipSubCommand> sub;
                HRESULT hr = Microsoft::WRL::MakeAndInitialize<IljipSubCommand>(&sub, id, m_items);
                if (FAILED(hr)) return hr;
                list.push_back(sub);
            }
            Microsoft::WRL::ComPtr<SubCommandEnumerator> enumerator;
            HRESULT hr = Microsoft::WRL::MakeAndInitialize<SubCommandEnumerator>(&enumerator, list, 0);
            if (FAILED(hr)) return hr;
            *commands = enumerator.Detach();
            return S_OK;
        }
        catch (...)
        {
            return E_OUTOFMEMORY;
        }
    }

    IFACEMETHODIMP IljipRootCommand::Invoke(IShellItemArray*, IBindCtx*) noexcept
    {
        return S_OK;   // 하위 메뉴가 있는 항목은 직접 실행하지 않는다
    }

    // ===== 하위 메뉴 =====

    HRESULT IljipSubCommand::RuntimeClassInitialize(SubCommandId id, const std::vector<SelectedItem>& snapshot)
    {
        try
        {
            m_id = id;
            m_snapshot = snapshot;
            return S_OK;
        }
        catch (...)
        {
            return E_OUTOFMEMORY;
        }
    }

    std::vector<SelectedItem> IljipSubCommand::ItemsOrSnapshot(IShellItemArray* items) const
    {
        // 하위 메뉴가 항목 없이 불리는 경우(Windows 판에 따라)가 있어 루트가 본 것을 쓴다
        auto selected = SelectedItems(items);
        return selected.empty() ? m_snapshot : selected;
    }

    IFACEMETHODIMP IljipSubCommand::GetTitle(IShellItemArray* items, LPWSTR* title) noexcept
    {
        try
        {
            return SHStrDupW(SubCommandTitle(m_id, ItemsOrSnapshot(items)).c_str(), title);
        }
        catch (...)
        {
            *title = nullptr;
            return E_OUTOFMEMORY;
        }
    }

    IFACEMETHODIMP IljipSubCommand::GetIcon(IShellItemArray*, LPWSTR* icon) noexcept
    {
        *icon = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP IljipSubCommand::GetToolTip(IShellItemArray*, LPWSTR* tooltip) noexcept
    {
        *tooltip = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP IljipSubCommand::GetCanonicalName(GUID* name) noexcept
    {
        *name = GUID_NULL;
        return S_OK;
    }

    IFACEMETHODIMP IljipSubCommand::GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) noexcept
    {
        try
        {
            const auto ids = SubCommandsFor(Classify(ItemsOrSnapshot(items)));
            *state = std::find(ids.begin(), ids.end(), m_id) != ids.end() ? ECS_ENABLED : ECS_HIDDEN;
            return S_OK;
        }
        catch (...)
        {
            *state = ECS_HIDDEN;
            return S_OK;
        }
    }

    IFACEMETHODIMP IljipSubCommand::GetFlags(EXPCMDFLAGS* flags) noexcept
    {
        *flags = ECF_DEFAULT;
        return S_OK;
    }

    IFACEMETHODIMP IljipSubCommand::EnumSubCommands(IEnumExplorerCommand** commands) noexcept
    {
        if (commands) *commands = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP IljipSubCommand::Invoke(IShellItemArray* items, IBindCtx*) noexcept
    {
        try
        {
            return RunSubCommand(m_id, ItemsOrSnapshot(items));
        }
        catch (...)
        {
            return E_FAIL;
        }
    }

    // ===== 하위 메뉴 목록 =====

    HRESULT SubCommandEnumerator::RuntimeClassInitialize(const std::vector<Microsoft::WRL::ComPtr<IExplorerCommand>>& commands, size_t position)
    {
        try
        {
            m_commands = commands;
            m_position = position;
            return S_OK;
        }
        catch (...)
        {
            return E_OUTOFMEMORY;
        }
    }

    IFACEMETHODIMP SubCommandEnumerator::Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) noexcept
    {
        if (!commands) return E_POINTER;
        ULONG done = 0;
        while (done < count && m_position < m_commands.size())
        {
            m_commands[m_position].CopyTo(&commands[done]);
            ++m_position;
            ++done;
        }
        if (fetched) *fetched = done;
        return done == count ? S_OK : S_FALSE;
    }

    IFACEMETHODIMP SubCommandEnumerator::Skip(ULONG count) noexcept
    {
        const size_t left = m_commands.size() - m_position;
        m_position += (count < left ? count : left);
        return count <= left ? S_OK : S_FALSE;
    }

    IFACEMETHODIMP SubCommandEnumerator::Reset() noexcept
    {
        m_position = 0;
        return S_OK;
    }

    IFACEMETHODIMP SubCommandEnumerator::Clone(IEnumExplorerCommand** copy) noexcept
    {
        if (!copy) return E_POINTER;
        *copy = nullptr;
        Microsoft::WRL::ComPtr<SubCommandEnumerator> clone;
        HRESULT hr = Microsoft::WRL::MakeAndInitialize<SubCommandEnumerator>(&clone, m_commands, m_position);
        if (FAILED(hr)) return hr;
        *copy = clone.Detach();
        return S_OK;
    }

    // ===== 폴더 안 빈 곳 '일집 열기' =====

    HRESULT IljipBackgroundCommand::Execute(IShellItemArray*)
    {
        return LaunchIljipAlone();
    }
}

// COM 클래스 등록(IljipCommands.cpp 의 주석과 같은 까닭으로 using namespace)
using namespace IljipShell;

CoCreatableClass(IljipRootCommand);
CoCreatableClass(IljipBackgroundCommand);
