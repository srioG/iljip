using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Iljip.Services;

/// <summary>ZIP 암호가 어떤 바이트로 맞는가(<see cref="ZipPasswordProbe"/>).</summary>
public enum PasswordBytes
{
    /// <summary>(고른 항목 중) 암호 항목이 없다.</summary>
    NotApplicable,
    /// <summary>암호의 UTF-8 바이트로 맞는다(ASCII 암호도 여기 — 두 후보가 같음). 일집·7-Zip·WinZip 방식.</summary>
    Utf8,
    /// <summary>암호의 CP949 바이트로 맞는다 — 한국어 Windows 의 반디집·알집이 만든 한글 암호 ZIP(B3).</summary>
    Cp949,
    /// <summary>어느 바이트로도 맞지 않는다 = 틀린 암호(또는 암호를 주지 않음).</summary>
    None,
    /// <summary>판정할 수 없다(분할 .z01·앞에 다른 것이 붙은 파일·읽기 실패·알 수 없는 암호 방식) — 엔진에 맡긴다.</summary>
    Unknown,
}

/// <summary>중앙 디렉터리 한 항목(암호 판정과 AES 직접 해제에 필요한 것만). 크기·위치는 Zip64 추가 필드까지 반영한 값.</summary>
public sealed record ZipCentralEntry(
    int Index, string Name, int Flags, int Method, ushort DosTime, uint Crc,
    long CompressedSize, long UncompressedSize, long LocalHeaderOffset,
    int AesVersion, int AesStrength, int AesMethod)
{
    public bool IsEncrypted => (Flags & 1) != 0;

    /// <summary>목록 기록의 AES 추가 필드(0x9901) 내용 7바이트(판·'AE'·세기·안쪽 방식) 그대로 — 로컬 머리와 맞춰 볼 때(IJ3-02). 없으면 null.</summary>
    public byte[]? AesBlock { get; init; }

    /// <summary>WinZip AES(방식 99). <see cref="AesStrength"/> 1·2·3 = AES-128·192·256, <see cref="AesMethod"/> = 안쪽 압축 방식.</summary>
    public bool IsAes => Method == 99;
}

/// <summary>
/// 판정 결과. <see cref="AnyEncrypted"/>·<see cref="HasAes"/>·<see cref="HasZipCrypto"/> 는 고른 항목 기준.
/// <see cref="MatchedPasswordBytes"/> 는 <see cref="Match"/> 가 Utf8·Cp949 일 때 맞은 암호 바이트.
/// </summary>
public sealed record ZipPasswordProbeResult(bool AnyEncrypted, bool HasAes, bool HasZipCrypto, PasswordBytes Match)
{
    public byte[]? MatchedPasswordBytes { get; init; }

    /// <summary>
    /// 두 후보(UTF-8·CP949)가 모두 검사를 통과했고 가려낼 근거가 없을 때 '다른 후보'(아니면 None). 풀다가 데이터가 틀리면 이것으로 처음부터 다시 푼다(B3).
    /// 확인 바이트가 하나뿐인 ZipCrypto(항목 하나, 또는 하나만 골라 풀기)에서 약 1/128 로 생긴다.
    /// </summary>
    public PasswordBytes AlternateMatch { get; init; } = PasswordBytes.None;

    public byte[]? AlternatePasswordBytes { get; init; }

    /// <summary>
    /// 맞은 후보가 하나뿐이고 근거가 16비트 이상(틀린 암호가 우연히 통과할 확률 1/65536 이하 — ZipCrypto 확인 바이트 둘 이상, 또는 AES 확인값 하나)인지.
    /// 그렇다면 풀다가 나는 데이터·CRC·인증값 오류는 틀린 암호가 아니라 손상이다(맞게 풀린 파일은 남김).
    /// </summary>
    public bool Confirmed { get; init; }

    /// <summary>중앙 디렉터리 전체(순서 그대로). 판정할 수 없었으면 비어 있다.</summary>
    public IReadOnlyList<ZipCentralEntry> Entries { get; init; } = Array.Empty<ZipCentralEntry>();

    /// <summary>
    /// <see cref="PasswordBytes.None"/> 인데 어느 후보가 일부 항목의 확인값과는 맞았다 — 틀린 암호와 항목 손상을 가를 근거가 모자람(I4-1 검토:
    /// '암호가 맞지 않아요'에 '손상된 경우에도 이렇게 보일 수 있어요'를 붙인다).
    /// </summary>
    public bool SomeEntriesMatched { get; init; }

    public static ZipPasswordProbeResult Unknown(bool anyEncrypted = false) => new(anyEncrypted, false, false, PasswordBytes.Unknown);
}

