using System.Security.Cryptography;
using System.Text;

namespace Iljip.Tests.TestSupport;

/// <summary>
/// 최소 RAR5 작성기(저장 방식만) — 이 PC·반디집 모두 RAR 을 만들 수 없어(RAR 압축은 WinRAR 전용) RAR 해제·암호 흐름을 시험하려고 만든 합성 파일.
/// 반디집 콘솔(bz.exe t)이 이 파일을 맞게 푸는지 확인하는 테스트가 함께 있다(<c>RarTests.합성_RAR을_반디집도_맞게_푼다</c> — 작성기 자체 검증,
/// 2026-10-12 반디집 7.46 에서 평문·파일 암호·머리 암호·한글 암호 모두 'All OK', 틀린 암호는 'Invalid Password').
/// 구조(RAR 5.0 기술 문서·unrar 기준):
/// <code>
///   서명 52 61 72 21 1A 07 01 00
///   [머리 암호면] 보관 암호 머리(형식 4: 버전 0 · 플래그 1(확인값 있음) · KDF 횟수 log2 · salt 16 · 확인값 12)
///   블록마다: CRC32(머리 크기 칸부터) · 머리 크기(vint) · 형식 · 플래그(1 = 덧붙임 칸, 2 = 데이터 칸) · [덧붙임 크기] · [데이터 크기] · 형식별 칸 · [덧붙임]
///     주 머리(형식 1: 보관 플래그 0) · 파일 머리(형식 2) · 끝(형식 5)
///   파일 머리: 파일 플래그(1 폴더 · 2 유닉스 시각 · 4 CRC) · 원래 크기 · 속성(윈도) · [유닉스 시각 uint32] · [CRC32] · 압축 정보 0(저장) · OS 0 · 이름(UTF-8, '/')
///   시각: 기본은 덧붙임의 파일 시각 기록(형식 3 · 플래그 2 · 윈도 FILETIME), unixTime 이면 파일 머리의 유닉스 시각 칸
///   암호 파일: 덧붙임에 파일 암호 기록(형식 1: 버전 0 · 플래그 1 · KDF · salt · IV · 확인값 12), 데이터는 AES-256-CBC(16바이트로 0 채움)
///   머리 암호면 주 머리부터 모든 머리를 'IV 16 + AES-256-CBC(머리, 16바이트로 0 채움)'로 씀(데이터 칸은 파일 암호 그대로)
/// </code>
/// 키: PBKDF2-HMAC-SHA256(암호 UTF-8, salt, 2^KDF) 32바이트, 확인값 = 같은 계산을 32회 더 한 값을 8바이트로 접은 것 + 그 SHA-256 앞 4바이트.
/// MAC 으로 바꾼 CRC(플래그 2)는 쓰지 않는다 — CRC 는 원래 내용의 CRC32 그대로.
/// </summary>
public static class RawRar5
{
    /// <param name="Data">null 이면 폴더.</param>
    public sealed record Item(string Name, byte[]? Data)
    {
        public bool IsDir => Data is null;
    }

