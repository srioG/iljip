using System.Diagnostics;
using System.IO;
using SharpSevenZip;

namespace Iljip.Services;

/// <summary>
/// 7z.dll(SharpSevenZip 2.0.128 에 든 7-Zip 26.03, x64) 불러오기.
/// 빌드·발행 폴더의 <c>x64\7z.dll</c>(단일 파일 발행에서도 묶이지 않는 별도 파일 — LGPL 상 바꿔 끼울 수 있게)을 경로로 못박아 쓴다.
/// 서명 없는 네이티브 DLL 이라 다른 PC 의 보안 정책(AppLocker·WDAC·백신)이 막을 수 있다 — 그러면 <see cref="IsAvailable"/> 가 false 이고,
/// 7z·RAR 는 SharpCompress 로 대신 열고 나머지 7z.dll 전용 형식은 <see cref="EngineUnavailableException"/>(한국어 안내)이 난다.
/// </summary>
public static class SevenZipLibrary
{
    private static readonly object Gate = new();
    private static bool _initialized;
    private static bool _available;
    private static string? _version;
    private static Exception? _loadError;

    /// <summary>쓰는 7z.dll 의 경로(실행 파일 옆 x64 폴더).</summary>
    public static string LibraryPath => Path.Combine(AppContext.BaseDirectory, "x64", "7z.dll");

    /// <summary>7z.dll 을 실제로 불러와 7z 풀기가 되는지(처음 물을 때 한 번 확인).</summary>
    public static bool IsAvailable
    {
        get
        {
            EnsureLoaded();
            return _available && !SimulatedUnavailable.Value;
        }
    }

    // 계약 테스트용: 보안 정책이 7z.dll 을 막은 PC 를 흉내 낸다(이 비동기 흐름 안에서만 — 같이 도는 다른 테스트에는 영향 없음).
    private static readonly AsyncLocal<bool> SimulatedUnavailable = new();

    /// <summary>테스트 전용: Dispose 할 때까지 이 흐름에서 7z.dll 을 '못 불러온 것'으로 본다.</summary>
    internal static IDisposable SimulateUnavailableForTests()
    {
        SimulatedUnavailable.Value = true;
        return new Restore();
    }

    private sealed class Restore : IDisposable
    {
        public void Dispose() => SimulatedUnavailable.Value = false;
    }

    /// <summary>7z.dll 의 파일 버전(예: "26.03"). 못 불러왔으면 null.</summary>
    public static string? DllVersion
    {
        get
        {
            EnsureLoaded();
            return _version;
        }
    }

    /// <summary>불러오기에 실패한 이유(로그·안내용). 성공했으면 null.</summary>
    public static Exception? LoadError
    {
        get
        {
            EnsureLoaded();
            return _loadError;
        }
    }

    /// <summary>
    /// 한 번만: 경로를 지정하고(SharpSevenZip 기본 탐색 대신) 라이브러리 기능 점검으로 실제 로드까지 해 본다.
    /// 64비트가 아니거나 DLL 이 없거나 보안 프로그램이 막으면 예외를 삼키고 <see cref="IsAvailable"/> = false.
    /// </summary>
    public static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                if (!Environment.Is64BitProcess)
                    throw new PlatformNotSupportedException("64비트 프로세스가 아니라 x64 7z.dll 을 쓸 수 없어요.");
                string path = LibraryPath;
                if (!File.Exists(path))
                    throw new FileNotFoundException("7z.dll 이 없어요.", path);
                SharpSevenZipBase.SetLibraryPath(path);
                // 기능 점검이 내장 시험 압축을 실제로 풀어 본다(DLL 로드 + CreateObject 까지). 실패는 None 으로 돌아온다.
                var features = SharpSevenZipBase.CurrentLibraryFeatures;
                if ((features & LibraryFeature.Extract7z) == 0)
                    throw new InvalidOperationException($"7z.dll 기능 점검 실패({features}).");
                _version = FileVersionInfo.GetVersionInfo(path).FileVersion;
                _available = true;
            }
            catch (Exception ex)
            {
                _available = false;
                _loadError = ex;
                Logger.LogError("7z.dll 불러오기 실패 — 7z·RAR 는 SharpCompress 로 대신, 나머지 형식은 못 엶", ex);
            }
        }
    }

    /// <summary>7z.dll 이 없으면 쉬운 안내 예외를 던진다.</summary>
    public static void ThrowIfUnavailable()
    {
        if (!IsAvailable) throw new EngineUnavailableException(_loadError);
    }
}