/// <summary>
/// ZIP 을 풀기 '전에'(대상 폴더에 아무것도 만들기 전에) 암호가 맞는지, 어떤 바이트로 맞는지 판정한다(B3·B5·B4 의 핵심).
/// <list type="bullet">
/// <item>후보: 암호의 UTF-8 바이트와 CP949 바이트(CP949 로 못 바꾸는 글자가 있으면 뺌, ASCII 면 둘이 같아 하나).
///   반디집·알집(한국어 Windows)은 한글 암호를 CP949 로 쓰고, 일집(SharpZipLib AES)·7-Zip 은 UTF-8 로 쓴다.</item>
/// <item>ZipCrypto 항목(최대 64개): 12바이트 암호 머리를 풀어 마지막 바이트를 CRC 윗바이트(데이터 설명자 플래그면 DOS 시각 윗바이트도)와 비교.
///   항목 하나는 1/256 확률로 틀린 암호도 통과하지만, '모든 항목이 맞는 후보만' 남기므로 둘째 항목부터 걸러진다
///   (예전엔 첫 항목만 보고 쓰레기를 풀어 썼다 — B4·B5).</item>
/// <item>AES 항목(최대 3개): 0x9901 추가 필드의 세기, 로컬 머리 뒤 salt·2바이트 확인값(PV)을 PBKDF2-HMAC-SHA1(1000회)로 맞춰 봄.</item>
/// <item>두 후보가 다 통과하거나 근거가 약하면(고른 항목의 확인 바이트가 하나뿐 등) 고르지 않은 암호 항목도 본다 — 한 후보만 맞으면 그것을 고르고
///   근거에 더한다(같은 압축 파일은 보통 한 암호). 그래도 못 가리면 ZipCrypto 는 CP949(반디집·알집·일집이 한글 암호를 쓰는 방식), AES 는 UTF-8(일집 AES)을
///   먼저 쓰고 다른 후보를 <see cref="ZipPasswordProbeResult.AlternateMatch"/> 로 남긴다(예전엔 UTF-8 이 우연히 통과하면 맞는 한글 암호로도 못 풀었다).</item>
/// </list>
/// PKZIP 나눔(.z01 … .zip)은 W2-03 부터 조각을 이어 보통 ZIP 처럼 판정한다(<see cref="SpannedZipStream"/> — 앞 조각이 없으면 아래처럼 Unknown).
/// 예외를 내지 않는다. 분할(.z01, 디스크 번호 ≠ 0)·앞에 다른 것이 붙은 파일(SFX 등 — 첫 4바이트가 ZIP 머리가 아님)·
/// 중앙 디렉터리가 깨진 파일·읽기 실패는 <see cref="PasswordBytes.Unknown"/> — 그때는 엔진이 암호를 확인하고 CRC 검사와 롤백 장부가 막는다.
/// </summary>
public static class ZipPasswordProbe
{
    private const int MaxZipCryptoChecks = 64;
    private const int MaxAesChecks = 3;
    private const long MaxCentralDirectoryBytes = 256L * 1024 * 1024;

    private static readonly Encoding StrictCp949 =
        Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    /// <param name="include">판정에 넣을 항목(이름은 목록·해제와 같은 KoreanFileNameDecoder 이름). null 이면 전부.</param>
    public static ZipPasswordProbeResult Probe(string zipPath, string? password, Func<string, bool>? include = null)
    {
        try
        {
            using var fs = ZipArchiveService.OpenRawStream(zipPath);   // .zip.001 숫자 분할은 이어 붙인 스트림(W2-01)
            var entries = ReadCentralDirectory(fs);
            return entries is null ? ZipPasswordProbeResult.Unknown() : Decide(fs, entries, password, include);
        }
        catch (Exception)
        {
            // 판정은 '덤'이다 — 못 하면 엔진에 맡긴다(읽기 실패의 진짜 이유는 엔진이 다시 알린다).
            return ZipPasswordProbeResult.Unknown();
        }
    }

    /// <summary>암호를 CP949 바이트로 바꾼다. 못 바꾸는 글자(이모지·일부 한자 등)가 있으면 null.</summary>
    public static byte[]? TryCp949Bytes(string password)
    {
        try { return StrictCp949.GetBytes(password); }
        catch (EncoderFallbackException) { return null; }
    }

