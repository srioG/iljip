#include "pch.h"
#include "IljipCommands.h"

using namespace Microsoft::WRL;

// WRL 모듈 진입점
BOOL APIENTRY DllMain(HMODULE /*hModule*/, DWORD reason, LPVOID /*lpReserved*/)
{
    switch (reason)
    {
    case DLL_PROCESS_ATTACH:
        Module<InProc>::GetModule().Create();
        break;
    case DLL_PROCESS_DETACH:
        Module<InProc>::GetModule().Terminate();
        break;
    }
    return TRUE;
}

// COM 활성화 진입점
STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, LPVOID* ppv)
{
    return Module<InProc>::GetModule().GetClassObject(rclsid, riid, ppv);
}

// 언로드 가능 여부
STDAPI DllCanUnloadNow()
{
    return Module<InProc>::GetModule().Terminate() ? S_OK : S_FALSE;
}
