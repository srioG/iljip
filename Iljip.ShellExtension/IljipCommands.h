#pragma once
#include "pch.h"

namespace IljipShell
{
    // 각 컨텍스트 메뉴 항목에 대한 CLSID.
    // MSIX manifest의 com:Class @Id 값과 일치해야 한다.

    // .zip/.7z 등 우클릭 → "일집으로 열기"
    constexpr GUID CLSID_OpenWithIljip       = { 0xB3E8A7D5, 0x1C9F, 0x4E2A, { 0xB6, 0xA1, 0xD3, 0x9C, 0x8F, 0x2E, 0x5B, 0x71 } };
    // .zip/.7z 등 우클릭 → "여기에 압축 풀기 (일집)"
    constexpr GUID CLSID_ExtractHere         = { 0x2A4F7C91, 0x8E6D, 0x4B3A, { 0x9C, 0x52, 0xF1, 0xD8, 0xE9, 0xA7, 0xB3, 0xC4 } };
    // .zip/.7z 등 우클릭 → "압축 풀기... (일집)"
    constexpr GUID CLSID_ExtractTo           = { 0x7D3E9C2A, 0x5B1F, 0x4A8C, { 0x93, 0xE6, 0xC2, 0xA8, 0xF5, 0xD7, 0xB1, 0xE9 } };
    // 일반 파일/폴더 우클릭 → "일집으로 압축"
    constexpr GUID CLSID_CompressWithIljip   = { 0xF9E2C1A8, 0x3D7B, 0x4E5A, { 0x91, 0xC8, 0xB5, 0xF7, 0xD2, 0xE9, 0xA3, 0xC6 } };

    /// <summary>
    /// 셸 확장 베이스. CRTP로 각 명령 클래스가 Title/Verb를 제공하고,
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
        IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* title) noexcept override
        {
            return SHStrDupW(static_cast<TDerived*>(this)->Title(), title);
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
        PCWSTR Title() noexcept { return L"여기에 압축 풀기 (일집)"; }
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