    private static ZipPasswordProbeResult Decide(Stream fs, List<ZipCentralEntry> entries, string? password, Func<string, bool>? include)
    {
        // 폴더 항목은 풀 내용이 없어(암호 비트가 켜져 있어도) 판정에 넣지 않는다.
        var targets = entries.Where(e => e.IsEncrypted && !IsDirectoryName(e.Name) && (include is null || include(e.Name))).ToList();
        if (targets.Count == 0)
            return new ZipPasswordProbeResult(false, false, false, PasswordBytes.NotApplicable) { Entries = entries };

        // 목록의 AES 정보가 로컬 머리와 어긋나는 항목(목록 손상 — 그 세기로는 맞는 암호도 틀려 보인다)은 판정에 넣지 않는다. 풀기·검사에서는 구조 검사가
        // 그 항목을 '목록 손상'으로 알리고 읽지 않는다(IJ3-02 I2-8 — 예전엔 맞는 암호에도 '암호가 맞지 않아요'). 다 빠지면 판정 못 함(엔진에 맡김).
        var damaged = targets.Where(e => e.IsAes && !LocalAesAgrees(fs, e)).Select(e => e.Index).ToHashSet();
        if (damaged.Count > 0)
        {
            targets = targets.Where(e => !damaged.Contains(e.Index)).ToList();
            if (targets.Count == 0) return ZipPasswordProbeResult.Unknown(anyEncrypted: true) with { Entries = entries };
        }

        // 목록 기록에만 암호 표시가 있고 로컬 머리에는 없으면 목록이 깨진 것(데이터는 암호가 없음) — 판정에서 뺀다. 구조 검사가 그 항목을 손상으로 알린다
        // (I4-1 검토: 예전엔 암호 없는 ZIP 에 암호를 물어 손상을 암호 문제로 보였다)
        targets = targets.Where(e => LocalFlags(fs, e) is not { } plain || (plain & 0x01) != 0).ToList();
        if (targets.Count == 0)
            return new ZipPasswordProbeResult(false, false, false, PasswordBytes.NotApplicable) { Entries = entries };

        bool hasAes = targets.Any(e => e.IsAes);
        bool hasZipCrypto = targets.Any(e => !e.IsAes);

        // 강한 암호화(PKWARE SES, 플래그 bit 6)는 판정할 수 없다 — 엔진에 맡긴다. 목록 기록에만 그 표시가 있고 로컬 머리에는 없으면 목록이 깨진 것 —
        // 판정에서 빼고 나머지로 본다(I4-1 검토: 한 기록의 표시 칸이 깨져 판정을 통째로 못 하면 AES 항목을 엔진이 풀어 틀린 내용이 나왔다)
        targets = targets.Where(e => (e.Flags & 0x40) == 0 || LocalFlags(fs, e) is not { } lf || (lf & 0x40) != 0).ToList();
        if (targets.Any(e => (e.Flags & 0x40) != 0))
            return ZipPasswordProbeResult.Unknown(anyEncrypted: true) with { Entries = entries };
        if (targets.Count == 0)
            return ZipPasswordProbeResult.Unknown(anyEncrypted: true) with { Entries = entries };
        // 세기를 모르는 AES·암호 머리보다 짧은 항목은 목록이 깨진 것 — 판정에서 빼고(구조 검사가 그 항목을 알린다) 나머지로 본다(I4-1 검토:
        // 예전엔 한 항목 때문에 판정을 통째로 못 해 한글 암호를 엔진에 글자로 넘겨 맞는 암호로도 '암호가 맞지 않아요').
        targets = targets.Where(IsCheckable).ToList();
        if (targets.Count == 0)
            return ZipPasswordProbeResult.Unknown(anyEncrypted: true) with { Entries = entries };

        if (string.IsNullOrEmpty(password))
            return new ZipPasswordProbeResult(true, hasAes, hasZipCrypto, PasswordBytes.None) { Entries = entries };

        byte[] utf8 = Encoding.UTF8.GetBytes(password);
        byte[]? cp949 = TryCp949Bytes(password);
        if (cp949 is not null && cp949.AsSpan().SequenceEqual(utf8)) cp949 = null;   // ASCII — 후보가 같음

        var checks = BuildChecks(fs, targets);
        if (checks.Count == 0)
            return ZipPasswordProbeResult.Unknown(anyEncrypted: true) with { Entries = entries };   // 고른 항목의 로컬 머리를 하나도 못 읽음
        var passing = new List<(PasswordBytes Kind, byte[] Bytes)>();
        if (checks.All(c => c.Passes(utf8))) passing.Add((PasswordBytes.Utf8, utf8));
        if (cp949 is not null && checks.All(c => c.Passes(cp949))) passing.Add((PasswordBytes.Cp949, cp949));
        if (passing.Count == 0)
            return PartialMatch(checks, cp949 is null ? new[] { (PasswordBytes.Utf8, utf8) } : new[] { (PasswordBytes.Utf8, utf8), (PasswordBytes.Cp949, cp949) },
                hasAes, hasZipCrypto, entries);

        double bits = checks.Sum(c => c.Bits);

        // 두 후보가 다 통과했거나 근거가 약하면(고른 항목의 ZipCrypto 확인 바이트가 하나뿐 등) 고르지 않은 암호 항목도 본다.
        // 이것은 '덤' 근거라 맞지 않아도 후보를 버리지 않는다(항목마다 암호가 다른 드문 ZIP) — 가려내기와 확인 근거에만 쓴다.
        List<PasswordCheck> extra = new();
        if (passing.Count > 1 || bits < ConfirmBits)
        {
            var targetIndexes = targets.Select(e => e.Index).ToHashSet();
            extra = TryBuildChecks(fs, entries.Where(e =>
                e.IsEncrypted && !IsDirectoryName(e.Name) && !targetIndexes.Contains(e.Index) && !damaged.Contains(e.Index) && IsCheckable(e)
                && (!e.IsAes || LocalAesAgrees(fs, e))));
        }
        bool PassesExtra(byte[] pw) => extra.Count > 0 && extra.All(c => c.Passes(pw));

        if (passing.Count > 1)
        {
            var byExtra = passing.Where(p => PassesExtra(p.Bytes)).ToList();
            if (byExtra.Count == 1)
                passing = byExtra;
            else if (hasZipCrypto)
                passing.Reverse();   // 못 가림: ZipCrypto 는 CP949 먼저(반디집·알집·일집이 한글 암호를 쓰는 방식). AES 만이면 UTF-8 먼저(일집 AES) 그대로
        }

        var (kind, bytes) = passing[0];
        if (PassesExtra(bytes)) bits += extra.Sum(c => c.Bits);
        var result = new ZipPasswordProbeResult(true, hasAes, hasZipCrypto, kind)
        {
            MatchedPasswordBytes = bytes,
            Confirmed = passing.Count == 1 && bits >= ConfirmBits,
            Entries = entries,
        };
        return passing.Count > 1
            ? result with { AlternateMatch = passing[1].Kind, AlternatePasswordBytes = passing[1].Bytes }
            : result;
    }

