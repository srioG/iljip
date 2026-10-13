#pragma once
#include "pch.h"

namespace IljipShell
{
    // 각 컨텍스트 메뉴 항목에 대한 CLSID.
    // MSIX manifest의 com:Class @Id 값과 일치해야 한다(Iljip.Tests ShellExtensionContractTests 의 ExpectedClsids 표가 대조).

    // ===== 옛 메뉴 5개(W2-12 전) — 매니페스트 메뉴에서는 뺐지만 DLL 에는 남긴다(업그레이드 중 옛 등록이 이 DLL 을 불러도 깨지지 않게) =====

    // .zip/.7z 등 우클릭 → "일집으로 열기"
    constexpr GUID CLSID_OpenWithIljip       = { 0xB3E8A7D5, 0x1C9F, 0x4E2A, { 0xB6, 0xA1, 0xD3, 0x9C, 0x8F, 0x2E, 0x5B, 0x71 } };
    // .zip/.7z 등 우클릭 → "여기에 풀기 (일집)" — 압축 파일이 있는 폴더에 바로(--extract-here). W1-D 전 제목은 '여기에 압축 풀기'였고 실제로는 이름 폴더에 풀었다.
    constexpr GUID CLSID_ExtractHere         = { 0x2A4F7C91, 0x8E6D, 0x4B3A, { 0x9C, 0x52, 0xF1, 0xD8, 0xE9, 0xA7, 0xB3, 0xC4 } };
    // .zip/.7z 등 우클릭 → "\"이름\"\ 에 풀기 (일집)" — '부모\이름' 폴더에(--extract-to-folder, 이미 있으면 '이름 (2)')
    constexpr GUID CLSID_ExtractToFolder     = { 0xF5964E4F, 0x2EC5, 0x4DC7, { 0xBA, 0x37, 0xC0, 0x4A, 0xD4, 0xDB, 0x45, 0x6F } };
    // .zip/.7z 등 우클릭 → "압축 풀기... (일집)"
    constexpr GUID CLSID_ExtractTo           = { 0x7D3E9C2A, 0x5B1F, 0x4A8C, { 0x93, 0xE6, 0xC2, 0xA8, 0xF5, 0xD7, 0xB1, 0xE9 } };
    // 일반 파일/폴더 우클릭 → "일집으로 압축"
    constexpr GUID CLSID_CompressWithIljip   = { 0xF9E2C1A8, 0x3D7B, 0x4E5A, { 0x91, 0xC8, 0xB5, 0xF7, 0xD2, 0xE9, 0xA3, 0xC6 } };

    // ===== W2-12(I1-2·I3-10) =====

    // 파일·폴더 우클릭 → '일집 ▶' 하나(하위 메뉴는 고른 것에 따라 — 압축 하나/여럿/일반 파일·폴더). 매니페스트 ItemType '*'·'Directory'
    constexpr GUID CLSID_IljipRoot           = { 0xC0EE1823, 0xADD5, 0x4327, { 0x92, 0xD8, 0xF9, 0x40, 0x14, 0x2A, 0xD9, 0x13 } };
    // 폴더 안 빈 곳 우클릭(Directory\Background) → '일집 열기'(빈 곳과 폴더 자체는 IShellItemArray 로 구별되지 않아 따로 둔다)
    constexpr GUID CLSID_IljipBackground     = { 0xA0714267, 0x8EDA, 0x4F03, { 0x8E, 0x4D, 0x41, 0x1F, 0xBB, 0x97, 0x93, 0x91 } };
    // 오른쪽 단추로 끌어 놓기 메뉴(고전 IContextMenu) → '일집: 여기에 압축'·'일집: 여기에 풀기'. 매니페스트 desktop9 fileExplorerClassicDragDropContextMenuHandler
    constexpr GUID CLSID_IljipDragDrop       = { 0x1AE50C95, 0x17B6, 0x40F9, { 0x91, 0x8B, 0x69, 0x09, 0x94, 0x28, 0xFF, 0x2A } };
    // zip·7z 아이콘 위에 끌어 놓아 넣기(드롭 처리기 — IPersistFile + IDropTarget). 매니페스트로는 선언할 수 없어 C# FileAssociationRegistrar 가
    // HKCU\Software\Classes\Iljip.zip\shellex\DropHandler 와 CLSID\{…}\InprocServer32 로 등록한다(선택 — ☰ ▸ 설정 '파일 연결')
    constexpr GUID CLSID_IljipDropHandler    = { 0x61C3F459, 0xE40B, 0x441D, { 0x9F, 0x76, 0xF5, 0x6C, 0x70, 0xDF, 0xF8, 0xC1 } };

