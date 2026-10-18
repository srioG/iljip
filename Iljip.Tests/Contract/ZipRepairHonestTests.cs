using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;
using SharpZip = ICSharpCode.SharpZipLib.Zip;

namespace Iljip.Tests.Contract;

/// <summary>
/// [I4-1] 깨진 ZIP 고치기의 '정직한 보수 모드'와 시험 로그 격리에서 새로 지키는 것:
/// 결과 창(다 살렸을 때만 안내 창, 그 밖은 주의 창), 고치기 한 번에 로그 한 기록(못 살린 이름은 처음 50개까지), 검사의 항목 문제 로그 20개 상한,
/// 시험의 로그가 사용자 로그 폴더에 가지 않음, '목록까지 암호' 표시의 구분(맨 앞 머리만), 흘려 쓴 ZipCrypto(서술자) ZIP, 다음 머리 몇 바이트에서 잘린 ZIP,
/// 뒤 머리가 깨진 폴더를 '못 살린 파일'로 세지 않음.
/// I4-1 검토(망가뜨림 시험이 찾은 것): 살렸다는 파일은 고친 ZIP 에서 다른 프로그램으로도 열림(필요한 판·ZipCrypto 서술자 표시),
/// 엔진이 쓰는 CRC·크기가 0 으로 깨진 ZIP 을 검사가 '문제없어요'·풀기가 0바이트로 넘기지 않음, 한 파일의 암호 머리만 깨지면 맞는 암호에 '암호가 틀렸다'고 하지 않음,
/// 0x7075 유니코드 이름의 글자만 깨지면 이름 바이트의 이름으로 살림(깨진 이름으로 '살렸어요' 하지 않음), 목록과 로컬 머리의 방식·암호 표시가 다르면 '문제없어요'가 아님,
/// Deflate 끝 블록·UTF-8 표시·볼륨 속성이 깨진 파일을 다른 프로그램이 못 여는 채로 '살렸어요' 하지 않음, 끝 머리 디스크·주석 칸만 깨진 한글 암호 ZIP 에 '암호가 틀림' 하지 않음,
/// 엔진이 깨진 크기로 엉뚱한 자리를 찾다 운영 체제에 거절돼도 '알 수 없는 문제'가 아니라 손상.
/// </summary>
public class ZipRepairHonestTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    private static Task<RepairReport> Repair(string src, string dest, string? password = null) =>
        ZipRepairer.Instance.RepairAsync(src, dest, password, null, CancellationToken.None).WaitAsync(Limit);

    private static ushort U16(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
    private static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));

    /// <summary>'문서/견적서N.txt' N개(한글 내용)의 보통 ZIP 바이트와 원래 파일(이름 → 내용).</summary>
    private static (byte[] Bytes, Dictionary<string, byte[]> Files) MakeZip(int count, int size = 3000, bool stored = false)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            for (int i = 0; i < count; i++)
            {
                var sb = new StringBuilder();
                var rnd = new Random(100 + i);
                while (sb.Length < size) sb.Append($"{i}번 견적 줄 {rnd.Next()} 한글 내용\n");
                byte[] data = Encoding.UTF8.GetBytes(sb.ToString());
                string name = $"문서/견적서{i:D2}.txt";
                using (var s = z.CreateEntry(name, stored ? CompressionLevel.NoCompression : CompressionLevel.Optimal).Open()) s.Write(data);
                files[name] = data;
            }
        return (ms.ToArray(), files);
    }

    private static int Eocd(byte[] b)
    {
        for (int i = b.Length - 22; i >= 0; i--)
            if (U32(b, i) == 0x06054b50) return i;
        throw new InvalidDataException("끝 머리 없음");
    }

    private static int CdStart(byte[] b) => (int)U32(b, Eocd(b) + 16);

    /// <summary>로컬 머리 위치들(목록 차례).</summary>
    private static List<int> Heads(byte[] b)
    {
        var heads = new List<int>();
        int e = Eocd(b);
        for (int p = CdStart(b), n = 0; n < U16(b, e + 10); n++)
        {
            heads.Add((int)U32(b, p + 42));
            p += 46 + U16(b, p + 28) + U16(b, p + 30) + U16(b, p + 32);
        }
        return heads;
    }

    /// <summary>오늘 로그 파일 내용(다른 시험이 쓰는 중이어도 읽음 — 없으면 빈 문자열).</summary>
    private static string ReadTodayLog()
    {
        string file = Path.Combine(Logger.LogFolder, $"error-{DateTime.Now:yyyyMMdd}.log");
        if (!File.Exists(file)) return string.Empty;
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(fs, Encoding.UTF8);
        return r.ReadToEnd();
    }

    private static int Count(string hay, string needle)
    {
        int n = 0;
        for (int i = hay.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = hay.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    // ───────────── 결과 창(F2) ─────────────

    /// <summary>
    /// 계약(I4-1 F2): 목록의 모든 파일을 확인해 살렸을 때만 안내 창('N개를 모두 살렸어요.') — 주의 창은 뜨지 않는다.
    /// (다 살리지 못한 것은 주의 창: <see cref="ArchiveTesterTests"/>·<see cref="ZipRepairTests"/>·<see cref="ArchiveTestReview4Tests"/> 의 화면 시험.)
    /// </summary>
    [Fact]
    public async Task 다_살렸을_때만_안내_창으로_알린다()
    {
        using var tmp = new TempDir("i41info");
        var (bytes, files) = MakeZip(4);
        string src = tmp.P("멀쩡한 견적.zip");
        File.WriteAllBytes(src, bytes);
        var ui = new FakeUserInteraction { SavePath = tmp.P("멀쩡한 견적_복구.zip") };
        var vm = new MainViewModel(ui);
        RepairReport? r = null;
        await VmRunner.Run(async () => r = await vm.RepairZipFileAsync(src), "고치기");

        Assert.NotNull(r);
        Assert.True(r!.IsComplete);
        string info = Assert.Single(ui.Infos);
        Assert.Contains($"{files.Count}개를 모두 살렸어요.", info);
        Assert.Empty(ui.Warnings);
        Assert.Empty(ui.Errors);
    }

    // ───────────── 로그 상한 ─────────────

    /// <summary>
    /// 계약(I4-1 로그 상한): 고치기 한 번은 로그에 한 기록만 남긴다(요약 + 못 살린 이름 처음 50개 + '외 N개') — 항목마다·시도마다 쓰지 않는다.
    /// 시험의 로그는 시험 자료 폴더 아래(사용자 %LOCALAPPDATA%\Iljip\logs 가 아님).
    /// </summary>
    [Fact]
    public async Task 고치기_한_번에_로그는_한_기록만_남긴다()
    {
        using var tmp = new TempDir("i41log");
        var (bytes, files) = MakeZip(60, size: 300, stored: true);
        // 60개 모두 데이터 한가운데 한 바이트가 바뀜(목록은 온전 — 목록 모드에서 60개 모두 '내용이 원래와 달라요')
        int cd = CdStart(bytes);
        foreach (int h in Heads(bytes))
        {
            int data = h + 30 + U16(bytes, h + 26) + U16(bytes, h + 28);
            bytes[data + 100] ^= 0x55;
        }
        Assert.Equal(cd, CdStart(bytes));
        string src = tmp.P($"모두 깨짐 {Guid.NewGuid():N}.zip");
        File.WriteAllBytes(src, bytes);

        var r = await Repair(src, tmp.P("모두 깨짐_복구.zip"));

        Assert.True(r.CountKnown);
        Assert.Equal(60, r.LostItems.Count);
        Assert.All(r.LostItems, l => Assert.Equal("내용이 원래와 달라요", l.Reason));
        Assert.Equal("전체 60개 중 하나도 살리지 못했어요.", r.Headline);
        Assert.Null(r.OutputPath);

        Assert.StartsWith(ModuleInit.TestAppDataRoot, Logger.LogFolder, StringComparison.OrdinalIgnoreCase);
        string log = ReadTodayLog();
        Assert.Equal(1, Count(log, src));   // 이 압축 경로가 나오는 기록은 하나
        int at = log.IndexOf($"ZIP 고치기: {src} — {r.Headline}", StringComparison.Ordinal);
        Assert.True(at >= 0, "고치기 요약 기록이 없음");
        int end = log.IndexOf(new string('-', 60), at, StringComparison.Ordinal);
        string block = log[at..end];
        Assert.Equal(50, Count(block, "\n  못 살림: "));
        Assert.Contains("\n  외 10개", block);
    }

    /// <summary>계약(I4-1 로그 상한): 압축 검사는 항목 문제를 처음 20개까지만 자세히(예외 포함) 기록하고, 나머지는 수만 한 줄로 남긴다.</summary>
    [Fact]
    public async Task 검사는_항목_문제를_20개까지만_자세히_기록한다()
    {
        using var tmp = new TempDir("i41tlog");
        var (bytes, _) = MakeZip(25, size: 300, stored: true);
        foreach (int h in Heads(bytes))
        {
            int data = h + 30 + U16(bytes, h + 26) + U16(bytes, h + 28);
            bytes[data + 100] ^= 0x55;
        }
        string src = tmp.P($"문제 많은 압축 {Guid.NewGuid():N}.zip");
        File.WriteAllBytes(src, bytes);

        var report = await ArchiveTestService.Instance.TestAsync(src, null, null, CancellationToken.None).WaitAsync(Limit);

        Assert.False(report.Ok);
        Assert.Equal(25, report.Problems.Count(p => p.Entry.Length > 0));
        string log = ReadTodayLog();
        Assert.Equal(20, Count(log, $"압축 검사 — 항목 문제: {src} / "));
        Assert.Equal(1, Count(log, $"압축 검사 — 그 밖 5개 항목 문제(기록 생략): {src}"));
    }

    /// <summary>
    /// 계약(I4-1 시험 로그 격리): 이 시험 프로세스의 일집 자료 폴더·로그 폴더는 시험 전용 폴더(%TEMP%\IljipTests\appdata-pid) — 사용자 PC 의 자료 폴더는 그대로
    /// %LOCALAPPDATA%\Iljip(재사용장치가 보는 로그 위치)이고 시험은 거기에 쓰지 않는다. 시험이 띄우는 exe 는 환경 변수로 같은 폴더를 받는다.
    /// </summary>
    [Fact]
    public void 시험의_로그는_사용자_로그_폴더에_쓰지_않는다()
    {
        string user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Iljip");
        Assert.Equal(user, AppPaths.DefaultRoot());
        Assert.Equal(ModuleInit.TestAppDataRoot, AppPaths.Root);
        Assert.Equal(Path.Combine(ModuleInit.TestAppDataRoot, "logs"), Logger.LogFolder);
        Assert.False(Logger.LogFolder.StartsWith(user + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        var psi = ModuleInit.ApplyTo(new System.Diagnostics.ProcessStartInfo("Iljip.exe"));
        Assert.Equal(ModuleInit.TestAppDataRoot, psi.Environment[AppPaths.TestAppDataEnvVar]);
    }

    // ───────────── '목록까지 암호' 표시 ─────────────

    /// <summary>
    /// 계약(I4-1): 목록까지 암호로 잠긴 ZIP(맨 앞 로컬 머리에 bit 13)은 '파일 목록까지 암호로 잠겨 있어 고칠 수 없어요.' — 새 ZIP 을 만들지 않는다.
    /// 사슬 가운데 머리의 그 표시는 머리가 깨진 것('파일 머리가 깨졌어요' — 앞 파일은 살림), 목록 기록의 그 표시도 깨진 것(목록 값·로컬 값으로 살리고 새 ZIP 에 옮기지 않음).
    /// </summary>
    [Fact]
    public async Task 목록까지_암호_표시는_맨_앞_머리에서만_고칠_수_없다고_알린다()
    {
        using var tmp = new TempDir("i41ses");
        var (bytes, files) = MakeZip(3);
        var heads = Heads(bytes);
        int cd = CdStart(bytes);

        // 맨 앞 머리(목록 없음)
        byte[] first = bytes[..cd];
        first[heads[0] + 7] |= 0x20;
        string a = tmp.P("목록까지 암호.zip");
        File.WriteAllBytes(a, first);
        var ex = await Svc.Catch(() => Repair(a, tmp.P("목록까지 암호_복구.zip")));
        Assert.Equal(ZipRepairer.ListEncryptedMessage, Assert.IsType<ArchiveEditException>(ex).Message);
        Assert.False(File.Exists(tmp.P("목록까지 암호_복구.zip")));

        // 둘째 머리(목록 없음) — 첫 파일은 살리고 둘째는 머리가 깨진 것
        byte[] middle = bytes[..cd];
        middle[heads[1] + 7] |= 0x20;
        string b = tmp.P("가운데 머리 표시.zip");
        File.WriteAllBytes(b, middle);
        var r = await Repair(b, tmp.P("가운데 머리 표시_복구.zip"));
        Assert.Equal(new[] { "문서/견적서00.txt" }, r.Salvaged);
        Assert.Equal(new[] { new RepairLoss("문서/견적서01.txt", "파일 머리가 깨졌어요") }, r.LostItems);

        // 목록 기록(목록 모드) — 모두 살리고 표시는 옮기지 않음
        byte[] listed = bytes.ToArray();
        listed[cd + 9] |= 0x20;
        string c = tmp.P("목록 기록 표시.zip");
        File.WriteAllBytes(c, listed);
        var r2 = await Repair(c, tmp.P("목록 기록 표시_복구.zip"));
        Assert.Equal($"{files.Count}개를 모두 살렸어요.", r2.Headline);
        Assert.All(ZipInspect.Read(r2.OutputPath!), e => Assert.Equal(0, e.Flags & 0x2000));
    }

    // ───────────── 흘려 쓴 ZipCrypto(서술자) ZIP ─────────────

    /// <summary>되감지 못하는 쓰기 전용 스트림(데이터 서술자를 쓰게).</summary>
    private sealed class ForwardOnly(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }

    /// <summary>
    /// 계약(I4-1): 흘려 쓴 ZipCrypto ZIP(서술자 — 암호 확인 바이트가 시각 윗바이트)은 목록이 있든 없든 맞는 암호로 모두 확인해 살리고(목록이 없으면 '원래 몇 개였는지
    /// 확실하지 않아요'), 암호 없이 고치면 '확인 못 함'으로 옮겨도 고친 ZIP 이 맞는 암호로 원래대로 풀린다(서술자 표시·서술자를 지킴).
    /// 고친 ZIP 은 만든 쪽(SharpZipLib)으로 풀어 본다 — 일집 풀기도 이 모양(흘려 쓴 ZipCrypto)을 원본부터 풀지만(2026-10-18 `StreamedZipHeaders` — `StreamedEncryptedZipTests`),
    /// 여기서는 고친 ZIP 이 다른 프로그램에서도 원래대로 풀리는지를 본다.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 흘려_쓴_ZipCrypto_ZIP도_고친_ZIP이_맞는_암호로_풀린다(bool withCd)
    {
        const string password = "pass-1234";
        using var tmp = new TempDir("i41zc");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var ms = new MemoryStream();
        using (var zos = new SharpZip.ZipOutputStream(new ForwardOnly(ms)) { IsStreamOwner = false, Password = password })
        {
            zos.SetLevel(6);
            for (int i = 0; i < 3; i++)
            {
                string name = $"흘림/암호{i}.txt";
                byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat($"{i} 암호 데이터 ", 500)));
                zos.PutNextEntry(new SharpZip.ZipEntry(name) { IsUnicodeText = true, DateTime = new DateTime(2026, 10, 1, 9, 0, 0) });
                zos.Write(data, 0, data.Length);
                zos.CloseEntry();
                files[name] = data;
            }
            zos.Finish();
        }
        byte[] whole = ms.ToArray();
        Assert.True((U16(whole, 6) & 0x09) == 0x09, "암호 + 서술자 ZIP 이 아님");
        string src = tmp.P("흘린 암호.zip");
        File.WriteAllBytes(src, withCd ? whole : whole[..CdStart(whole)]);
        var original = RepairOracle.Of(files);
        static List<(string Name, string? Sha)> ReadWithSharpZip(string zip, string pw)
        {
            using var zf = new SharpZip.ZipFile(zip) { Password = pw };
            var got = new List<(string Name, string? Sha)>();
            foreach (SharpZip.ZipEntry e in zf)
            {
                if (!e.IsFile) continue;
                using var s = zf.GetInputStream(e);
                using var m = new MemoryStream();
                s.CopyTo(m);
                got.Add((RepairOracle.Nfc(e.Name), TreeAssert.Sha256(m.ToArray())));
            }
            return got;
        }
        Assert.Equal(files.Count, ReadWithSharpZip(WriteTo(tmp.P("기준.zip"), whole), password).Count);   // 기준: 원본은 맞는 암호로 풀림

        // 맞는 암호: 모두 확인
        var r = await Repair(src, tmp.P("암호 넣음.zip"), password);
        Assert.Equal(withCd ? "3개를 모두 살렸어요." : "원래 몇 개였는지 확실하지 않아요 — 3개를 살렸어요.", r.Headline);
        var expect = new RepairOracle.Expect { ListMode = withCd, MustSalvage = files.Keys.ToList() };
        string? f = RepairOracle.Check(original, r, ReadWithSharpZip(r.OutputPath!, password), expect);
        Assert.True(f is null, f);
        Assert.All(ZipInspect.Read(r.OutputPath!), e => Assert.Equal(0x09, e.Flags & 0x09));   // 암호 + 서술자 표시를 지킴(확인 바이트가 시각)
        // 고친 ZIP 은 로컬 머리에도 확인한 CRC·크기를 적어 일집 검사도 통과한다(원본은 머리 값이 0 이라 일집 풀기 엔진이 못 읽음 — 위 설명)
        var tested = await ArchiveTestService.Instance.TestAsync(r.OutputPath!, password, null, CancellationToken.None).WaitAsync(Limit);
        Assert.True(tested.Ok, tested.ToDisplayText());

        // 암호 없이: 확인 못 함으로 옮기고, 고친 ZIP 은 맞는 암호로 원래대로 풀림
        var r2 = await Repair(src, tmp.P("암호 없음.zip"));
        Assert.Empty(r2.Salvaged);
        Assert.Equal(files.Keys.OrderBy(k => k, StringComparer.Ordinal), r2.Unverified.OrderBy(k => k, StringComparer.Ordinal));
        var got = ReadWithSharpZip(r2.OutputPath!, password);
        Assert.Equal(files.Count, got.Count);
        foreach (var (name, sha) in got) Assert.Equal(TreeAssert.Sha256(files[name]), sha);
    }

    private static string WriteTo(string path, byte[] bytes)
    {
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // ───────────── 고친 ZIP 이 다른 프로그램에서도 열림(I4-1 검토) ─────────────

    /// <summary>
    /// 계약(I4-1 검토 — 망가뜨림 시험이 찾은 거짓 성공 둘): '살렸어요'라고 한 파일은 고친 ZIP 에서 다른 프로그램(SharpZipLib)과 일집으로 열린다.
    /// <list type="bullet">
    /// <item>로컬 머리의 '필요한 판' 칸이 깨져도(20 → 29) 그 값을 옮기지 않는다 — 옮기면 SharpZipLib 가 '이 판은 풀 수 없음'으로 거절했다.</item>
    /// <item>ZipCrypto 서술자 항목(암호 확인 바이트가 시각)의 로컬 머리 서술자 표시(bit 3)만 깨져도, 확인한 바이트대로 bit 3·서술자를 남긴다 —
    ///   예전에는 깨진 머리를 따라 bit 3 을 지워 다른 프로그램이 '암호가 틀림'으로 거절했는데도 '모두 살렸어요'였다.</item>
    /// <item>목록 기록의 외부 속성에 '볼륨 이름'(0x08) 표시가 켜지면 옮기지 않는다 — 옮기면 SharpZipLib 가 그 파일을 파일로 보지 않고 건너뛰었다.</item>
    /// </list>
    /// </summary>
    [Theory]
    [InlineData("판")]
    [InlineData("서술자 표시")]
    [InlineData("볼륨 표시")]
    public async Task 살렸다는_파일은_고친_ZIP에서_다른_프로그램으로도_열린다(string damage)
    {
        const string password = "pass-5678";
        using var tmp = new TempDir("i41open");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var ms = new MemoryStream();
        bool encrypted = damage == "서술자 표시";
        using (var zos = new SharpZip.ZipOutputStream(ms) { IsStreamOwner = false })
        {
            if (encrypted) zos.Password = password;
            for (int i = 0; i < 3; i++)
            {
                string name = $"자료/파일{i}.txt";
                byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat($"{i} 열림 확인 데이터 ", 400)));
                zos.PutNextEntry(new SharpZip.ZipEntry(name) { IsUnicodeText = true, DateTime = new DateTime(2026, 10, 1, 9, 0, 0) });
                zos.Write(data, 0, data.Length);
                zos.CloseEntry();
                files[name] = data;
            }
            zos.Finish();
        }
        byte[] b = ms.ToArray();
        int h1 = Heads(b)[1];
        if (encrypted)
        {
            Assert.True((U16(b, h1 + 6) & 0x09) == 0x09, "암호 + 서술자 표시 ZIP 이 아님");
            b[h1 + 6] ^= 0x08;   // 둘째 항목 로컬 머리의 서술자 표시만 지움(목록 기록은 온전)
        }
        else if (damage == "판")
        {
            b[h1 + 4] = 29;      // 둘째 항목 로컬 머리의 '필요한 판'
        }
        else
        {
            int p = CdStart(b);
            p += 46 + U16(b, p + 28) + U16(b, p + 30) + U16(b, p + 32);   // 둘째 기록
            b[p + 38] |= 0x08;   // 외부 속성의 '볼륨 이름' 표시
        }
        string src = WriteTo(tmp.P($"{damage} 깨짐.zip"), b);
        string? pw = encrypted ? password : null;

        var r = await Repair(src, tmp.P($"{damage} 깨짐_복구.zip"), pw);

        Assert.Equal("3개를 모두 살렸어요.", r.Headline);
        // 다른 프로그램(SharpZipLib)으로 모두 원래대로 풀림
        using (var zf = new SharpZip.ZipFile(r.OutputPath!) { Password = pw })
        {
            Assert.Equal(files.Count, (int)zf.Count);
            foreach (SharpZip.ZipEntry e in zf)
            {
                Assert.True(e.Version is 20 or 45 or 51, $"{e.Name}: 필요한 판 {e.Version}");
                Assert.True(e.IsFile, $"{e.Name}: 파일로 보이지 않음(속성 {e.ExternalFileAttributes:X})");
                using var s = zf.GetInputStream(e);
                using var m = new MemoryStream();
                s.CopyTo(m);
                Assert.Equal(files[e.Name], m.ToArray());
            }
        }
        if (encrypted) Assert.All(ZipInspect.Read(r.OutputPath!), e => Assert.Equal(0x09, e.Flags & 0x09));   // 확인 바이트가 시각 — bit 3 을 남김
        // 일집 검사도 통과
        var tested = await ArchiveTestService.Instance.TestAsync(r.OutputPath!, pw, null, CancellationToken.None).WaitAsync(Limit);
        Assert.True(tested.Ok, tested.ToDisplayText());
    }

    // ───────────── 검사·풀기의 거짓 성공(I4-1 검토 — 망가뜨림 시험이 찾음) ─────────────

    /// <summary>
    /// 계약(I4-1 검토): 엔진이 데이터를 읽을 때 쓰는 CRC·크기가 깨져 0 이 되어도 검사가 '문제없어요'라 하지 않고, 풀기가 0바이트 파일을 오류 없이 만들지 않는다.
    /// <list type="bullet">
    /// <item>흘려 쓴 ZIP(서술자)의 로컬 머리 표시 칸이 0 — 엔진은 서술자 표시가 없으니 로컬 머리의 CRC·크기(0)로 읽어 빈 파일이 되고 CRC 0 과도 맞았다.</item>
    /// <item>흘려 쓴 ZIP 의 목록 기록 CRC·크기 칸이 0 — 엔진은 서술자 표시가 있으니 목록 값(0)으로 읽었다.</item>
    /// </list>
    /// 이제 그 항목은 '목록과 실제 내용이 맞지 않는' 손상으로 알리고 풀지 않는다(나머지는 원래대로 풀림 — 고치기를 권함).
    /// </summary>
    [Theory]
    [InlineData("로컬 머리 표시")]
    [InlineData("목록 CRC·크기")]
    public async Task 엔진이_쓰는_CRC_크기가_0으로_깨져도_문제없다고_하지_않는다(string damage)
    {
        using var tmp = new TempDir("i41zero");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(new ForwardOnly(ms), ZipArchiveMode.Create, leaveOpen: true))
            for (int i = 0; i < 3; i++)
            {
                string name = $"흘림/파일{i}.txt";
                byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat($"{i} 흘려 쓴 한글 {i * 7} ", 600)));
                using (var s = z.CreateEntry(name).Open()) s.Write(data);
                files[name] = data;
            }
        byte[] b = ms.ToArray();
        int h1 = Heads(b)[1];
        Assert.True((U16(b, h1 + 6) & 0x08) != 0, "서술자 ZIP 이 아님");
        if (damage == "로컬 머리 표시")
        {
            b[h1 + 6] = 0;
            b[h1 + 7] = 0;
        }
        else
        {
            int p = CdStart(b);
            p += 46 + U16(b, p + 28) + U16(b, p + 30) + U16(b, p + 32);   // 둘째 기록
            for (int k = 16; k < 28; k++) b[p + k] = 0;                   // CRC·압축 크기·원래 크기
        }
        string src = WriteTo(tmp.P($"{damage} 0.zip"), b);

        var report = await ArchiveTestService.Instance.TestAsync(src, null, null, CancellationToken.None).WaitAsync(Limit);
        Assert.False(report.Ok, report.ToDisplayText());
        Assert.Contains(report.Problems, p => p.Entry == "흘림/파일1.txt");
        Assert.True(report.HasDamage);

        string dest = tmp.Dir("풀림");
        var svc = new ArchiveServiceLocator().ResolveByContent(src)!;
        await Assert.ThrowsAnyAsync<CorruptArchiveException>(() => svc.ExtractAsync(src, dest, null).WaitAsync(Limit));
        Assert.False(File.Exists(Path.Combine(dest, "흘림", "파일1.txt")), "깨진 항목이 (0바이트로) 풀림");
        Assert.Equal(files["흘림/파일0.txt"], File.ReadAllBytes(Path.Combine(dest, "흘림", "파일0.txt")));
        Assert.Equal(files["흘림/파일2.txt"], File.ReadAllBytes(Path.Combine(dest, "흘림", "파일2.txt")));
    }

    /// <summary>
    /// 계약(I4-1 검토): 목록 기록과 로컬 머리의 압축 방식·암호 표시가 다르면(엔진은 한쪽만 보고 읽어 내용이 맞아도) 검사가 '문제없어요'라 하지 않는다 —
    /// 목록을 따르는 다른 프로그램은 그 파일을 못 풀거나 다르게 푼다. 그 파일을 문제로 알리고, 암호 틀림으로 알리지 않는다.
    /// </summary>
    [Theory]
    [InlineData("목록 방식")]
    [InlineData("목록 암호 표시")]
    public async Task 목록과_로컬_머리의_방식이나_암호_표시가_다르면_문제없다고_하지_않는다(string damage)
    {
        using var tmp = new TempDir("i41meth");
        var (b, _) = MakeZip(3);
        int p = CdStart(b);
        p += 46 + U16(b, p + 28) + U16(b, p + 30) + U16(b, p + 32);   // 둘째 기록
        Assert.Equal(8, U16(b, p + 10));
        if (damage == "목록 방식") b[p + 10] = 0;   // Deflate(8) → 저장(0)
        else b[p + 8] |= 0x01;                     // 암호 표시(로컬 머리·데이터는 암호 없음)
        string src = WriteTo(tmp.P($"{damage}.zip"), b);

        var report = await ArchiveTestService.Instance.TestAsync(src, null, null, CancellationToken.None).WaitAsync(Limit);

        Assert.False(report.Ok, report.ToDisplayText());
        var problem = Assert.Single(report.Problems, x => x.Entry == "문서/견적서01.txt");
        Assert.NotEqual(ProblemKind.WrongPassword, problem.Kind);
    }

    /// <summary>
    /// 계약(I4-1 검토 — 망가뜨림 시험이 찾은 '다른 프로그램은 못 여는데 살렸어요'): Deflate 데이터의 마지막 블록 표시(BFINAL)만 깨지면 내용·CRC 는 다 맞아도
    /// 압축 데이터가 끝나지 않는다(.NET 풀기는 그냥 끝내지만 SharpZipLib·7-Zip 은 오류). 고치기는 그 파일을 '살렸어요'에 넣지 않는다 — 압축 바이트를 끝까지 풀어
    /// 끝 블록에 닿았는지(SharpZipLib `Inflater.IsFinished`)까지 확인. 나머지는 살린다.
    /// </summary>
    [Fact]
    public async Task Deflate_데이터가_끝나지_않으면_내용이_맞아도_살렸다고_하지_않는다()
    {
        using var tmp = new TempDir("i41fin");
        var (b, files) = MakeZip(3);
        int h1 = Heads(b)[1];
        int dataStart = h1 + 30 + U16(b, h1 + 26) + U16(b, h1 + 28);
        Assert.Equal(1, b[dataStart] & 1);   // 블록 하나(마지막 블록 표시가 켜짐)
        b[dataStart] ^= 1;
        string src = WriteTo(tmp.P("끝 블록 깨짐.zip"), b);

        var r = await Repair(src, tmp.P("끝 블록 깨짐_복구.zip"));

        Assert.True(r.CountKnown);
        Assert.Equal(new[] { "문서/견적서00.txt", "문서/견적서02.txt" }, r.Salvaged);
        Assert.Equal("문서/견적서01.txt", Assert.Single(r.LostItems).Name);
        Assert.Equal("전체 3개 중 2개를 살렸어요.", r.Headline);
        using var zf = new SharpZip.ZipFile(r.OutputPath!);
        foreach (SharpZip.ZipEntry e in zf)
        {
            using var s = zf.GetInputStream(e);
            using var m = new MemoryStream();
            s.CopyTo(m);
            Assert.Equal(files[e.Name], m.ToArray());
        }
    }

    /// <summary>
    /// 계약(I4-1 검토): 목록 없는 ZIP 에서 UTF-8 이름 머리의 UTF-8 표시(bit 11)만 꺼져도 이름 바이트가 올바른 UTF-8 이면 그 이름으로 살리고,
    /// 고친 ZIP 에 UTF-8 표시를 켜 둔다 — 안 켜면 다른 프로그램(SharpZipLib 등)이 옛 코드 페이지로 읽어 보고와 다른 깨진 이름이 된다.
    /// </summary>
    [Fact]
    public async Task UTF8_표시만_꺼진_이름은_고친_ZIP에서_UTF8_표시를_켠다()
    {
        using var tmp = new TempDir("i41utf");
        var (b, files) = MakeZip(3);
        int h1 = Heads(b)[1];
        Assert.True((U16(b, h1 + 6) & 0x0800) != 0, "UTF-8 표시 ZIP 이 아님");
        byte[] broken = b[..CdStart(b)];
        broken[h1 + 7] &= 0xF7;   // 둘째 머리의 bit 11
        string src = WriteTo(tmp.P("UTF-8 표시 꺼짐.zip"), broken);

        var r = await Repair(src, tmp.P("UTF-8 표시 꺼짐_복구.zip"));

        Assert.False(r.CountKnown);
        Assert.Equal(files.Keys.OrderBy(k => k, StringComparer.Ordinal), r.Salvaged.OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(ZipInspect.Read(r.OutputPath!), e => Assert.True(e.Utf8, e.Name));
        using var zf = new SharpZip.ZipFile(r.OutputPath!);
        Assert.Equal(files.Keys.OrderBy(k => k, StringComparer.Ordinal), zf.Cast<SharpZip.ZipEntry>().Select(e => e.Name).OrderBy(k => k, StringComparer.Ordinal));
    }

    /// <summary>
    /// 계약(I4-1 검토 — I2-8 '손상을 암호 틀림으로 알리지 않음'): 반디집이 만든 한글 암호 ZipCrypto ZIP 에서 한 파일의 암호 머리(데이터 앞 12바이트)만 깨지면,
    /// 맞는 암호로 검사했을 때 '암호가 맞지 않아요'로 끝나지 않고 그 파일만 손상으로 알린다(다른 파일들의 확인값이 암호를 충분히 확인함).
    /// 예전엔 모든 항목이 맞아야 암호를 받아서 맞는 암호를 넣어도 '암호가 맞지 않아요. 다시 넣어 주세요.'가 되풀이됐다.
    /// </summary>
    [Fact]
    public async Task 한_파일의_암호_머리만_깨지면_맞는_암호에_암호가_틀렸다고_하지_않는다()
    {
        var fx = FixtureCatalog.Get("zip_zipcrypto_korean_pw");
        using var tmp = new TempDir("i41pw");
        byte[] b = File.ReadAllBytes(fx.FullPath);
        var encrypted = ZipInspect.Read(fx.FullPath).Where(e => e.Encrypted && !e.IsDir && e.CompressedSize >= 12).ToList();
        Assert.True(encrypted.Count >= 4, $"암호 파일이 {encrypted.Count}개뿐");
        // 목록 차례로 머리 위치를 찾아 마지막 암호 파일의 암호 머리 12바이트를 0 으로
        int victimHead = -1;
        string victim = encrypted[^1].Name;
        int e0 = Eocd(b), p = CdStart(b);
        for (int n = 0; n < U16(b, e0 + 10); n++)
        {
            int nl = U16(b, p + 28);
            if (b.AsSpan(p + 46, nl).SequenceEqual(encrypted[^1].NameBytes)) victimHead = (int)U32(b, p + 42);
            p += 46 + nl + U16(b, p + 30) + U16(b, p + 32);
        }
        Assert.True(victimHead >= 0);
        int dataStart = victimHead + 30 + U16(b, victimHead + 26) + U16(b, victimHead + 28);
        for (int k = 0; k < 12; k++) b[dataStart + k] = 0;
        string src = WriteTo(tmp.P("한 파일 암호 머리 깨짐.zip"), b);

        var report = await ArchiveTestService.Instance.TestAsync(src, fx.Password, null, CancellationToken.None).WaitAsync(Limit);

        Assert.False(report.Ok);
        var problem = Assert.Single(report.Problems);
        Assert.Equal(RepairOracle.Nfc(victim), RepairOracle.Nfc(problem.Entry));
        Assert.NotEqual(ProblemKind.WrongPassword, problem.Kind);
        Assert.Equal(fx.ExpectedFiles.Count() - 1, report.CheckedCount);
    }

    /// <summary>
    /// 계약(I4-1 검토 — 망가뜨림 시험이 찾음): 반디집 한글 암호 ZipCrypto ZIP 의 끝 머리 '목록이 시작하는 디스크' 칸이나 주석 길이 칸만 깨져도(나눔 ZIP 이 아님 — 앞 조각 없음)
    /// 맞는 암호로 검사하면 '암호가 맞지 않아요'가 아니다. 예전엔 판정이 디스크 칸을 보고 나눔 ZIP 으로 여기거나 파일 끝을 넘는 주석 길이에 걸려 그만두는 바람에
    /// 한글 암호를 엔진에 UTF-8 로 넘겨 맞는 암호도 틀렸다고 했다. 그 칸들은 엔진처럼 보지 않으므로(IJ3-02 5차의 구조 검사와 같음) 모든 파일을 확인하고 원래대로 푼다.
    /// </summary>
    [Theory]
    [InlineData("디스크 4")]
    [InlineData("디스크 FF00")]
    [InlineData("주석 길이")]
    public async Task 끝_머리의_디스크나_주석_칸만_깨져도_맞는_한글_암호를_틀렸다고_하지_않는다(string damage)
    {
        var fx = FixtureCatalog.Get("zip_zipcrypto_korean_pw");
        using var tmp = new TempDir("i41disk");
        byte[] b = File.ReadAllBytes(fx.FullPath);
        int end = Eocd(b);
        Assert.Equal(0, U16(b, end + 20));   // 주석 없음
        if (damage == "주석 길이") BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(end + 20), 0xFF);   // 파일 끝을 넘는 주석 길이
        else BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(end + 6), damage.EndsWith("FF00") ? (ushort)0xFF00 : (ushort)4);
        string src = WriteTo(tmp.P("디스크 칸 깨짐.zip"), b);

        var report = await ArchiveTestService.Instance.TestAsync(src, fx.Password, null, CancellationToken.None).WaitAsync(Limit);

        Assert.DoesNotContain(report.Problems, x => x.Kind == ProblemKind.WrongPassword);
        Assert.Equal(fx.ExpectedFiles.Count(), report.CheckedCount);
        string dest = tmp.Dir("풀림");
        var svc = new ArchiveServiceLocator().ResolveByContent(src)!;
        await svc.ExtractAsync(src, dest, fx.Password).WaitAsync(Limit);
        foreach (var e in fx.ExpectedFiles)
            Assert.Equal(e.Sha256, TreeAssert.Sha256(File.ReadAllBytes(Path.Combine(dest, e.Path.Replace('/', Path.DirectorySeparatorChar)))));
    }

    /// <summary>
    /// 계약(I4-1 검토 — 망가뜨림 시험이 찾음): 로컬 머리의 ZIP64 압축 크기 칸 맨 윗바이트가 깨져 엔진(SharpCompress)이 파일의 음수 자리로 옮겨 가다
    /// 운영 체제가 '매개 변수가 틀립니다'로 거절해도, 검사는 '알 수 없는 문제가 생겼어요'로 멈추지 않고 그 파일만 손상으로 알린다(나머지는 확인).
    /// 풀기도 손상 오류로 끝난다(사용자 PC 문제로 보이지 않게).
    /// </summary>
    [Fact]
    public async Task 엔진이_깨진_크기로_엉뚱한_자리를_찾아도_손상으로_알린다()
    {
        const string password = "암호1234";
        using var tmp = new TempDir("i41seek");
        using var ms = new MemoryStream();
        using (var zos = new SharpZip.ZipOutputStream(ms) { IsStreamOwner = false, Password = password })
        {
            zos.SetLevel(6);
            for (int i = 0; i < 4; i++)
            {
                byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat($"zc{i} 암호 데이터 {i * 3} ", 150)));
                var e = new SharpZip.ZipEntry($"암호/파일{i}.txt") { IsUnicodeText = true, DateTime = new DateTime(2026, 10, 1, 9, 0, 0) };
                if (i == 1) e.CompressionMethod = SharpZip.CompressionMethod.Stored;
                zos.PutNextEntry(e);
                zos.Write(data, 0, data.Length);
                zos.CloseEntry();
            }
            zos.Finish();
        }
        byte[] b = ms.ToArray();
        int h3 = Heads(b)[3];
        int nameLen = U16(b, h3 + 26), extraLen = U16(b, h3 + 28);
        Assert.True(extraLen >= 20 && U16(b, h3 + 30 + nameLen) == 0x0001, "로컬 머리에 ZIP64 칸이 없음");
        b[h3 + 30 + nameLen + 19] ^= 0xC9;   // ZIP64 칸의 압축 크기 맨 윗바이트
        string src = WriteTo(tmp.P("ZIP64 크기 깨짐.zip"), b);

        var report = await ArchiveTestService.Instance.TestAsync(src, password, null, CancellationToken.None).WaitAsync(Limit);

        Assert.False(report.Ok);
        var problem = Assert.Single(report.Problems, x => x.Entry == "암호/파일3.txt");
        Assert.Equal(ProblemKind.Corrupt, problem.Kind);
        Assert.Equal(3, report.CheckedCount);
        var svc = new ArchiveServiceLocator().ResolveByContent(src)!;
        await Assert.ThrowsAnyAsync<CorruptArchiveException>(() => svc.ExtractAsync(src, tmp.Dir("풀림"), password).WaitAsync(Limit));
    }

    // ───────────── 다음 머리 몇 바이트에서 잘림 ─────────────

    /// <summary>
    /// 계약(I4-1 훑기 모드): 받다 만 ZIP 이 다음 기록 머리의 1~3바이트('P'·'PK'·'PK\x03')에서 끊겼으면 앞 파일의 끝은 기록 경계 — 앞 파일을 모두 살린다
    /// (끝 뒤가 서명의 앞부분이고 바로 파일 끝).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task 다음_머리_몇_바이트에서_잘려도_앞_파일을_살린다(int keep)
    {
        using var tmp = new TempDir("i41cut");
        var (bytes, _) = MakeZip(4);
        var heads = Heads(bytes);
        string src = tmp.P("받다 만.zip");
        File.WriteAllBytes(src, bytes[..(heads[2] + keep)]);

        var r = await Repair(src, tmp.P("받다 만_복구.zip"));

        Assert.False(r.CountKnown);
        Assert.Equal(new[] { "문서/견적서00.txt", "문서/견적서01.txt" }, r.Salvaged);
        Assert.Empty(r.LostItems);
        Assert.Equal(0, r.UnconfirmedPieces);
        Assert.Equal("원래 몇 개였는지 확실하지 않아요 — 2개를 살렸어요.", r.Headline);
    }

    // ───────────── 뒤 머리가 깨진 폴더 ─────────────

    /// <summary>
    /// 계약(I4-1 훑기 모드): 폴더 머리(데이터 없음) 바로 뒤의 머리가 깨졌으면 폴더는 넣되 그 뒤는 믿지 않는다 — 폴더를 '못 살린 파일'로 세지 않고,
    /// 깨진 뒤 머리 너머의 머리는 조각 수로만 알린다.
    /// </summary>
    [Fact]
    public async Task 뒤_머리가_깨진_폴더를_못_살린_파일로_세지_않는다()
    {
        using var tmp = new TempDir("i41dir");
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var s = z.CreateEntry("앞 파일.txt").Open()) s.Write(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("앞 내용 ", 200))));
            z.CreateEntry("자료 폴더/");
            using (var s = z.CreateEntry("자료 폴더/뒤 파일.txt").Open()) s.Write(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("뒤 내용 ", 200))));
            using (var s = z.CreateEntry("끝 파일.txt").Open()) s.Write(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("끝 내용 ", 200))));
        }
        byte[] b = ms.ToArray();
        var heads = Heads(b);
        byte[] broken = b[..CdStart(b)];
        broken[heads[2] + 1] ^= 0xFF;   // 폴더 바로 뒤 머리의 서명
        string src = tmp.P("폴더 뒤 깨짐.zip");
        File.WriteAllBytes(src, broken);

        var r = await Repair(src, tmp.P("폴더 뒤 깨짐_복구.zip"));

        Assert.False(r.CountKnown);
        Assert.Equal(new[] { "앞 파일.txt" }, r.Salvaged);
        Assert.Empty(r.LostItems);
        Assert.Equal(1, r.UnconfirmedPieces);   // 끝 파일 머리
        Assert.Contains(ZipInspect.Read(r.OutputPath!), e => e.Name == "자료 폴더/");
    }

    // ───────────── 0x7075 유니코드 이름의 글자 깨짐(I4-1 검토 — 망가뜨림 시험이 찾음) ─────────────

    /// <summary>
    /// 반디집처럼 이름 바이트는 CP949, 0x7075 에 UTF-8 이름(CRC 는 이름 바이트의 것)을 둔 저장(stored) 항목 하나짜리 ZIP.
    /// <paramref name="withCd"/> 가 거짓이면 끝의 목록 없이(받다 만 모양 — 훑기 모드).
    /// </summary>
    private static byte[] Cp949NameZip(byte[] nameBytes, string unicodeName, byte[] data, bool withCd)
    {
        var crc = new ICSharpCode.SharpZipLib.Checksum.Crc32();
        crc.Update(nameBytes);
        uint nameCrc = (uint)crc.Value;
        crc.Reset();
        crc.Update(data);
        uint dataCrc = (uint)crc.Value;
        byte[] u = Encoding.UTF8.GetBytes(unicodeName);
        var extra = new byte[9 + u.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(extra, 0x7075);
        BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(2), (ushort)(5 + u.Length));
        extra[4] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(extra.AsSpan(5), nameCrc);
        u.CopyTo(extra, 9);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        void Common()
        {
            w.Write((ushort)20); w.Write((ushort)0); w.Write((ushort)0);    // 필요한 판·표시(UTF-8 표시 없음)·저장
            w.Write((ushort)0x4800); w.Write((ushort)0x5B41);                // 시각·날짜
            w.Write(dataCrc); w.Write(data.Length); w.Write(data.Length);
            w.Write((ushort)nameBytes.Length); w.Write((ushort)extra.Length);
        }
        w.Write(0x04034b50u); Common(); w.Write(nameBytes); w.Write(extra); w.Write(data);
        if (withCd)
        {
            long cd = ms.Position;
            w.Write(0x02014b50u); w.Write((ushort)20); Common();
            w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0); w.Write(0x20u); w.Write(0u);   // 설명·디스크·내부 속성·외부 속성·위치
            w.Write(nameBytes); w.Write(extra);
            long cdEnd = ms.Position;
            w.Write(0x06054b50u); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)1);
            w.Write((uint)(cdEnd - cd)); w.Write((uint)cd); w.Write((ushort)0);
        }
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// 계약(I4-1 검토 — 망가뜨림 시험이 찾은 '틀린 이름으로 살렸어요'): 0x7075 의 CRC 는 이름 바이트만 덮으므로, 그 안의 UTF-8 글자가 깨져도
    /// (예: '견적서' → '견적쐜') CRC 는 맞는다. 이름 바이트(CP949)로 다시 적어 보아 글자가 다르면 0x7075 를 쓰지 않는다 — 보고 이름도 고친 ZIP 의 이름도
    /// 이름 바이트의 '자료/견적서_2026.txt' 이고, 깨진 글자를 고친 ZIP 에 옮기지 않는다(옮기면 반디집 같은 프로그램이 깨진 이름으로 보여 줌).
    /// 이름 바이트에 없는 글자(이모지 → '?' 하나 또는 '??')를 담은 온전한 0x7075 는 예전대로 쓴다.
    /// </summary>
    [Theory]
    [InlineData("글자 깨짐", true)]
    [InlineData("글자 깨짐", false)]
    [InlineData("없는 글자 ?", false)]
    [InlineData("없는 글자 ??", true)]
    public async Task 유니코드_이름의_글자가_깨지면_이름_바이트의_이름으로_살린다(string kind, bool withCd)
    {
        using var tmp = new TempDir("i41u");
        var cp949 = Encoding.GetEncoding(949);
        byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("견적 내용 한 줄\n", 50)));
        bool garbled = kind == "글자 깨짐";
        byte[] nameBytes = garbled ? cp949.GetBytes("자료/견적서_2026.txt")
            : cp949.GetBytes("자료/보고서").Concat(Encoding.ASCII.GetBytes(kind.EndsWith("??") ? "??.txt" : "?.txt")).ToArray();
        string unicode = garbled ? "자료/견적쐜_2026.txt" : "자료/보고서😀.txt";
        string expected = garbled ? "자료/견적서_2026.txt" : "자료/보고서😀.txt";
        string file = $"{kind.Replace("?", "물음표")} {withCd}";   // '?' 는 Windows 파일 이름에 못 씀
        string src = WriteTo(tmp.P(file + ".zip"), Cp949NameZip(nameBytes, unicode, data, withCd));

        var r = await Repair(src, tmp.P(file + "_복구.zip"));

        Assert.Equal(withCd, r.CountKnown);
        Assert.Equal(new[] { expected }, r.Salvaged);
        Assert.Empty(r.LostItems);
        var e = Assert.Single(ZipInspect.Read(r.OutputPath!));
        Assert.Equal(nameBytes, e.NameBytes);   // 이름 바이트는 그대로
        Assert.Equal(data, e.Data);
        byte[] output = File.ReadAllBytes(r.OutputPath!);
        if (garbled)
        {
            Assert.Equal(expected, e.Name);
            Assert.Equal(-1, output.AsSpan().IndexOf(Encoding.UTF8.GetBytes("견적쐜")));   // 깨진 글자를 옮기지 않음
        }
        else
        {
            Assert.True(output.AsSpan().IndexOf(Encoding.UTF8.GetBytes(unicode)) >= 0, "온전한 0x7075 이름을 버림");
        }
    }
}