    /// <summary>이 정도 근거(비트)면 '암호 확인됨'으로 본다 — 틀린 암호가 우연히 통과할 확률 1/65536.</summary>
    private const double ConfirmBits = 16;

    /// <summary>
    /// 어느 후보도 모든 항목과 맞지 않을 때(I4-1 검토): 한 후보가 맞은 항목들의 근거가, 여러 항목 가운데 몇 개가 우연히 맞을 가능성(조합 수)을 빼고도
    /// 16비트 이상이면 그 암호가 맞고 맞지 않은 항목은 암호 머리가 깨진 것으로 본다(확인됨 — 풀기·검사는 그 항목만 손상으로 알림).
    /// 아니면 틀린 암호(어느 후보든 일부 항목과 맞았으면 <see cref="ZipPasswordProbeResult.SomeEntriesMatched"/> — '손상일 수도' 덧말).
    /// 예전엔 항목 하나의 암호 머리만 깨져도 맞는 암호에 '암호가 맞지 않아요. 다시 넣어 주세요.'가 되풀이됐다.
    /// </summary>
    private static ZipPasswordProbeResult PartialMatch(List<PasswordCheck> checks, (PasswordBytes Kind, byte[] Bytes)[] candidates,
        bool hasAes, bool hasZipCrypto, List<ZipCentralEntry> entries)
    {
        (PasswordBytes Kind, byte[] Bytes, double Evidence)? best = null;
        bool some = false;
        foreach (var (kind, bytes) in candidates)
        {
            var passed = checks.Where(c => c.Passes(bytes)).ToList();
            if (passed.Count == 0) continue;
            some = true;
            double evidence = passed.Sum(c => c.Bits) - Log2Binomial(checks.Count, passed.Count);
            if (evidence >= ConfirmBits && (best is null || evidence > best.Value.Evidence)) best = (kind, bytes, evidence);
        }
        if (best is { } b)
            return new ZipPasswordProbeResult(true, hasAes, hasZipCrypto, b.Kind) { MatchedPasswordBytes = b.Bytes, Confirmed = true, Entries = entries };
        return new ZipPasswordProbeResult(true, hasAes, hasZipCrypto, PasswordBytes.None) { Entries = entries, SomeEntriesMatched = some };
    }

    /// <summary>log2(n 개 중 k 개를 고르는 수).</summary>
    private static double Log2Binomial(int n, int k)
    {
        double s = 0;
        for (int i = 1; i <= k; i++) s += Math.Log2((double)(n - k + i) / i);
        return s;
    }

