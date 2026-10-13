using System.Buffers.Binary;
using System.IO;
using System.Text;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>
/// 자동 풀림 exe 만들기(W2-10, I3-1 — 받는 사람에게 일집·반디집이 없어도 두 번 눌러 풀리는 파일).
/// <para>
/// 방식: 일집 자체 스텁(<c>Iljip.SfxStub</c> — .NET Framework 4.6.2, 한국어 화면, 실행 파일 옆 <c>sfx\Iljip.SfxStub.exe</c>)을 앞에 두고
/// 보통 ZIP(<see cref="ZipArchiveService"/> 와 같은 규칙 — 수준·AES-256/ZipCrypto 암호·빼기 목록·잠긴 원본 건너뛰기)을 바이트 그대로 붙인 뒤
/// 꼬리 24바이트(<c>'ILJIPSFX'</c> + 데이터 시작 위치 8 + 데이터 길이 8, 리틀 엔디언)를 단다:
/// <c>[스텁 exe][ZIP][꼬리]</c>. 7-Zip 자동 풀림 모듈(SharpSevenZipSfx — 7-Zip 4.65(2009)·7zSFX 1.4(2010), 영어 화면·x86)을 쓰지 않은 까닭은 HANDOFF 'W2-10'.
/// </para>
/// 순서: ① 만들 exe 옆 숨김 임시 파일(<c>.~이름.exe.8글자.tmp</c>)에 ZIP 을 다 만들고 ② 다 만들 exe 가 4GB 를 넘지 않는지 보고(<see cref="MaxExeBytes"/> —
/// 넘으면 <see cref="SfxTooLargeException"/>, W2-10 검증 지적 2차) ③ 다른 임시 파일에 스텁 + ZIP + 꼬리를 이어 쓰고
/// ④ 다 맞을 때만 이름.exe 로 옮긴다(덮어쓰기면 한 번에 바꿈). 실패·취소면 임시 파일을 지워 반쪽 exe 가 남지 않고 덮어쓰려던 기존 파일은 그대로다.
/// 일집 안에서는 <see cref="SfxArchiveService"/> 가 꼬리로 알아보고 ZIP 부분만 연다(목록·풀기·'다 만들고 시험하기').
/// 꼬리 찾기는 스텁과 같은 공유 소스(<see cref="SfxTrailer"/> — 나중에 코드 서명을 해서 끝에 서명 표가 붙은 exe 도 받음).
/// </summary>
public static class SfxBuilder
{
    /// <summary>꼬리 머리 글자(8바이트 ASCII).</summary>
    public const string TrailerMagic = SfxTrailer.Magic;

    /// <summary>꼬리 길이: 머리 8 + 데이터 시작 위치 8 + 데이터 길이 8.</summary>
    public const int TrailerLength = SfxTrailer.Length;

    /// <summary>
    /// 만든 exe 의 가장 큰 크기: 4GB − 1바이트(4,294,967,295). Windows 는 4GB(2^32 바이트) 이상인 실행 파일을 시작하지 않는다 —
    /// 받는 사람이 두 번 누르면 '이 앱은 PC 에서 실행할 수 없습니다'(CreateProcess 오류 193). 2026-10-14 이 PC 실측(성긴 파일로 스텁 + 꼬리):
    /// 4,294,967,295 바이트는 시작했고 4,294,967,296 바이트부터 거절(W2-10 검증 지적 2차 — 반디집도 4GB 를 넘으면 경고, 기능 대조 B-010).
    /// </summary>
    public const long MaxExeBytes = uint.MaxValue;

    /// <summary>지금 쓰는 한도(<see cref="MaxExeBytes"/> — 시험은 <see cref="LimitForTests"/> 로 작게).</summary>
    internal static long ExeLimit => LimitOverride.Value ?? MaxExeBytes;

    // 계약 시험용: 4GB 를 실제로 만들지 않고 한도를 작게(이 비동기 흐름 안에서만 — SimulateMissingForTests 와 같은 모양)
    private static readonly AsyncLocal<long?> LimitOverride = new();

    /// <summary>테스트 전용: Dispose 할 때까지 이 흐름에서 exe 한도를 <paramref name="maxExeBytes"/> 로 본다(만들기 뒤 확인·만들기 전 어림 모두).</summary>
    internal static IDisposable LimitForTests(long maxExeBytes)
    {
        LimitOverride.Value = maxExeBytes;
        return new RestoreLimit();
    }

    private sealed class RestoreLimit : IDisposable
    {
        public void Dispose() => LimitOverride.Value = null;
    }