    private static readonly byte[] Signature = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 };

    /// <summary>KDF 반복 횟수(log2). WinRAR 기본값 15.</summary>
    private const byte KdfLog2 = 15;

    /// <param name="password">null 이 아니면 모든 파일을 이 암호로 암호화(폴더는 암호 기록 없음).</param>
    /// <param name="encryptHeaders">true 면 파일 이름까지 암호화(WinRAR '파일 이름 암호화'). <paramref name="password"/> 필요.</param>
    /// <param name="mtimeUtc">모든 항목의 수정 시각(기본 픽스처 시각).</param>
    /// <param name="unixTime">true 면 시각을 파일 머리의 유닉스 시각 칸(초)에, false 면 덧붙임의 '파일 시각' 기록(형식 3, 윈도 FILETIME — WinRAR 기본 '정밀 시각')에 쓴다.</param>
    public static void Write(string path, IEnumerable<Item> items, string? password = null, bool encryptHeaders = false, DateTime? mtimeUtc = null,
                             bool unixTime = false, bool fakeBlake2 = false)
    {
        if (encryptHeaders && password is null) throw new ArgumentException("머리 암호에는 암호가 필요함");
        var rng = new Random(20261012);   // 결정적 salt·IV(같은 입력 → 같은 파일)
        byte[] salt = Bytes(rng, 16);
        byte[]? key = null, check = null;
        if (password is not null) (key, check) = Derive(password, salt);
        DateTime when = mtimeUtc ?? FixtureCatalog.FixedMtimeUtc;
        uint mtime = (uint)new DateTimeOffset(when).ToUnixTimeSeconds();
        // 파일 시각 기록: 크기 · 형식 3 · 플래그 2(수정 시각, 윈도 FILETIME) · FILETIME 8바이트
        byte[] timeRecord = new byte[] { 0x03, 0x02 }.Concat(BitConverter.GetBytes(when.ToFileTimeUtc())).ToArray();
        timeRecord = Vint(timeRecord.Length).Concat(timeRecord).ToArray();

        using var ms = new MemoryStream();
        ms.Write(Signature);

        void WriteHeader(byte[] block)
        {
            if (!encryptHeaders) { ms.Write(block); return; }
            byte[] iv = Bytes(rng, 16);
            ms.Write(iv);
            ms.Write(Aes(key!, iv, block));
        }

        if (encryptHeaders)
        {
            var body = new List<byte>();
            body.AddRange(Vint(0));           // 암호 버전 0 = AES-256
            body.AddRange(Vint(0x0001));      // 확인값 있음
            body.Add(KdfLog2);
            body.AddRange(salt);
            body.AddRange(check!);
            ms.Write(Block(4, body.ToArray(), null, null));
        }

        WriteHeader(Block(1, Vint(0), null, null));   // 주 머리(보관 플래그 0)

        foreach (var it in items)
        {
            byte[] data = it.Data ?? Array.Empty<byte>();
            byte[] name = Encoding.UTF8.GetBytes(it.Name.TrimEnd('/'));
            var body = new List<byte>();
            bool crc = !it.IsDir && !fakeBlake2;
            body.AddRange(Vint((it.IsDir ? 0x0001 : 0) | (crc ? 0x0004 : 0) | (unixTime ? 0x0002 : 0)));
            body.AddRange(Vint(data.Length));
            body.AddRange(Vint(it.IsDir ? 0x10 : 0x20));
            if (unixTime) body.AddRange(BitConverter.GetBytes(mtime));
            if (crc) body.AddRange(BitConverter.GetBytes(Crc32.Compute(data)));
            body.AddRange(Vint(0));            // 압축 정보: 버전 0 · 저장
            body.AddRange(Vint(0));            // 만든 OS: 윈도
            body.AddRange(Vint(name.Length));
            body.AddRange(name);

            byte[] payload = data;
            var extraList = new List<byte>();
            if (password is not null && !it.IsDir)
            {
                byte[] iv = Bytes(rng, 16);
                var rec = new List<byte>();
                rec.AddRange(Vint(0x01));      // 파일 암호 기록
                rec.AddRange(Vint(0));         // 버전 0 = AES-256
                rec.AddRange(Vint(0x0001));    // 확인값 있음
                rec.Add(KdfLog2);
                rec.AddRange(salt);
                rec.AddRange(iv);
                rec.AddRange(check!);
                extraList.AddRange(Vint(rec.Count));
                extraList.AddRange(rec);
                payload = Aes(key!, iv, data);
            }
            if (!unixTime) extraList.AddRange(timeRecord);
            if (fakeBlake2 && !it.IsDir)
            {
                byte[] rec = new byte[] { 0x02, 0x00 }.Concat(Enumerable.Repeat((byte)0x5A, 32)).ToArray();
                extraList.AddRange(Vint(rec.Length));
                extraList.AddRange(rec);
            }
            byte[]? extra = extraList.Count > 0 ? extraList.ToArray() : null;
            WriteHeader(Block(2, body.ToArray(), extra, it.IsDir ? null : payload.Length));
            ms.Write(payload);
        }

        WriteHeader(Block(5, Vint(0), null, null));   // 끝
        File.WriteAllBytes(path, ms.ToArray());
    }

    /// <summary>블록 하나: CRC32 · 머리 크기 · 형식 · 플래그 · [덧붙임 크기] · [데이터 크기] · 형식별 칸 · [덧붙임].</summary>
    private static byte[] Block(int type, byte[] typeBody, byte[]? extra, int? dataSize)
    {
        var body = new List<byte>();
        body.AddRange(Vint(type));
        body.AddRange(Vint((extra is null ? 0 : 0x0001) | (dataSize is null ? 0 : 0x0002)));
        if (extra is not null) body.AddRange(Vint(extra.Length));
        if (dataSize is not null) body.AddRange(Vint(dataSize.Value));
        body.AddRange(typeBody);
        if (extra is not null) body.AddRange(extra);
        byte[] crcInput = Vint(body.Count).Concat(body).ToArray();
        return BitConverter.GetBytes(Crc32.Compute(crcInput)).Concat(crcInput).ToArray();
    }

    /// <summary>RAR5 키와 확인값 12바이트(unrar <c>CryptData::SetKey50</c> 와 같은 계산).</summary>
    private static (byte[] Key, byte[] Check) Derive(string password, byte[] salt)
    {
        byte[] pw = Encoding.UTF8.GetBytes(password);
        int n = 1 << KdfLog2;
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(pw, salt, n, HashAlgorithmName.SHA256, 32);
        byte[] value = Rfc2898DeriveBytes.Pbkdf2(pw, salt, n + 32, HashAlgorithmName.SHA256, 32);
        var psw = new byte[8];
        for (int i = 0; i < value.Length; i++) psw[i % 8] ^= value[i];
        return (key, psw.Concat(SHA256.HashData(psw).Take(4)).ToArray());
    }

    /// <summary>AES-CBC(키 32바이트 = AES-256, 16바이트 = AES-128 · 16바이트 배수로 0 채움, 덧붙임 없음).</summary>
    internal static byte[] Aes(byte[] key, byte[] iv, byte[] plain)
    {
        var padded = new byte[(plain.Length + 15) / 16 * 16];
        plain.CopyTo(padded, 0);
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = key;
        return aes.EncryptCbc(padded, iv, PaddingMode.None);
    }

    private static byte[] Vint(long v)
    {
        var o = new List<byte>();
        do
        {
            byte b = (byte)(v & 0x7F);
            v >>= 7;
            if (v != 0) b |= 0x80;
            o.Add(b);
        } while (v != 0);
        return o.ToArray();
    }

    internal static byte[] Bytes(Random rng, int n)
    {
        var b = new byte[n];
        rng.NextBytes(b);
        return b;
    }
}

