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
            using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
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

    private static ZipPasswordProbeResult Decide(FileStream fs, List<ZipCentralEntry> entries, string? password, Func<string, bool>? include)
    {
        // 폴더 항목은 풀 내용이 없어(암호 비트가 켜져 있어도) 판정에 넣지 않는다.
        var targets = entries.Where(e => e.IsEncrypted && !IsDirectoryName(e.Name) && (include is null || include(e.Name))).ToList();
        if (targets.Count == 0)
            return new ZipPasswordProbeResult(false, false, false, PasswordBytes.NotApplicable) { Entries = entries };

        bool hasAes = targets.Any(e => e.IsAes);
        bool hasZipCrypto = targets.Any(e => !e.IsAes);

        // 강한 암호화(PKWARE SES, 플래그 bit 6)·세기를 모르는 AES·암호 머리보다 짧은 항목(손상)은 판정할 수 없다 — 엔진에 맡긴다.
        if (!targets.All(IsCheckable))
            return ZipPasswordProbeResult.Unknown(anyEncrypted: true) with { Entries = entries };

        if (string.IsNullOrEmpty(password))
            return new ZipPasswordProbeResult(true, hasAes, hasZipCrypto, PasswordBytes.None) { Entries = entries };

        byte[] utf8 = Encoding.UTF8.GetBytes(password);
        byte[]? cp949 = TryCp949Bytes(password);
        if (cp949 is not null && cp949.AsSpan().SequenceEqual(utf8)) cp949 = null;   // ASCII — 후보가 같음

        var checks = BuildChecks(fs, targets);
        var passing = new List<(PasswordBytes Kind, byte[] Bytes)>();
        if (checks.All(c => c.Passes(utf8))) passing.Add((PasswordBytes.Utf8, utf8));
        if (cp949 is not null && checks.All(c => c.Passes(cp949))) passing.Add((PasswordBytes.Cp949, cp949));
        if (passing.Count == 0)
            return new ZipPasswordProbeResult(true, hasAes, hasZipCrypto, PasswordBytes.None) { Entries = entries };

        double bits = checks.Sum(c => c.Bits);

        // 두 후보가 다 통과했거나 근거가 약하면(고른 항목의 ZipCrypto 확인 바이트가 하나뿐 등) 고르지 않은 암호 항목도 본다.
        // 이것은 '덤' 근거라 맞지 않아도 후보를 버리지 않는다(항목마다 암호가 다른 드문 ZIP) — 가려내기와 확인 근거에만 쓴다.
        List<PasswordCheck> extra = new();
        if (passing.Count > 1 || bits < ConfirmBits)
        {
            var targetIndexes = targets.Select(e => e.Index).ToHashSet();
            extra = TryBuildChecks(fs, entries.Where(e =>
                e.IsEncrypted && !IsDirectoryName(e.Name) && !targetIndexes.Contains(e.Index) && IsCheckable(e)));
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

    /// <summary>암호 바이트 하나를 맞춰 보는 검사와 그 검사의 근거 크기(틀린 암호가 통과할 확률 = 2^-Bits).</summary>
    private sealed record PasswordCheck(Func<byte[], bool> Passes, double Bits);

    /// <summary>판정할 수 있는 암호 항목인가(강한 암호화·세기를 모르는 AES·암호 머리보다 짧은 항목은 아님).</summary>
    private static bool IsCheckable(ZipCentralEntry e) =>
        (e.Flags & 0x40) == 0
        && (e.IsAes
            ? e.AesStrength is >= 1 and <= 3 && e.CompressedSize >= WinZipAes.SaltLength(e.AesStrength) + 2 + WinZipAes.AuthCodeLength
            : e.CompressedSize >= 12);

    /// <summary>ZipCrypto 항목은 최대 64개, AES 항목은 최대 3개까지 검사를 만든다(로컬 머리에서 암호 머리·salt 를 읽음).</summary>
    private static List<PasswordCheck> BuildChecks(FileStream fs, IEnumerable<ZipCentralEntry> list)
    {
        var items = list.ToList();
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
            byte[] saltPv = ReadAt(fs, dataStart, saltLen + 2);
            byte[] salt = saltPv[..saltLen], pv = saltPv[saltLen..];
            checks.Add(new PasswordCheck(pw => WinZipAes.PasswordVerifierMatches(pw, salt, pv, strength), 16));
        }
        return checks;
    }

    /// <summary>고르지 않은 항목의 검사(덤 근거). 그 항목의 머리가 깨져 읽을 수 없으면 덤 없이 판정한다.</summary>
    private static List<PasswordCheck> TryBuildChecks(FileStream fs, IEnumerable<ZipCentralEntry> list)
    {
        try { return BuildChecks(fs, list); }
        catch (Exception) { return new List<PasswordCheck>(); }
    }

    private static bool IsDirectoryName(string name) => name.EndsWith('/') || name.EndsWith('\\');

    // ───── 중앙 디렉터리 읽기 ─────

    /// <summary>중앙 디렉터리 전체. 판정할 수 없는 모양이면 null.</summary>
    private static List<ZipCentralEntry>? ReadCentralDirectory(FileStream fs)
    {
        long len = fs.Length;
        if (len < 22) return null;

        // 첫 머리가 ZIP 로컬 머리(또는 빈 ZIP 의 끝 머리)가 아니면 앞에 다른 것이 붙은 파일(SFX 등)이나 분할 조각
        uint first = BitConverter.ToUInt32(ReadAt(fs, 0, 4));
        if (first != 0x04034b50 && first != 0x06054b50) return null;

        int tailLen = (int)Math.Min(len, 22 + 0xFFFF);
        long tailStart = len - tailLen;
        byte[] tail = ReadAt(fs, tailStart, tailLen);
        int eocd = -1;
        for (int i = tailLen - 22; i >= 0; i--)
        {
            if (BitConverter.ToUInt32(tail, i) != 0x06054b50) continue;
            if (i + 22 + BitConverter.ToUInt16(tail, i + 20) <= tailLen) { eocd = i; break; }
        }
        if (eocd < 0) return null;
        long eocdPos = tailStart + eocd;

        long disk = BitConverter.ToUInt16(tail, eocd + 4);
        long cdDisk = BitConverter.ToUInt16(tail, eocd + 6);
        long count = BitConverter.ToUInt16(tail, eocd + 10);
        long cdSize = BitConverter.ToUInt32(tail, eocd + 12);
        long cdOffset = BitConverter.ToUInt32(tail, eocd + 16);
        long cdEnd = eocdPos;

        if (disk == 0xFFFF || cdDisk == 0xFFFF || count == 0xFFFF || cdSize == 0xFFFFFFFF || cdOffset == 0xFFFFFFFF)
        {
            // Zip64: 끝 머리 바로 앞의 위치 표(locator) → Zip64 끝 머리
            if (eocdPos < 20) return null;
            byte[] loc = ReadAt(fs, eocdPos - 20, 20);
            if (BitConverter.ToUInt32(loc, 0) != 0x07064b50) return null;
            long z64Pos = (long)BitConverter.ToUInt64(loc, 8);
            if (z64Pos < 0 || z64Pos + 56 > eocdPos) return null;
            byte[] z = ReadAt(fs, z64Pos, 56);
            if (BitConverter.ToUInt32(z, 0) != 0x06064b50) return null;
            disk = BitConverter.ToUInt32(z, 16);
            cdDisk = BitConverter.ToUInt32(z, 20);
            count = (long)BitConverter.ToUInt64(z, 32);
            cdSize = (long)BitConverter.ToUInt64(z, 40);
            cdOffset = (long)BitConverter.ToUInt64(z, 48);
            cdEnd = z64Pos;
        }

        if (disk != 0 || cdDisk != 0) return null;                          // 분할(.z01 …)
        if (cdOffset < 0 || cdSize < 0 || cdOffset + cdSize != cdEnd) return null;   // 위치를 믿을 수 없음
        if (cdSize > MaxCentralDirectoryBytes || count > cdSize / 46 + 1) return null;

        byte[] cd = ReadAt(fs, cdOffset, (int)cdSize);
        var list = new List<ZipCentralEntry>((int)Math.Min(count, 100_000));
        int p = 0;
        for (int n = 0; n < count; n++)
        {
            if (p + 46 > cd.Length || BitConverter.ToUInt32(cd, p) != 0x02014b50) return null;
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

            string name = KoreanFileNameDecoder.DecodeBytes(cd, nameAt, nameLen);
            list.Add(new ZipCentralEntry(n, name, flags, method, time, crc, csize, usize, offset, aesVersion, aesStrength, aesMethod));
            p = next;
        }
        return list;
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

    internal static byte[] ReadAt(Stream s, long position, int count)
    {
        var b = new byte[count];
        s.Position = position;
        s.ReadExactly(b);
        return b;
    }

    // ───── ZipCrypto(전통 PKWARE 암호) 1바이트 검사 ─────

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    /// <summary>암호 바이트로 12바이트 암호 머리를 풀어 마지막 바이트가 받아들일 값 중 하나인지.</summary>
    internal static bool ZipCryptoCheck(byte[] password, byte[] head12, byte[] accepted)
    {
        uint k0 = 0x12345678, k1 = 0x23456789, k2 = 0x34567890;
        void Update(byte c)
        {
            k0 = CrcTable[(k0 ^ c) & 0xFF] ^ (k0 >> 8);
            k1 = unchecked((k1 + (k0 & 0xFF)) * 134775813 + 1);
            k2 = CrcTable[(k2 ^ (byte)(k1 >> 24)) & 0xFF] ^ (k2 >> 8);
        }
        foreach (byte c in password) Update(c);
        byte last = 0;
        foreach (byte e in head12)
        {
            uint t = (k2 | 2) & 0xFFFF;
            last = (byte)(e ^ (byte)((t * (t ^ 1)) >> 8));
            Update(last);
        }
        return Array.IndexOf(accepted, last) >= 0;
    }
}