    /// <summary>만들기 전 어림(원본 크기 합 — 압축하기 전)을 재는 동안의 진행 창 문구.</summary>
    internal const string StageMeasuring = "원본 크기를 재는 중";

    /// <summary>
    /// 만들기 전 어림: 원본(빼기 목록을 뺀 파일들)의 크기 합(압축하기 전)이 한도를 넘는가 — 그러면 압축해서 4GB 아래로 줄지 않는 한 못 만든다
    /// (사진·영상처럼 이미 압축된 파일은 거의 줄지 않음). 넘는다고 해도 만들기는 할 수 있다(줄면 만들어짐 — 확실한 판정은 만들기 뒤 <see cref="CreateAsync(IEnumerable{string}, string, CompressionOptions?, IProgress{ArchiveProgress}?, CancellationToken)"/>).
    /// </summary>
    internal static bool MayExceedLimit(long sourceBytes) => sourceBytes > ExeLimit;

    /// <summary>원본의 크기 합(빼기 목록 적용 — 압축에 담길 파일만). 훑는 동안 취소를 본다. 못 여는 폴더는 0 으로 셈(어림).</summary>
    internal static long SourceBytes(IEnumerable<string> sources, IFileFilter? exclude, CancellationToken cancellationToken) =>
        SourceCollector.Collect(sources, exclude, cancellationToken).TotalBytes;

    /// <summary>실행 파일 폴더 기준 스텁 위치.</summary>
    public const string StubRelativePath = @"sfx\Iljip.SfxStub.exe";

    /// <summary>만들기 마지막 단계(스텁 + ZIP 을 잇는 동안)의 진행 창 문구.</summary>
    internal const string StageComposing = "자동 풀림 exe 로 묶는 중";

    /// <summary>스텁 경로(일집 실행 파일 옆 sfx 폴더).</summary>
    public static string StubPath => Path.Combine(AppContext.BaseDirectory, StubRelativePath);

    /// <summary>자동 풀림 exe 를 만들 수 있는가(스텁이 있음) — 압축 창의 형식 목록에 '자동 풀림 EXE' 를 넣을지.</summary>
    public static bool IsAvailable
    {
        get
        {
            if (SimulatedMissing.Value) return false;
            try { return File.Exists(StubPath); }
            catch { return false; }
        }
    }

    // 계약 테스트용: 스텁이 없는 설치(백신이 지움 등)를 흉내 낸다(이 비동기 흐름 안에서만 — SevenZipLibrary.SimulateUnavailableForTests 와 같은 모양).
    private static readonly AsyncLocal<bool> SimulatedMissing = new();

    /// <summary>테스트 전용: Dispose 할 때까지 이 흐름에서 스텁이 '없는 것'으로 본다(<see cref="IsAvailable"/> 만 — 만들기는 경로를 직접 줘서 시험).</summary>
    internal static IDisposable SimulateMissingForTests()
    {
        SimulatedMissing.Value = true;
        return new Restore();
    }

    private sealed class Restore : IDisposable
    {
        public void Dispose() => SimulatedMissing.Value = false;
    }

