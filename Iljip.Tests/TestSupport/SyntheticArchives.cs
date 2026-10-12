using System.Text;

namespace Iljip.Tests.TestSupport;

/// <summary>
/// 픽스처가 없는 형식(RAR4·RAR5·EGG·ALZ)을 같은 내용으로 테스트 안에서 만든다 — 통합 점검(2026-10-12)에서 '형식이 달라도 화면 흐름이 같은지'를
/// 한 표로 보려고 모았다. 내용(모든 형식 같음):
/// <code>
///   자료/            (폴더)
///   자료/견적서.txt   한글 이름·한글 내용
///   자료/빈파일.txt   0바이트
///   자료/하위/난수.bin 200,000바이트(진행 막대가 파일 안에서도 움직이게 81,920B 를 넘김)
///   readme.txt
/// </code>
/// 암호는 <see cref="FixtureCatalog.DefaultPassword"/>. RAR 은 <see cref="RawRar5"/>·<see cref="RawRar4"/>(반디집 bz.exe 로 맞게 풀리는 것 확인 —
/// <c>RarTests.합성_RAR을_반디집도_맞게_푼다</c>), EGG·ALZ 는 <see cref="RawEgg"/>·<see cref="RawAlz"/>.
/// </summary>
public static class SyntheticArchives
{
    /// <param name="Path">만든 압축 파일.</param>
    /// <param name="Password">풀 때 넣을 암호(없으면 null).</param>
    /// <param name="HeaderEncrypted">파일 이름까지 암호가 걸렸는지(열 때부터 암호를 물음).</param>
    /// <param name="Expected">풀린 결과 기대(폴더 포함, '/' 구분).</param>
    /// <param name="MtimeWallClock">수정 시각이 현지 벽시계(DOS 시각 — RAR4)인지. 아니면 절대 시각(<see cref="FixtureCatalog.FixedMtimeUtc"/>). null 이면 확인 안 함(EGG·ALZ).</param>
    public sealed record Built(string Path, string? Password, bool HeaderEncrypted, List<ExpectedEntry> Expected, bool? MtimeWallClock);

    public const string Folder = "자료";
    public const string KoreanFile = "자료/견적서.txt";

    /// <summary>만들 수 있는 종류.</summary>
    public static readonly string[] Kinds =
    {
        "rar5", "rar5_unix", "rar5_enc", "rar5_hdr", "rar4", "rar4_cp949", "rar4_enc", "rar4_hdr", "egg", "egg_enc", "alz",
    };

    private static readonly byte[] Korean = Encoding.UTF8.GetBytes("테스트용 가상품목 견적서 — 합성 데이터 0123456789\r\n");
    private static readonly byte[] Readme = Encoding.ASCII.GetBytes("synthetic readme\r\n");
    private static readonly byte[] Big = Enumerable.Range(0, 200_000).Select(i => (byte)(i * 7 + (i >> 9))).ToArray();

    /// <summary><paramref name="kind"/> 압축 파일을 <paramref name="dir"/> 에 '표본_종류.확장자' 로 만든다.</summary>
    public static Built Build(string kind, string dir)
    {
        var rar = new List<RawRar5.Item>
        {
            new(Folder + "/", null),
            new(KoreanFile, Korean),
            new(Folder + "/빈파일.txt", Array.Empty<byte>()),
            new(Folder + "/하위/난수.bin", Big),
            new("readme.txt", Readme),
        };
        string pw = FixtureCatalog.DefaultPassword;
        string ext = kind.StartsWith("rar") ? ".rar" : kind.StartsWith("egg") ? ".egg" : ".alz";
        string path = System.IO.Path.Combine(dir, $"표본_{kind}{ext}");
        switch (kind)
        {
            case "rar5": RawRar5.Write(path, rar); return new(path, null, false, Expected(), false);
            case "rar5_unix": RawRar5.Write(path, rar, unixTime: true); return new(path, null, false, Expected(), false);
            case "rar5_enc": RawRar5.Write(path, rar, pw); return new(path, pw, false, Expected(), false);
            case "rar5_hdr": RawRar5.Write(path, rar, pw, encryptHeaders: true); return new(path, pw, true, Expected(), false);
            case "rar4": RawRar4.Write(path, rar); return new(path, null, false, Expected(), true);
            case "rar4_cp949": RawRar4.Write(path, rar, unicodeNames: false); return new(path, null, false, Expected(), true);
            case "rar4_enc": RawRar4.Write(path, rar, pw); return new(path, pw, false, Expected(), true);
            case "rar4_hdr": RawRar4.Write(path, rar, pw, encryptHeaders: true); return new(path, pw, true, Expected(), true);
            case "egg":
            case "egg_enc":
                {
                    string? p = kind == "egg_enc" ? pw : null;
                    RawEgg.Write(path, rar.Select(i => new RawEgg.Item(i.Name, i.Data, Password: p)));
                    return new(path, p, false, Expected(), null);
                }
            case "alz":
                {
                    var cp949 = Encoding.GetEncoding(949);
                    RawAlz.Write(path, rar.Select(i => new RawAlz.Item(cp949.GetBytes(i.Name.Replace('/', '\\')), i.Data)).ToArray());
                    return new(path, null, false, Expected(), null);
                }
            default: throw new ArgumentException("모르는 종류: " + kind);
        }
    }

    /// <summary>풀린 결과 기대(폴더 '자료'·'자료/하위' 포함).</summary>
    public static List<ExpectedEntry> Expected() => new()
    {
        new(Folder, 0, null, true),
        new(KoreanFile, Korean.Length, TreeAssert.Sha256(Korean), false),
        new(Folder + "/빈파일.txt", 0, TreeAssert.Sha256(Array.Empty<byte>()), false),
        new(Folder + "/하위", 0, null, true),
        new(Folder + "/하위/난수.bin", Big.Length, TreeAssert.Sha256(Big), false),
        new("readme.txt", Readme.Length, TreeAssert.Sha256(Readme), false),
    };

    /// <summary>이 PC 현지 시각으로 본 기대 수정 시각(RAR4 = 벽시계 03:04:06, 나머지 = 절대 시각을 현지로).</summary>
    public static DateTime ExpectedLocalMtime(bool wallClock) =>
        wallClock ? DateTime.SpecifyKind(FixtureCatalog.FixedMtimeWallClock, DateTimeKind.Local) : FixtureCatalog.FixedMtimeUtc.ToLocalTime();
}