    /// <summary>암호 바이트 하나를 맞춰 보는 검사와 그 검사의 근거 크기(틀린 암호가 통과할 확률 = 2^-Bits).</summary>
    private sealed record PasswordCheck(Func<byte[], bool> Passes, double Bits);

    /// <summary>판정할 수 있는 암호 항목인가(강한 암호화·세기를 모르는 AES·암호 머리보다 짧은 항목은 아님).</summary>
    private static bool IsCheckable(ZipCentralEntry e) =>
        (e.Flags & 0x40) == 0
        && (e.IsAes
            ? e.AesStrength is >= 1 and <= 3 && e.CompressedSize >= WinZipAes.SaltLength(e.AesStrength) + 2 + WinZipAes.AuthCodeLength
            : e.CompressedSize >= 12);

    /// <summary>ZipCrypto 항목은 최대 64개, AES 항목은 최대 3개까지 검사를 만든다(로컬 머리에서 암호 머리·salt 를 읽음).</summary>
    private static List<PasswordCheck> BuildChecks(Stream fs, IEnumerable<ZipCentralEntry> list)
    {
        // 로컬 머리 서명이 깨진 항목은 확인값을 읽을 수 없다 — 빼고 본다(구조 검사가 그 항목을 손상으로 알린다. I4-1 검토: 예전엔 판정을 통째로 못 함)
        var items = list.Where(e => TryLocalData(fs, e) is not null).ToList();
        var checks = new List<PasswordCheck>();
        foreach (var e in items.Where(e => !e.IsAes).Take(MaxZipCryptoChecks))
        {
            var (dataStart, localTime) = LocalData(fs, e);
            byte[] head = ReadAt(fs, dataStart, 12);
            var accepted = new HashSet<byte> { (byte)(e.Crc >> 24) };
            if ((e.Flags & 0x08) != 0)
            {
                // 데이터 설명자(bit 3)면 CRC 를 미리 모르는 만든이가 DOS 시각 윗바이트를 쓴다(SharpCompress 도 둘 다 받아 줌).
                accepted.Add((byte)(localTime >> 8));
                accepted.Add((byte)(e.DosTime >> 8));
            }
            byte[] acc = accepted.ToArray();
            checks.Add(new PasswordCheck(pw => ZipCryptoCheck(pw, head, acc), Math.Log2(256.0 / acc.Length)));
        }
        foreach (var e in items.Where(e => e.IsAes).Take(MaxAesChecks))
        {
            var (dataStart, _) = LocalData(fs, e);
            int strength = e.AesStrength;
            int saltLen = WinZipAes.SaltLength(strength);
            if (dataStart + saltLen + 2 > fs.Length) continue;   // 끝이 잘림 — 확인값을 읽을 수 없음
            byte[] saltPv = ReadAt(fs, dataStart, saltLen + 2);
            byte[] salt = saltPv[..saltLen], pv = saltPv[saltLen..];
            checks.Add(new PasswordCheck(pw => WinZipAes.PasswordVerifierMatches(pw, salt, pv, strength), 16));
        }
        return checks;
    }

    /// <summary>고르지 않은 항목의 검사(덤 근거). 그 항목의 머리가 깨져 읽을 수 없으면 덤 없이 판정한다.</summary>
    private static List<PasswordCheck> TryBuildChecks(Stream fs, IEnumerable<ZipCentralEntry> list)
    {
        try { return BuildChecks(fs, list); }
        catch (Exception) { return new List<PasswordCheck>(); }
    }

    private static bool IsDirectoryName(string name) => name.EndsWith('/') || name.EndsWith('\\');

    // ───── 중앙 디렉터리 읽기 ─────