    /// <summary>
    /// '"이름"\ 에 풀기'가 만들 폴더 이름 — C# Iljip.Services.ArchiveNaming.FolderNameFor 와 같은 규칙(메뉴 제목 = 실제 폴더 이름).
    /// 파일 이름 문자열만 다룬다(셸 메뉴 단계에서 파일 내용을 읽지 않는다). 한쪽을 고치면 다른 쪽도 고칠 것.
    /// </summary>
    std::wstring FolderNameFor(const std::wstring& fileName);

    /// <summary>메뉴 제목에 넣을 이름 줄이기(40자를 넘으면 39자 + '…', 서로게이트 쌍은 자르지 않음).</summary>
    std::wstring ShortenForMenu(const std::wstring& name);

    // ===== Iljip.exe 부르기(IljipLaunch.cpp) — C# Iljip.Services.ShellCommandLine 이 같은 규칙의 거울(계약 시험이 맞춰 봄) =====

    /// <summary>명령줄 전체(실행 파일 포함) 최대 글자 수 — 넘으면 경로들을 목록 파일로(C# ShellCommandLine.MaxChars 와 같은 값).</summary>
    constexpr size_t MaxCommandLineChars = 30000;

    /// <summary>이 DLL 이 있는 폴더(끝 '\' 없음).</summary>
    std::wstring ModuleFolder();

    /// <summary>같은 폴더의 Iljip.exe 절대 경로.</summary>
    std::wstring IljipExePath();

    /// <summary>인자 하나를 늘 큰따옴표로 싼다 — 닫는 따옴표 바로 앞의 역슬래시는 두 배('C:\' 가 다음 인자를 먹지 않게), 안의 '"' 는 '\"'.</summary>
    std::wstring QuoteCommandArg(const std::wstring& arg);

    /// <summary>
    /// "Iljip.exe" 동사 ["앞 인자"…] "경로"… 로 실행(비동기, 결과 무시). 길이가 <see cref="MaxCommandLineChars"/> 를 넘으면
    /// 경로들을 %LOCALAPPDATA%\Iljip\temp\args\&lt;guid&gt;.lst(UTF-8, 한 줄에 하나)에 쓰고 '--list-file "그 파일"' 로 넘긴다(일집이 읽고 지움).
    /// </summary>
    HRESULT LaunchIljipCommand(const std::wstring& verb, const std::vector<std::wstring>& leading, const std::vector<std::wstring>& paths);

    /// <summary>"Iljip.exe" 만(인자 없이 — 일집 창 열기).</summary>
    HRESULT LaunchIljipAlone();

    /// <summary>긴 목록 파일을 두는 폴더 %LOCALAPPDATA%\Iljip\temp\args(못 구하면 빈 문자열).</summary>
    std::wstring ListFileFolder();

    /// <summary>경로 목록 파일을 만든다(UTF-8 BOM 없음, 줄 끝 CRLF). 만든 파일 경로, 못 만들면 빈 문자열.</summary>
    std::wstring WriteListFile(const std::vector<std::wstring>& paths);

    /// <summary>IShellItemArray 의 파일 시스템 경로 전부.</summary>
    std::vector<std::wstring> SelectedPaths(IShellItemArray* items);

    /// <summary>끌어 놓은 데이터의 CF_HDROP 경로 전부(없으면 빈 목록).</summary>
    std::vector<std::wstring> DropFiles(IDataObject* data);

