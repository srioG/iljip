#include "pch.h"
#include "IljipCommands.h"

// 오른쪽 단추로 끌어 놓기 메뉴(I3-10)와 zip·7z 드롭 처리기(W2-12). 제목은 C# Iljip.Services.ShellMenu.DragCompressTitle·DragExtractTitle 과 같다.
// 끌어 놓기 메뉴는 매니페스트 desktop9 'windows.fileExplorerClassicDragDropContextMenuHandler'(폴더·드라이브)로,
// 드롭 처리기는 고전 레지스트리(C# FileAssociationRegistrar — Iljip.zip\shellex\DropHandler)로만 등록된다.

namespace IljipShell
{
    namespace
    {
        constexpr UINT CmdCompressHere = 0;   // '일집: 여기에 압축'
        constexpr UINT CmdExtractHere = 1;    // '일집: 여기에 풀기'

        const wchar_t* const DragCompressTitle = L"일집: 여기에 압축";
        const wchar_t* const DragExtractTitle = L"일집: 여기에 풀기";

        const char* const VerbCompressHereA = "IljipCompressHere";
        const char* const VerbExtractHereA = "IljipExtractHere";

        std::wstring PathFromPidl(PCIDLIST_ABSOLUTE pidl)
        {
            std::vector<wchar_t> buffer(32768);
            if (!SHGetPathFromIDListEx(pidl, buffer.data(), static_cast<DWORD>(buffer.size()), GPFIDL_DEFAULT)) return std::wstring();
            return std::wstring(buffer.data());
        }

        /// <summary>일집이 고칠 수 있는 압축(W2-06 '--add-to' — ZIP·7Z)인가(이름만).</summary>
        bool IsEditableArchive(const std::wstring& path)
        {
            const size_t dot = path.find_last_of(L'.');
            if (dot == std::wstring::npos) return false;
            std::wstring ext = path.substr(dot);
            for (auto& c : ext) c = static_cast<wchar_t>(towlower(c));
            return ext == L".zip" || ext == L".7z";
        }

        bool HasFileDrop(IDataObject* data)
        {
            if (!data) return false;
            FORMATETC format = { CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL };
            return data->QueryGetData(&format) == S_OK;
        }
    }

    // ===== 오른쪽 끌기 메뉴 =====

    IFACEMETHODIMP IljipDragDropHandler::Initialize(PCIDLIST_ABSOLUTE folder, IDataObject* data, HKEY) noexcept
    {
        try
        {
            m_target.clear();
            m_sources.clear();
            m_allArchives = false;
            if (!folder || !data) return E_INVALIDARG;
            m_target = PathFromPidl(folder);
            m_sources = DropFiles(data);
            if (m_target.empty() || m_sources.empty()) return E_FAIL;
            m_allArchives = true;
            for (const auto& s : m_sources)
            {
                const DWORD attributes = GetFileAttributesW(s.c_str());
                const bool isFolder = attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
                if (isFolder || !IsArchiveName(s)) { m_allArchives = false; break; }
            }
            return S_OK;
        }
        catch (...)
        {
            return E_OUTOFMEMORY;
        }
    }