    /// <summary>중앙 디렉터리 전체. 판정할 수 없는 모양이면 null.</summary>
    private static List<ZipCentralEntry>? ReadCentralDirectory(Stream fs)
    {
        long len = fs.Length;
        if (len < 22) return null;

        // 첫 머리가 ZIP 로컬 머리(또는 빈 ZIP 의 끝 머리)가 아니면 앞에 다른 것이 붙은 파일(SFX 등)이나 분할 조각.
        // 서명 4바이트 중 한 바이트만 다르면 깨진 첫 머리로 보고 판정한다(I4-1 검토 — 그 항목은 판정에서 빠지고 구조 검사가 알린다)
        uint first = BitConverter.ToUInt32(ReadAt(fs, 0, 4));
        if (first != 0x04034b50 && first != 0x06054b50 && !OneByteOff(first, 0x04034b50)) return null;

        int tailLen = (int)Math.Min(len, 22 + 0xFFFF);
        long tailStart = len - tailLen;
        byte[] tail = ReadAt(fs, tailStart, tailLen);
        int eocd = -1, loose = -1;
        for (int i = tailLen - 22; i >= 0; i--)
        {
            if (BitConverter.ToUInt32(tail, i) != 0x06054b50) continue;
            if (loose < 0) loose = i;
            if (i + 22 + BitConverter.ToUInt16(tail, i + 20) <= tailLen) { eocd = i; break; }
        }
        // 주석 길이 칸이 깨져 파일 끝을 넘으면 맨 뒤의 끝 머리 서명으로(구조 검사 ZipDirectory.FindEnd(lenient) 와 같게 — I4-1 검토: 그 칸 하나로 판정을 못 해
        // 맞는 암호로도 '암호가 맞지 않아요'). 목록 자리는 아래에서 서명으로 확인한다.
        if (eocd < 0) eocd = loose;
        if (eocd < 0) return null;
        long eocdPos = tailStart + eocd;

        long disk = BitConverter.ToUInt16(tail, eocd + 4);
        long cdDisk = BitConverter.ToUInt16(tail, eocd + 6);
        long count = BitConverter.ToUInt16(tail, eocd + 10);
        long cdSize = BitConverter.ToUInt32(tail, eocd + 12);
        long cdOffset = BitConverter.ToUInt32(tail, eocd + 16);
        long cdEnd = eocdPos;

        bool zip64Needed = count == 0xFFFF || cdSize == 0xFFFFFFFF || cdOffset == 0xFFFFFFFF;
        byte[]? locator = (zip64Needed || disk == 0xFFFF || cdDisk == 0xFFFF) && eocdPos >= 20 ? ReadAt(fs, eocdPos - 20, 20) : null;
        if (locator is not null && BitConverter.ToUInt32(locator, 0) != 0x07064b50) locator = null;
        if (zip64Needed && locator is null) return null;
        if (locator is { } loc)
        {
            // Zip64: 끝 머리 바로 앞의 위치 표(locator) → Zip64 끝 머리(디스크 번호 칸만 가득이고 위치 표가 없으면 그 칸이 깨진 보통 ZIP — 아래에서 보지 않음)
            long z64Pos = (long)BitConverter.ToUInt64(loc, 8);
            if (z64Pos < 0 || z64Pos + 56 > eocdPos) return null;
            byte[] z = ReadAt(fs, z64Pos, 56);
            if (BitConverter.ToUInt32(z, 0) != 0x06064b50) return null;
            count = (long)BitConverter.ToUInt64(z, 32);
            cdSize = (long)BitConverter.ToUInt64(z, 40);
            cdOffset = (long)BitConverter.ToUInt64(z, 48);
            cdEnd = z64Pos;
        }

        // 디스크 번호 칸은 보지 않는다 — 나눔 세트(.z01 …)는 이어 붙인 스트림이 한 디스크로 고쳐 쓴 목록을 주므로(SpannedZipStream) 여기서 0 이 아니면 그 칸이 깨진 것
        // (I4-1 검토: 그 칸 하나가 깨지면 판정을 못 해 한글 암호를 엔진에 글자로 넘겨 맞는 암호로도 '암호가 맞지 않아요'). 목록 자리는 아래에서 서명으로 확인한다.
        if (cdOffset < 0 || cdSize < 0) return null;
        if (cdOffset + cdSize != cdEnd)
        {
            // 목록 크기 칸이나 위치 칸 하나만 깨졌을 수 있다 — 다른 칸이 끝 머리 바로 앞까지의 목록 기록을 가리키면 그것으로 읽는다(IJ3-02 I2-8 4차 검증:
            // 반디집 한글 암호 ZIP 의 목록 크기 칸이 깨지면 판정을 못 해 맞는 암호로도 '암호가 맞지 않아요'). 둘 다 맞지 않으면 위치를 믿을 수 없음.
            if (cdOffset < cdEnd && CentralSigAt(fs, cdOffset)) cdSize = cdEnd - cdOffset;
            else if (cdSize <= cdEnd && CentralSigAt(fs, cdEnd - cdSize)) cdOffset = cdEnd - cdSize;
            else return null;
        }
        if (cdSize > MaxCentralDirectoryBytes) return null;

        // 기록은 목록 크기만큼 빈틈없이 읽는다 — 끝 머리의 항목 수 칸은 믿지 않는다(IJ3-02 I2-8: 그 칸만 0 이 되면 암호 항목이 없는 것으로 판정해
        // 반디집 한글 암호를 엔진에 글자로 넘겨 맞는 암호로도 '암호가 맞지 않아요'). 기록 사이가 어긋나면 판정하지 않는다(엔진에 맡김).
        byte[] cd = ReadAt(fs, cdOffset, (int)cdSize);
        var list = new List<ZipCentralEntry>((int)Math.Min(Math.Max(count, cdSize / 46), 100_000));
        var rawNames = new List<ZipEntryNames.Raw>(list.Capacity);   // W2-09: 이름은 다 읽은 뒤 압축 파일 하나에 한 방식으로 푼다(목록과 같은 이름)
        int p = 0;
        for (int n = 0; p < cd.Length; n++)
        {
            if (p + 46 > cd.Length || BitConverter.ToUInt32(cd, p) != 0x02014b50)
            {
                // 끝 머리의 항목 수만큼 다 읽었으면 뒤에 남은 바이트는 넘긴다(엔진도 항목 수만큼 읽는다 — I4-1 검토: 마지막 기록 안에 바이트가 끼어
                // 판정을 못 하면 한글 암호를 엔진에 글자로 넘겨 맞는 암호로도 '암호가 맞지 않아요')
                if (n > 0 && n == count) break;
                return null;
            }
            int flags = BitConverter.ToUInt16(cd, p + 8);
            int method = BitConverter.ToUInt16(cd, p + 10);
            ushort time = BitConverter.ToUInt16(cd, p + 12);
            uint crc = BitConverter.ToUInt32(cd, p + 16);
            long csize = BitConverter.ToUInt32(cd, p + 20);
            long usize = BitConverter.ToUInt32(cd, p + 24);
            int nameLen = BitConverter.ToUInt16(cd, p + 28);
            int extraLen = BitConverter.ToUInt16(cd, p + 30);
            int commentLen = BitConverter.ToUInt16(cd, p + 32);
            long diskStart = BitConverter.ToUInt16(cd, p + 34);
            long offset = BitConverter.ToUInt32(cd, p + 42);
            int nameAt = p + 46, extraAt = nameAt + nameLen, next = extraAt + extraLen + commentLen;
            if (next > cd.Length) return null;

            int aesVersion = 0, aesStrength = 0, aesMethod = 0;
            byte[]? aesBlock = ZipArchiveService.AesBlockOf(cd, extraAt, extraLen);
            for (int x = extraAt; x + 4 <= extraAt + extraLen;)
            {
                int id = BitConverter.ToUInt16(cd, x), size = BitConverter.ToUInt16(cd, x + 2);
                int d = x + 4;
                if (d + size > extraAt + extraLen) break;
                if (id == 0x0001)
                {
                    // Zip64: 원래 칸이 가득(0xFFFFFFFF/0xFFFF)인 값만 이 순서로 들어 있다
                    int q = d;
                    if (usize == 0xFFFFFFFF && q + 8 <= d + size) { usize = (long)BitConverter.ToUInt64(cd, q); q += 8; }
                    if (csize == 0xFFFFFFFF && q + 8 <= d + size) { csize = (long)BitConverter.ToUInt64(cd, q); q += 8; }
                    if (offset == 0xFFFFFFFF && q + 8 <= d + size) { offset = (long)BitConverter.ToUInt64(cd, q); q += 8; }
                    if (diskStart == 0xFFFF && q + 4 <= d + size) diskStart = BitConverter.ToUInt32(cd, q);
                }
                else if (id == 0x9901 && size >= 7)
                {
                    aesVersion = BitConverter.ToUInt16(cd, d);
                    aesStrength = cd[d + 4];
                    aesMethod = BitConverter.ToUInt16(cd, d + 5);
                }
                x = d + size;
            }
            if (diskStart != 0 || offset < 0 || csize < 0 || usize < 0) return null;

            rawNames.Add(ZipEntryNames.Read(cd, p, flags, nameLen, extraLen, commentLen));
            list.Add(new ZipCentralEntry(n, string.Empty, flags, method, time, crc, csize, usize, offset, aesVersion, aesStrength, aesMethod) { AesBlock = aesBlock });
            p = next;
        }
        int commentLength = Math.Min(BitConverter.ToUInt16(tail, eocd + 20), tail.Length - eocd - 22);   // 깨진 주석 길이 칸은 파일 끝까지만
        var names = ZipEntryNames.Decode(rawNames, tail.AsSpan(eocd + 22, commentLength).ToArray());
        for (int i = 0; i < list.Count; i++) list[i] = list[i] with { Name = names[i] };
        return list;
    }