    // ===== '일집 ▶' 하위 메뉴 규칙(IljipMenu.cpp) — C# Iljip.Services.ShellMenu 와 같은 규칙 =====

    /// <summary>고른 것 하나(경로, 폴더인가 — GetFileAttributesW, 파일 내용은 읽지 않음).</summary>
    struct SelectedItem
    {
        std::wstring path;
        bool isFolder = false;
    };

    /// <summary>고른 것의 종류(C# ShellSelectionKind 와 같은 차례).</summary>
    enum class SelectionKind { None, OneArchive, ManyArchives, FilesOrFolders };

    /// <summary>하위 메뉴(C# ShellMenu.ItemsFor 의 Id 와 짝).</summary>
    enum class SubCommandId
    {
        Open, ExtractHere, ExtractToFolder, ExtractSmart, ExtractTo,        // 압축 하나
        ExtractMany, ExtractSmartMany, ExtractToFolderMany,                 // 압축 여럿
        Compress, CompressZip, Compress7z,                                  // 파일·폴더
    };

    std::vector<SelectedItem> SelectedItems(IShellItemArray* items);

    /// <summary>이름(끝 확장자)만 보고 압축 파일인가 — C# ShellMenu.ArchiveExtensions 와 같은 목록.</summary>
    bool IsArchiveName(const std::wstring& path);

    SelectionKind Classify(const std::vector<SelectedItem>& items);

    /// <summary>'"이름.zip"으로 압축'의 이름 — C# CompressNaming.SuggestBaseName(창 없이 바로 압축이 실제로 쓰는 이름)과 같은 규칙.</summary>
    std::wstring CompressBaseName(const std::vector<SelectedItem>& items);

    std::vector<SubCommandId> SubCommandsFor(SelectionKind kind);

    std::wstring SubCommandTitle(SubCommandId id, const std::vector<SelectedItem>& items);

    /// <summary>Iljip.exe 에 넘기는 동사(C# CommandLineParser 키워드).</summary>
    const wchar_t* SubCommandVerb(SubCommandId id);

    HRESULT RunSubCommand(SubCommandId id, const std::vector<SelectedItem>& items);