    /// <summary>
    /// <paramref name="sourcePaths"/> 로 자동 풀림 exe(<paramref name="exePath"/>)를 만든다. 나누기(<see cref="CompressionOptions.SplitBytes"/>)는 쓰지 않는다.
    /// 결과의 건너뛴 원본·뺀 파일은 ZIP 과 같다. 스텁이 없으면 <see cref="SfxStubMissingException"/>(아무것도 만들지 않음).
    /// </summary>
    public static Task<CompressResult> CreateAsync(
        IEnumerable<string> sourcePaths,
        string exePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => CreateAsync(sourcePaths, exePath, options, progress, cancellationToken, StubPath);

    /// <summary><see cref="CreateAsync(IEnumerable{string}, string, CompressionOptions?, IProgress{ArchiveProgress}?, CancellationToken)"/> — 스텁 경로를 정해서(시험: 없는 스텁).</summary>
    internal static Task<CompressResult> CreateAsync(
        IEnumerable<string> sourcePaths,
        string exePath,
        CompressionOptions? options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken,
        string stubPath)
    {
        options ??= CompressionOptions.Default;
        var sources = sourcePaths.ToList();
        return Task.Run(async () =>
        {
            byte[] stub = ReadStub(stubPath);
            string target = Path.GetFullPath(exePath);
            string folder = Path.GetDirectoryName(target) ?? throw new IOException("만들 곳을 알 수 없어요: " + exePath);
            Directory.CreateDirectory(folder);

            // 나누기는 없다(자동 풀림은 한 파일) — 나머지 엔진 옵션은 그대로
            var zipOptions = new CompressionOptions
            {
                Level = options.Level,
                Password = options.Password,
                Encryption = options.Encryption,
                ExcludeFilter = options.ExcludeFilter,
            };

            string tempZip = OutputFiles.TempSibling(target);
            try
            {
                var result = await ZipArchiveService.CompressForSfxAsync(sources, tempZip, zipOptions, alsoSkip: target, progress, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                // 다 만들 exe 가 4GB 를 넘으면 Windows 가 실행하지 않는다 — 잇기 전에 멈춘다(임시 ZIP 은 아래 finally 가 지움, 덮어쓰려던 exe 는 그대로)
                long exeLength = stub.LongLength + new FileInfo(tempZip).Length + TrailerLength;
                if (exeLength > ExeLimit) throw new SfxTooLargeException(exeLength);
                progress?.Report(new ArchiveProgress { CurrentFile = StageComposing, IsStage = true });

                string tempExe = OutputFiles.TempSibling(target);
                try
                {
                    Compose(stub, tempZip, tempExe, cancellationToken);
                    if (File.Exists(target)) OutputFiles.ReplaceExisting(tempExe, target);
                    else File.Move(tempExe, target);
                }
                catch
                {
                    TryDelete(tempExe);
                    throw;
                }
                return result;
            }
            finally
            {
                TryDelete(tempZip);
            }
        }, cancellationToken);
    }

    /// <summary>스텁 바이트(실행 파일 'MZ' 로 시작하고 꼬리가 붙지 않은 것). 없거나 못 읽으면 <see cref="SfxStubMissingException"/>.</summary>
    internal static byte[] ReadStub(string stubPath)
    {
        byte[] stub;
        try
        {
            stub = File.ReadAllBytes(stubPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new SfxStubMissingException(ex);
        }
        if (stub.Length < 1024 || stub[0] != (byte)'M' || stub[1] != (byte)'Z')
            throw new SfxStubMissingException(new InvalidDataException("스텁이 실행 파일이 아님: " + stubPath));
        using (var ms = new MemoryStream(stub, writable: false))
        {
            if (TryReadTrailer(ms, out _, out _))
                throw new SfxStubMissingException(new InvalidDataException("스텁에 이미 데이터가 붙어 있음: " + stubPath));
        }
        return stub;
    }

    /// <summary>스텁 + ZIP 파일 + 꼬리를 <paramref name="outPath"/>(새 파일)에 잇는다. 취소는 64KB 마다 본다.</summary>
    internal static void Compose(byte[] stub, string zipPath, string outPath, CancellationToken cancellationToken)
    {
        using var output = new FileStream(outPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16);
        output.Write(stub, 0, stub.Length);
        long zipLength;
        using (var zip = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
        {
            zipLength = zip.Length;
            var buffer = new byte[1 << 16];
            int n;
            while ((n = zip.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, n);
            }
        }
        output.Write(BuildTrailer(stub.Length, zipLength));
        output.Flush();
    }

    /// <summary>꼬리 24바이트.</summary>
    public static byte[] BuildTrailer(long dataStart, long dataLength)
    {
        var t = new byte[TrailerLength];
        Encoding.ASCII.GetBytes(TrailerMagic, 0, 8, t, 0);
        BinaryPrimitives.WriteInt64LittleEndian(t.AsSpan(8), dataStart);
        BinaryPrimitives.WriteInt64LittleEndian(t.AsSpan(16), dataLength);
        return t;
    }

    /// <summary>
    /// 일집 자동 풀림 꼬리가 있고 위치가 맞는가(머리 글자 + 데이터 시작·길이가 꼬리 끝과 맞음 — 꼬리는 파일 끝, 코드 서명을 했으면 서명 표 바로 앞:
    /// 스텁과 같은 규칙 <see cref="SfxTrailer.TryLocate"/>). 맞으면 ZIP 데이터의 시작·길이. 스트림 위치는 바뀐다. 못 읽으면 false.
    /// </summary>
    public static bool TryReadTrailer(Stream s, out long dataStart, out long dataLength)
    {
        dataStart = dataLength = 0;
        try
        {
            return SfxTrailer.TryLocate(s, out dataStart, out dataLength);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException)
        {
            dataStart = dataLength = 0;
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 임시 파일 정리 실패는 무시(숨김 '.~' 파일 — 다음에 같은 이름으로 만들지 않음)
        }
    }
}