    /// <summary>목록의 AES 정보가 로컬 머리의 것과 맞는가(<see cref="ZipArchiveService.AesAgrees"/> — 로컬 머리를 못 읽으면 맞는 것으로).</summary>
    private static bool LocalAesAgrees(Stream fs, ZipCentralEntry e)
    {
        if (e.LocalHeaderOffset < 0 || e.LocalHeaderOffset + 30 > fs.Length) return true;
        byte[] h = ReadAt(fs, e.LocalHeaderOffset, 30);
        if (BitConverter.ToUInt32(h, 0) != 0x04034b50) return true;
        int nameLen = BitConverter.ToUInt16(h, 26), extraLen = BitConverter.ToUInt16(h, 28);
        long extraAt = e.LocalHeaderOffset + 30 + nameLen;
        if (extraLen == 0 || extraAt + extraLen > fs.Length) return true;
        byte[] extra = ReadAt(fs, extraAt, extraLen);
        return ZipArchiveService.AesAgrees(e.AesBlock, ZipArchiveService.AesBlockOf(extra, 0, extraLen), e.IsAes);
    }

    /// <summary>로컬 머리의 표시 칸(서명이 맞을 때만 — 아니면 null).</summary>
    private static int? LocalFlags(Stream fs, ZipCentralEntry e)
    {
        if (e.LocalHeaderOffset < 0 || e.LocalHeaderOffset + 30 > fs.Length) return null;
        byte[] h = ReadAt(fs, e.LocalHeaderOffset, 30);
        return BitConverter.ToUInt32(h, 0) == 0x04034b50 ? BitConverter.ToUInt16(h, 6) : null;
    }