    /// <summary>
    /// 셸 확장 베이스. CRTP로 각 명령 클래스가 Title(또는 고른 항목에 따라 TitleFor)을 제공하고,
    /// Invoke는 Iljip.exe를 적절한 명령줄로 실행한다.
    /// </summary>
    template <typename TDerived>
    class IljipCommandBase :
        public Microsoft::WRL::RuntimeClass<
            Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
            IExplorerCommand,
            IObjectWithSite>
    {
    public:
        // IExplorerCommand
        IFACEMETHODIMP GetTitle(IShellItemArray* items, LPWSTR* title) noexcept override
        {
            try
            {
                return SHStrDupW(static_cast<TDerived*>(this)->TitleFor(items).c_str(), title);
            }
            catch (...)
            {
                *title = nullptr;
                return E_OUTOFMEMORY;
            }
        }
        IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) noexcept override
        {
            *icon = nullptr;
            return E_NOTIMPL;
        }
        IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tooltip) noexcept override
        {
            *tooltip = nullptr;
            return E_NOTIMPL;
        }
        IFACEMETHODIMP GetCanonicalName(GUID* name) noexcept override
        {
            *name = GUID_NULL;
            return S_OK;
        }
        IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) noexcept override
        {
            *state = ECS_ENABLED;
            return S_OK;
        }
        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) noexcept override
        {
            *flags = ECF_DEFAULT;
            return S_OK;
        }
        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand**) noexcept override
        {
            return E_NOTIMPL;
        }
        IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) noexcept override
        {
            try
            {
                return static_cast<TDerived*>(this)->Execute(items);
            }
            catch (...) { return E_FAIL; }
        }

        // IObjectWithSite
        IFACEMETHODIMP SetSite(IUnknown* site) noexcept override { m_site = site; return S_OK; }
        IFACEMETHODIMP GetSite(REFIID riid, void** out) noexcept override
        {
            return m_site ? m_site->QueryInterface(riid, out) : E_FAIL;
        }

        /// <summary>고른 항목에 따라 제목을 바꾸는 명령은 이것을 다시 정의한다. 기본은 고정 제목.</summary>
        std::wstring TitleFor(IShellItemArray*)
        {
            return static_cast<TDerived*>(this)->Title();
        }

    protected:
        Microsoft::WRL::ComPtr<IUnknown> m_site;

        /// <summary>같은 폴더에 있는 Iljip.exe 절대 경로 계산.</summary>
        static std::wstring GetIljipExePath();

        /// <summary>IShellItemArray의 모든 항목 경로를 가져옴.</summary>
        static std::vector<std::wstring> GetSelectedPaths(IShellItemArray* items);

        /// <summary>Iljip.exe를 인자로 실행. 비동기, 결과 무시.</summary>
        static HRESULT LaunchIljip(const std::wstring& argLine);

        /// <summary>경로를 따옴표로 안전하게 감싸기(W2-12 — 끝 역슬래시는 두 배, QuoteCommandArg).</summary>
        static std::wstring QuoteArg(const std::wstring& path);
    };

    // ===== 구체 명령들 =====
    // 각 클래스에 __declspec(uuid(...))로 CLSID 부여 → WRL CoCreatableClass가 이걸 사용.

    class __declspec(uuid("B3E8A7D5-1C9F-4E2A-B6A1-D39C8F2E5B71"))
        OpenWithIljipCommand : public IljipCommandBase<OpenWithIljipCommand>
    {
    public:
        PCWSTR Title() noexcept { return L"일집으로 열기"; }
        HRESULT Execute(IShellItemArray* items);
    };

    class __declspec(uuid("2A4F7C91-8E6D-4B3A-9C52-F1D8E9A7B3C4"))
        ExtractHereCommand : public IljipCommandBase<ExtractHereCommand>
    {
    public:
        PCWSTR Title() noexcept { return L"여기에 풀기 (일집)"; }
        HRESULT Execute(IShellItemArray* items);
    };

    class __declspec(uuid("F5964E4F-2EC5-4DC7-BA37-C04AD4DB456F"))
        ExtractToFolderCommand : public IljipCommandBase<ExtractToFolderCommand>
    {
    public:
        /// <summary>이름을 못 알아낼 때(항목 없음 등)의 제목.</summary>
        PCWSTR Title() noexcept { return L"이름 폴더에 풀기 (일집)"; }
        /// <summary>하나: '"보고서"\ 에 풀기 (일집)', 여럿: '각각 이름 폴더에 풀기 (일집)'.</summary>
        std::wstring TitleFor(IShellItemArray* items);
        HRESULT Execute(IShellItemArray* items);
    };

    class __declspec(uuid("7D3E9C2A-5B1F-4A8C-93E6-C2A8F5D7B1E9"))
        ExtractToCommand : public IljipCommandBase<ExtractToCommand>
    {
    public:
        PCWSTR Title() noexcept { return L"압축 풀기... (일집)"; }
        HRESULT Execute(IShellItemArray* items);
    };

    class __declspec(uuid("F9E2C1A8-3D7B-4E5A-91C8-B5F7D2E9A3C6"))
        CompressWithIljipCommand : public IljipCommandBase<CompressWithIljipCommand>
    {
    public:
        PCWSTR Title() noexcept { return L"일집으로 압축"; }
        HRESULT Execute(IShellItemArray* items);
    };

    // ===== W2-12: '일집 ▶' 하나와 하위 메뉴(IljipMenu.cpp) =====

    /// <summary>
    /// 최상위 '일집 ▶'(ECF_HASSUBCOMMANDS). GetState 에서 고른 것을 보고(확장자·폴더인가만) 종류를 기억했다가 EnumSubCommands 가 그 종류의 하위 메뉴만 준다.
    /// GetState 없이 EnumSubCommands 가 불리면 모든 하위 메뉴를 주고, 하위 메뉴가 각자 GetState 에서 안 맞으면 숨는다(ECS_HIDDEN).
    /// 하위 메뉴는 이 DLL 안의 객체라 COM 등록이 필요 없다.
    /// </summary>
    class __declspec(uuid("C0EE1823-ADD5-4327-92D8-F940142AD913"))
        IljipRootCommand : public Microsoft::WRL::RuntimeClass<
            Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
            IExplorerCommand,
            IObjectWithSite>
    {
    public:
        IFACEMETHODIMP GetTitle(IShellItemArray* items, LPWSTR* title) noexcept override;
        IFACEMETHODIMP GetIcon(IShellItemArray* items, LPWSTR* icon) noexcept override;
        IFACEMETHODIMP GetToolTip(IShellItemArray* items, LPWSTR* tooltip) noexcept override;
        IFACEMETHODIMP GetCanonicalName(GUID* name) noexcept override;
        IFACEMETHODIMP GetState(IShellItemArray* items, BOOL okToBeSlow, EXPCMDSTATE* state) noexcept override;
        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) noexcept override;
        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) noexcept override;
        IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx* bind) noexcept override;

        IFACEMETHODIMP SetSite(IUnknown* site) noexcept override { m_site = site; return S_OK; }
        IFACEMETHODIMP GetSite(REFIID riid, void** out) noexcept override { return m_site ? m_site->QueryInterface(riid, out) : E_FAIL; }

    private:
        Microsoft::WRL::ComPtr<IUnknown> m_site;
        bool m_known = false;                       // GetState 로 고른 것을 보았나
        std::vector<SelectedItem> m_items;          // 그때 고른 것(하위 메뉴가 항목 없이 불릴 때 씀)
    };

    /// <summary>'일집 ▶' 아래 하위 메뉴 하나(COM 등록 없음 — 루트가 만들어 준다).</summary>
    class IljipSubCommand : public Microsoft::WRL::RuntimeClass<
        Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
        IExplorerCommand>
    {
    public:
        HRESULT RuntimeClassInitialize(SubCommandId id, const std::vector<SelectedItem>& snapshot);

        IFACEMETHODIMP GetTitle(IShellItemArray* items, LPWSTR* title) noexcept override;
        IFACEMETHODIMP GetIcon(IShellItemArray* items, LPWSTR* icon) noexcept override;
        IFACEMETHODIMP GetToolTip(IShellItemArray* items, LPWSTR* tooltip) noexcept override;
        IFACEMETHODIMP GetCanonicalName(GUID* name) noexcept override;
        IFACEMETHODIMP GetState(IShellItemArray* items, BOOL okToBeSlow, EXPCMDSTATE* state) noexcept override;
        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) noexcept override;
        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) noexcept override;
        IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx* bind) noexcept override;

    private:
        std::vector<SelectedItem> ItemsOrSnapshot(IShellItemArray* items) const;
        SubCommandId m_id = SubCommandId::Open;
        std::vector<SelectedItem> m_snapshot;
    };

    /// <summary>하위 메뉴 목록(IEnumExplorerCommand).</summary>
    class SubCommandEnumerator : public Microsoft::WRL::RuntimeClass<
        Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
        IEnumExplorerCommand>
    {
    public:
        HRESULT RuntimeClassInitialize(const std::vector<Microsoft::WRL::ComPtr<IExplorerCommand>>& commands, size_t position = 0);

        IFACEMETHODIMP Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) noexcept override;
        IFACEMETHODIMP Skip(ULONG count) noexcept override;
        IFACEMETHODIMP Reset() noexcept override;
        IFACEMETHODIMP Clone(IEnumExplorerCommand** copy) noexcept override;

    private:
        std::vector<Microsoft::WRL::ComPtr<IExplorerCommand>> m_commands;
        size_t m_position = 0;
    };

    /// <summary>폴더 안 빈 곳(Directory\Background) '일집 열기' — 인자 없이 일집 창(떠 있으면 그 창을 앞으로).</summary>
    class __declspec(uuid("A0714267-8EDA-4F03-8E4D-411FBB979391"))
        IljipBackgroundCommand : public IljipCommandBase<IljipBackgroundCommand>
    {
    public:
        PCWSTR Title() noexcept { return L"일집 열기"; }
        HRESULT Execute(IShellItemArray* items);
    };

    // ===== W2-12(I3-10): 오른쪽 끌기 메뉴와 드롭 처리기(IljipDragDrop.cpp) =====

    /// <summary>
    /// 오른쪽 단추로 파일을 폴더에 끌어 놓았을 때의 메뉴(고전 IShellExtInit + IContextMenu): '일집: 여기에 압축'(--compress-to 그 폴더 원본…),
    /// 끈 것이 모두 압축 파일이면 '일집: 여기에 풀기'(--extract-to-folder-in 그 폴더 압축… — 압축마다 '이름' 폴더).
    /// </summary>
    class __declspec(uuid("1AE50C95-17B6-40F9-918B-69099428FF2A"))
        IljipDragDropHandler : public Microsoft::WRL::RuntimeClass<
            Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
            IShellExtInit,
            IContextMenu>
    {
    public:
        // IShellExtInit
        IFACEMETHODIMP Initialize(PCIDLIST_ABSOLUTE folder, IDataObject* data, HKEY progId) noexcept override;
        // IContextMenu
        IFACEMETHODIMP QueryContextMenu(HMENU menu, UINT index, UINT firstId, UINT lastId, UINT flags) noexcept override;
        IFACEMETHODIMP InvokeCommand(CMINVOKECOMMANDINFO* info) noexcept override;
        IFACEMETHODIMP GetCommandString(UINT_PTR id, UINT type, UINT* reserved, CHAR* name, UINT nameSize) noexcept override;

    private:
        std::wstring m_target;                  // 끌어 놓은 폴더
        std::vector<std::wstring> m_sources;    // 끈 파일·폴더
        bool m_allArchives = false;
    };

    /// <summary>
    /// zip·7z 아이콘 위에 파일을 끌어 놓으면 그 압축에 넣기(드롭 처리기 — IPersistFile + IDropTarget, '--add-to 압축 파일…').
    /// 고전 레지스트리로만 등록된다(C# FileAssociationRegistrar — 선택). Drop 만 Iljip.exe 를 부른다.
    /// </summary>
    class __declspec(uuid("61C3F459-E40B-441D-9F76-F56C70DFF8C1"))
        IljipDropHandler : public Microsoft::WRL::RuntimeClass<
            Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
            Microsoft::WRL::ChainInterfaces<IPersistFile, IPersist>,
            IDropTarget>
    {
    public:
        // IPersist
        IFACEMETHODIMP GetClassID(CLSID* clsid) noexcept override;
        // IPersistFile
        IFACEMETHODIMP IsDirty() noexcept override;
        IFACEMETHODIMP Load(LPCOLESTR fileName, DWORD mode) noexcept override;
        IFACEMETHODIMP Save(LPCOLESTR fileName, BOOL remember) noexcept override;
        IFACEMETHODIMP SaveCompleted(LPCOLESTR fileName) noexcept override;
        IFACEMETHODIMP GetCurFile(LPOLESTR* fileName) noexcept override;
        // IDropTarget
        IFACEMETHODIMP DragEnter(IDataObject* data, DWORD keys, POINTL pt, DWORD* effect) noexcept override;
        IFACEMETHODIMP DragOver(DWORD keys, POINTL pt, DWORD* effect) noexcept override;
        IFACEMETHODIMP DragLeave() noexcept override;
        IFACEMETHODIMP Drop(IDataObject* data, DWORD keys, POINTL pt, DWORD* effect) noexcept override;

    private:
        DWORD EffectFor(DWORD allowed) const noexcept;
        std::wstring m_archive;     // 놓을 곳(압축 파일)
        bool m_canDrop = false;
    };
}
