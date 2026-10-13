using System.IO;
using Microsoft.Win32;

namespace Iljip.Services;

/// <summary>
/// 레지스트리 뿌리 하나(W2-12 — 파일 연결 I1-1). 진짜는 <see cref="CurrentUserRegistryRoot"/>(HKCU — 사용자별, 관리자 권한 불필요),
/// 시험은 메모리 트리 가짜(Iljip.Tests/TestSupport/FakeRegistryRoot)만 쓴다 — 시험은 HKCU 의 시험용 하위 키조차 쓰지 않는다(설계 §5).
/// 경로는 뿌리 아래 상대 경로('Software\Classes\Iljip.zip'), 대소문자는 Windows 처럼 가리지 않는다.
/// </summary>
public interface IRegistryRoot
{
    /// <summary>키를 열거나(없으면 만들어) 쓸 수 있게 돌려준다.</summary>
    IRegistryKey CreateSubKey(string path);

    /// <summary>키를 연다. 없으면 null.</summary>
    IRegistryKey? OpenSubKey(string path, bool writable);

    /// <summary>키와 그 아래를 모두 지운다. 없을 때 <paramref name="throwOnMissing"/> 가 false 면 조용히 넘어간다.</summary>
    void DeleteSubKeyTree(string path, bool throwOnMissing);
}

/// <summary>연 레지스트리 키(<see cref="IRegistryRoot"/>). 값 이름 null·빈 문자열 = (기본값).</summary>
public interface IRegistryKey : IDisposable
{
    void SetValue(string? name, object value, RegistryValueKind kind);

    /// <summary>값(없으면 null). REG_NONE 은 byte[].</summary>
    object? GetValue(string? name);

    /// <summary>값의 종류(없으면 null).</summary>
    RegistryValueKind? GetValueKind(string? name);

    void DeleteValue(string name, bool throwOnMissing);

    string[] GetValueNames();

    string[] GetSubKeyNames();
}

/// <summary>진짜: HKEY_CURRENT_USER. 시험에서는 절대 만들지 않는다(AppServices 기본값이 이것 — 시험은 가짜를 넣음).</summary>
public sealed class CurrentUserRegistryRoot : IRegistryRoot
{
    public IRegistryKey CreateSubKey(string path) =>
        new Key(Registry.CurrentUser.CreateSubKey(path, writable: true) ?? throw new InvalidOperationException("레지스트리 키를 만들지 못했어요: " + path));

    public IRegistryKey? OpenSubKey(string path, bool writable) =>
        Registry.CurrentUser.OpenSubKey(path, writable) is { } k ? new Key(k) : null;

    public void DeleteSubKeyTree(string path, bool throwOnMissing) => Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissing);

    private sealed class Key(RegistryKey key) : IRegistryKey
    {
        public void SetValue(string? name, object value, RegistryValueKind kind) => key.SetValue(name ?? string.Empty, value, kind);

        public object? GetValue(string? name) => key.GetValue(name ?? string.Empty, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

        public RegistryValueKind? GetValueKind(string? name)
        {
            try
            {
                return key.GetValue(name ?? string.Empty) is null && !key.GetValueNames().Contains(name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    ? null
                    : key.GetValueKind(name ?? string.Empty);
            }
            catch (IOException)
            {
                return null;   // 값 없음
            }
        }

        public void DeleteValue(string name, bool throwOnMissing) => key.DeleteValue(name, throwOnMissing);

        public string[] GetValueNames() => key.GetValueNames();

        public string[] GetSubKeyNames() => key.GetSubKeyNames();

        public void Dispose() => key.Dispose();
    }
}