    /// <summary><see cref="LocalData"/> — 로컬 머리를 읽을 수 없거나(파일 밖·서명이 다름) 데이터가 파일 밖이면 null.</summary>
    private static (long DataStart, ushort LocalTime)? TryLocalData(Stream fs, ZipCentralEntry e)
    {
        if (e.LocalHeaderOffset < 0 || e.LocalHeaderOffset + 30 > fs.Length) return null;
        byte[] h = ReadAt(fs, e.LocalHeaderOffset, 30);
        if (BitConverter.ToUInt32(h, 0) != 0x04034b50) return null;
        long dataStart = e.LocalHeaderOffset + 30 + BitConverter.ToUInt16(h, 26) + BitConverter.ToUInt16(h, 28);
        return dataStart + 12 <= fs.Length ? (dataStart, BitConverter.ToUInt16(h, 10)) : null;
    }

    /// <summary>항목 데이터가 시작하는 위치와 로컬 머리의 DOS 시각.</summary>
    internal static (long DataStart, ushort LocalTime) LocalData(Stream fs, ZipCentralEntry e)
    {
        byte[] h = ReadAt(fs, e.LocalHeaderOffset, 30);
        if (BitConverter.ToUInt32(h, 0) != 0x04034b50)
            throw new InvalidDataException("ZIP 로컬 머리를 찾지 못함: " + e.Name);
        int nameLen = BitConverter.ToUInt16(h, 26), extraLen = BitConverter.ToUInt16(h, 28);
        return (e.LocalHeaderOffset + 30 + nameLen + extraLen, BitConverter.ToUInt16(h, 10));
    }

    /// <summary>두 4바이트 값이 정확히 한 바이트만 다른가.</summary>
    private static bool OneByteOff(uint a, uint b)
    {
        int diff = 0;
        for (int k = 0; k < 4; k++)
            if ((byte)(a >> (8 * k)) != (byte)(b >> (8 * k))) diff++;
        return diff == 1;
    }

    /// <summary><paramref name="at"/> 에 목록 기록 서명('PK\x01\x02')이 있는가.</summary>
    private static bool CentralSigAt(Stream s, long at) =>
        at >= 0 && at + 4 <= s.Length && BitConverter.ToUInt32(ReadAt(s, at, 4)) == 0x02014b50;

    internal static byte[] ReadAt(Stream s, long position, int count)
    {
        var b = new byte[count];
        s.Position = position;
        s.ReadExactly(b);
        return b;
    }

    // ───── ZipCrypto(전통 PKWARE 암호) 1바이트 검사 ─────

    /// <summary>
    /// 암호 바이트로 12바이트 암호 머리를 풀어 마지막 바이트가 받아들일 값 중 하나인지. 키 계산은 자동 풀림 스텁과 같은 공유 소스
    /// <see cref="ZipCryptoKeys"/>(Services/Shared — W2-10).
    /// </summary>
    internal static bool ZipCryptoCheck(byte[] password, byte[] head12, byte[] accepted) =>
        ZipCryptoKeys.CheckHeader(password, head12, accepted, out _);
}