/// <summary>
/// 최소 RAR4(RAR 2.9~4.x 형식, WinRAR 6 까지의 기본) 작성기(저장 방식만). <see cref="RawRar5"/> 와 같은 까닭으로 만든 합성 파일이고,
/// 반디집 콘솔로 맞게 풀리는지 확인하는 테스트가 함께 있다.
/// 구조(RAR 2.9 technote·unrar 기준):
/// <code>
///   표시 블록 52 61 72 21 1A 07 00
///   주 머리: CRC16 · 형식 0x73 · 플래그(0x80 = 머리 암호) · 크기 13 · 예약 6
///   파일 머리: CRC16 · 0x74 · 플래그(0x8000 늘 · 0x04 암호 · 0x400 salt · 0x200 유니코드 이름 · 0xE0 폴더) · 머리 크기 ·
///             압축 크기 · 원래 크기 · OS 2(Win32) · CRC32 · DOS 시각(현지 벽시계) · 버전 29 · 방식 0x30(저장) · 이름 길이 · 속성 · 이름 · [salt 8]
///   끝: CRC16 · 0x7B · 0x4000 · 7
/// </code>
/// 유니코드 이름(플래그 0x200)은 'CP949 이름 + 0 + 인코딩된 유니코드'(높은 바이트 하나 + 4글자마다 플래그 0xAA + 글자마다 2바이트 — 가장 단순한 인코딩).
/// unicodeNames = false 면 이름을 CP949 바이트로만 쓴다(옛 WinRAR·한국어 윈도 — 일집 이름 판별기 경로).
/// 암호(RAR 3.x AES-128): 키·IV = SHA-1(암호 UTF-16LE + salt + 회차 3바이트)을 0x40000 번 이어 넣은 값(unrar <c>SetKey30</c>),
/// 데이터는 AES-128-CBC(16바이트로 0 채움). 머리 암호면 주 머리 뒤의 모든 머리를 'salt 8 + AES-128-CBC(머리)'로 쓴다.
/// 짧은 암호만(UTF-16 + salt 가 64바이트 미만 — unrar 의 옛 SHA-1 특이 동작이 끼어들지 않는 길이).
/// </summary>
public static class RawRar4
{
    private static readonly byte[] Marker = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 };

    /// <param name="password">null 이 아니면 모든 파일을 이 암호로 암호화(폴더는 암호 없음).</param>
    /// <param name="encryptHeaders">true 면 파일 이름까지 암호화(WinRAR -hp).</param>
    /// <param name="unicodeNames">ASCII 가 아닌 이름에 유니코드 이름 칸을 쓸지(WinRAR 3.x 이후 기본).</param>
    public static void Write(string path, IEnumerable<RawRar5.Item> items, string? password = null, bool encryptHeaders = false, bool unicodeNames = true)
    {
        if (encryptHeaders && password is null) throw new ArgumentException("머리 암호에는 암호가 필요함");
        if (password is not null && Encoding.Unicode.GetByteCount(password) + 8 >= 64) throw new ArgumentException("암호가 너무 김(합성 작성기 한계)");
        var rng = new Random(20261013);
        byte[] salt = RawRar5.Bytes(rng, 8);
        (byte[] Key, byte[] Iv)? keys = password is null ? null : Derive(password, salt);
        var cp949 = Encoding.GetEncoding(949);
        const uint dosDate = ((2026u - 1980u) << 9) | (1u << 5) | 2u;      // 2026-01-02 (현지 벽시계)
        const uint dosTime = (3u << 11) | (4u << 5) | (6u / 2u);           // 03:04:06
        const uint dos = (dosDate << 16) | dosTime;

        using var ms = new MemoryStream();
        ms.Write(Marker);
        ms.Write(Head(0x73, encryptHeaders ? 0x0080 : 0x0000, new byte[6]));

        void WriteHeader(byte[] head)
        {
            if (!encryptHeaders) { ms.Write(head); return; }
            ms.Write(salt);
            ms.Write(RawRar5.Aes(keys!.Value.Key, keys.Value.Iv, head));
        }

        foreach (var it in items)
        {
            byte[] data = it.Data ?? Array.Empty<byte>();
            string name = it.Name.TrimEnd('/').Replace('/', '\\');
            bool ascii = name.All(c => c < 0x80);
            byte[] nameBytes = cp949.GetBytes(name);
            int flags = 0x8000;
            if (it.IsDir) flags |= 0x00E0;
            if (!ascii && unicodeNames)
            {
                flags |= 0x0200;
                nameBytes = nameBytes.Concat(new byte[] { 0 }).Concat(EncodeUnicode(name)).ToArray();
            }
            bool encrypted = keys is not null && !it.IsDir;
            byte[] payload = data;
            if (encrypted)
            {
                flags |= 0x0004 | 0x0400;
                payload = RawRar5.Aes(keys!.Value.Key, keys.Value.Iv, data);
            }
            var body = new List<byte>();
            body.AddRange(BitConverter.GetBytes((uint)payload.Length));
            body.AddRange(BitConverter.GetBytes((uint)data.Length));
            body.Add(2);                                                  // Win32
            body.AddRange(BitConverter.GetBytes(it.IsDir ? 0u : Crc32.Compute(data)));
            body.AddRange(BitConverter.GetBytes(dos));
            body.Add(29);                                                 // 풀 때 필요한 버전 2.9
            body.Add(0x30);                                               // 저장
            body.AddRange(BitConverter.GetBytes((ushort)nameBytes.Length));
            body.AddRange(BitConverter.GetBytes(it.IsDir ? 0x10u : 0x20u));
            body.AddRange(nameBytes);
            if (encrypted) body.AddRange(salt);
            WriteHeader(Head(0x74, flags, body.ToArray()));
            ms.Write(payload);
        }

        WriteHeader(Head(0x7B, 0x4000, Array.Empty<byte>()));
        File.WriteAllBytes(path, ms.ToArray());
    }

    /// <summary>머리 하나: CRC16(형식부터 끝까지의 CRC32 아래 16비트) · 형식 · 플래그 · 크기 · 본문.</summary>
    private static byte[] Head(byte type, int flags, byte[] body)
    {
        int size = 7 + body.Length;
        var h = new List<byte> { type };
        h.AddRange(BitConverter.GetBytes((ushort)flags));
        h.AddRange(BitConverter.GetBytes((ushort)size));
        h.AddRange(body);
        ushort crc = (ushort)(Crc32.Compute(h.ToArray()) & 0xFFFF);
        return BitConverter.GetBytes(crc).Concat(h).ToArray();
    }

    /// <summary>유니코드 이름 인코딩(unrar <c>DecodeFileName</c> 의 '2바이트 글자' 연산만 씀).</summary>
    private static byte[] EncodeUnicode(string name)
    {
        var o = new List<byte> { 0 };   // 높은 바이트(안 씀)
        for (int i = 0; i < name.Length; i++)
        {
            if (i % 4 == 0) o.Add(0xAA);
            o.Add((byte)name[i]);
            o.Add((byte)(name[i] >> 8));
        }
        return o.ToArray();
    }

    /// <summary>RAR 3.x 키·IV(unrar <c>CryptData::SetKey30</c>).</summary>
    private static (byte[] Key, byte[] Iv) Derive(string password, byte[] salt)
    {
        byte[] raw = Encoding.Unicode.GetBytes(password).Concat(salt).ToArray();
        const int rounds = 0x40000;
        var iv = new byte[16];
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var num = new byte[3];
        for (int i = 0; i < rounds; i++)
        {
            sha.AppendData(raw);
            num[0] = (byte)i; num[1] = (byte)(i >> 8); num[2] = (byte)(i >> 16);
            sha.AppendData(num);
            if (i % (rounds / 16) == 0)
                iv[i / (rounds / 16)] = sha.GetCurrentHash()[19];
        }
        byte[] d = sha.GetHashAndReset();
        var key = new byte[16];
        for (int w = 0; w < 4; w++)
            for (int b = 0; b < 4; b++)
                key[w * 4 + b] = d[w * 4 + 3 - b];
        return (key, iv);
    }
}
