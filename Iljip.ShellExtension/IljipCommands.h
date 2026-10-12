#pragma once
#include "pch.h"

namespace IljipShell
{
    // 각 컨텍스트 메뉴 항목에 대한 CLSID.
    // MSIX manifest의 com:Class @Id 값과 일치해야 한다(Iljip.Tests ShellExtensionContractTests 가 대조).

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

    /// <summary>
    /// '"이름"\ 에 풀기'가 만들 폴더 이름 — C# Iljip.Services.ArchiveNaming.FolderNameFor 와 같은 규칙(메뉴 제목 = 실제 폴더 이름).
    /// 파일 이름 문자열만 다룬다(셸 메뉴 단계에서 파일 내용을 읽지 않는다). 한쪽을 고치면 다른 쪽도 고칠 것.
    /// </summary>
    std::wstring FolderNameFor(const std::wstring& fileName);

    /// <summary>메뉴 제목에 넣을 이름 줄이기(40자를 넘으면 39자 + '…', 서로게이트 쌍은 자르지 않음).</summary>
    std::wstring ShortenForMenu(const std::wstring& name);

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

        /// <summary>경로를 따옴표로 안전하게 감싸기.</summary>
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
}