    IFACEMETHODIMP IljipDragDropHandler::QueryContextMenu(HMENU menu, UINT index, UINT firstId, UINT lastId, UINT flags) noexcept
    {
        if ((flags & CMF_DEFAULTONLY) || m_target.empty() || m_sources.empty() || firstId + CmdExtractHere > lastId)
            return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, 0);
        UINT position = index;
        // 끈 것이 모두 압축 파일이면 '풀기'를 먼저(그때 더 바랄 일), 압축은 늘
        if (m_allArchives)
            InsertMenuW(menu, position++, MF_BYPOSITION | MF_STRING, firstId + CmdExtractHere, DragExtractTitle);
        InsertMenuW(menu, position++, MF_BYPOSITION | MF_STRING, firstId + CmdCompressHere, DragCompressTitle);
        return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, (m_allArchives ? CmdExtractHere : CmdCompressHere) + 1);
    }

    IFACEMETHODIMP IljipDragDropHandler::InvokeCommand(CMINVOKECOMMANDINFO* info) noexcept
    {
        if (!info) return E_INVALIDARG;
        try
        {
            UINT command;
            if (IS_INTRESOURCE(info->lpVerb))
                command = LOWORD(reinterpret_cast<UINT_PTR>(info->lpVerb));
            else if (lstrcmpiA(info->lpVerb, VerbCompressHereA) == 0)
                command = CmdCompressHere;
            else if (lstrcmpiA(info->lpVerb, VerbExtractHereA) == 0)
                command = CmdExtractHere;
            else
                return E_INVALIDARG;

            if (command == CmdCompressHere)
                return LaunchIljipCommand(L"--compress-to", { m_target }, m_sources);          // 만들 곳을 그 폴더로 정한 압축 창
            if (command == CmdExtractHere && m_allArchives)
                return LaunchIljipCommand(L"--extract-to-folder-in", { m_target }, m_sources); // 그 폴더 안 '이름' 폴더마다
            return E_INVALIDARG;
        }
        catch (...)
        {
            return E_FAIL;
        }
    }

    IFACEMETHODIMP IljipDragDropHandler::GetCommandString(UINT_PTR id, UINT type, UINT*, CHAR* name, UINT nameSize) noexcept
    {
        if (id != CmdCompressHere && !(id == CmdExtractHere && m_allArchives)) return E_INVALIDARG;
        switch (type)
        {
        case GCS_VERBA:
            return StringCchCopyA(name, nameSize, id == CmdCompressHere ? VerbCompressHereA : VerbExtractHereA);
        case GCS_VERBW:
            return StringCchCopyW(reinterpret_cast<LPWSTR>(name), nameSize, id == CmdCompressHere ? L"IljipCompressHere" : L"IljipExtractHere");
        case GCS_VALIDATEA:
        case GCS_VALIDATEW:
            return S_OK;
        default:
            return E_NOTIMPL;
        }
    }

    // ===== zip·7z 드롭 처리기 =====

    IFACEMETHODIMP IljipDropHandler::GetClassID(CLSID* clsid) noexcept
    {
        if (!clsid) return E_POINTER;
        *clsid = CLSID_IljipDropHandler;
        return S_OK;
    }

    IFACEMETHODIMP IljipDropHandler::IsDirty() noexcept { return S_FALSE; }

    IFACEMETHODIMP IljipDropHandler::Load(LPCOLESTR fileName, DWORD) noexcept
    {
        try
        {
            m_archive = fileName ? fileName : L"";
            return S_OK;
        }
        catch (...)
        {
            return E_OUTOFMEMORY;
        }
    }

    IFACEMETHODIMP IljipDropHandler::Save(LPCOLESTR, BOOL) noexcept { return E_NOTIMPL; }

    IFACEMETHODIMP IljipDropHandler::SaveCompleted(LPCOLESTR) noexcept { return E_NOTIMPL; }

    IFACEMETHODIMP IljipDropHandler::GetCurFile(LPOLESTR* fileName) noexcept
    {
        if (!fileName) return E_POINTER;
        return SHStrDupW(m_archive.c_str(), fileName);
    }

    DWORD IljipDropHandler::EffectFor(DWORD allowed) const noexcept
    {
        // 원본은 그대로 두고 압축에 복사해 넣는다(옮기기·바로 가기는 하지 않음)
        return m_canDrop && (allowed & DROPEFFECT_COPY) ? DROPEFFECT_COPY : DROPEFFECT_NONE;
    }

    IFACEMETHODIMP IljipDropHandler::DragEnter(IDataObject* data, DWORD, POINTL, DWORD* effect) noexcept
    {
        if (!effect) return E_INVALIDARG;
        m_canDrop = IsEditableArchive(m_archive) && HasFileDrop(data);
        *effect = EffectFor(*effect);
        return S_OK;
    }

    IFACEMETHODIMP IljipDropHandler::DragOver(DWORD, POINTL, DWORD* effect) noexcept
    {
        if (!effect) return E_INVALIDARG;
        *effect = EffectFor(*effect);
        return S_OK;
    }

    IFACEMETHODIMP IljipDropHandler::DragLeave() noexcept
    {
        m_canDrop = false;
        return S_OK;
    }

    IFACEMETHODIMP IljipDropHandler::Drop(IDataObject* data, DWORD, POINTL, DWORD* effect) noexcept
    {
        if (!effect) return E_INVALIDARG;
        try
        {
            const DWORD allowed = *effect;
            *effect = DROPEFFECT_NONE;
            if (!IsEditableArchive(m_archive)) return S_OK;
            auto files = DropFiles(data);
            // 압축 파일 자신을 자기에게 넣지 않는다
            files.erase(std::remove_if(files.begin(), files.end(),
                            [this](const std::wstring& f) { return lstrcmpiW(f.c_str(), m_archive.c_str()) == 0; }),
                        files.end());
            if (files.empty() || !(allowed & DROPEFFECT_COPY)) return S_OK;
            HRESULT hr = LaunchIljipCommand(L"--add-to", { m_archive }, files);   // W2-06: 창 없이 넣기(진행 창만)
            if (SUCCEEDED(hr)) *effect = DROPEFFECT_COPY;
            return hr;
        }
        catch (...)
        {
            return E_FAIL;
        }
    }
}

using namespace IljipShell;

CoCreatableClass(IljipDragDropHandler);
CoCreatableClass(IljipDropHandler);
