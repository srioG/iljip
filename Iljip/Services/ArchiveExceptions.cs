namespace Iljip.Services;

// 일집 자체 예외 체계. 엔진(SharpCompress·EggDotNet)마다 제각각인 예외를 서비스 경계에서 이 타입들로 바꿔 던진다
// (EngineErrorTranslator, 각 서비스의 TranslateError). Message 가 곧 화면에 보일 쉬운 한국어 문구라 기존 'ex.Message 표시'도
// 그대로 맞고, 엔진의 원래 예외는 InnerException 에 넣어 Logger 로 남긴다.
// 화면(MainViewModel.IsPasswordRelated)은 문구가 아니라 타입으로 '암호 문제'를 판정한다(B5) — ArchivePasswordException 이면 암호를 다시 묻는다.
// W1-C 에는 암호·무결성에 필요한 것만 있다(W1-D 에서 '원본 못 읽음' 추가). 설계표의 나머지(분할 조각 없음·형식 미지원·7z.dll 없음)는 W1-B(7z.dll)와 같이 들어온다.
// → W2-01(1차 W1-B 재작업): 분할 조각 없음·덜 받아짐·형식 미지원·7z.dll 없음·잘림 표시를 아래에 덧붙였다(기존 타입·문구는 그대로).
// 화면·창 없이 풀기의 문구는 ErrorMessages.ToUserMessage 한 곳에서 만든다(일집 예외는 Message 그대로).
// ★ 손상 문구에 '압축 파일이 아닌'·'not archive' 를 넣지 말 것 — 재사용장치가 그 문자열로 '압축 아님'을 분류한다(재사용장치_processor.py 491행).

/// <summary>일집 압축 예외의 공통 부모. <see cref="EntryPath"/> 는 문제가 난 항목(알 때만).</summary>
public abstract class ArchiveException : Exception
{
    protected ArchiveException(string message, Exception? inner = null, string? entryPath = null)
        : base(message, inner)
    {
        EntryPath = entryPath;
    }

    /// <summary>문제가 난 압축 안 항목 경로('/' 구분). 모르면 null.</summary>
    public string? EntryPath { get; }
}

/// <summary>암호 때문에 못 연 경우(화면은 이 타입이면 암호를 다시 묻는다). 해제 서비스는 이 예외면 이번에 만든 파일·폴더를 모두 지운다.</summary>
public abstract class ArchivePasswordException : ArchiveException
{
    protected ArchivePasswordException(string message, bool isHeaderEncrypted, Exception? inner = null, string? entryPath = null)
        : base(message, inner, entryPath)
    {
        IsHeaderEncrypted = isHeaderEncrypted;
    }

    /// <summary>파일 이름(머리)까지 암호가 걸린 압축(7z 헤더 암호)인지.</summary>
    public bool IsHeaderEncrypted { get; }
}

/// <summary>암호가 걸렸는데 암호를 주지 않았다.</summary>
public sealed class PasswordRequiredException : ArchivePasswordException
{
    public const string DefaultMessage = "암호가 걸린 압축 파일이에요. 암호를 넣어 주세요.";
    public const string HeaderMessage = "파일 이름까지 암호가 걸려 있어요. 암호를 넣어 주세요.";

    public PasswordRequiredException(bool isHeaderEncrypted = false, Exception? inner = null, string? entryPath = null)
        : base(isHeaderEncrypted ? HeaderMessage : DefaultMessage, isHeaderEncrypted, inner, entryPath)
    {
    }
}

/// <summary>
/// 준 암호가 맞지 않다. 헤더 암호이거나 손상과 구분이 안 되는 경우(<paramref name="mayBeCorrupt"/> — 7z 처럼 암호 확인값이 없거나,
/// 확인값을 통과한 뒤 데이터·CRC 가 틀린 경우)에는 그 말을 덧붙인다. 맞는 암호를 넣고도 되풀이되면 손상을 떠올리게.
/// </summary>
public sealed class WrongPasswordException : ArchivePasswordException
{
    public const string DefaultMessage = "암호가 맞지 않아요. 다시 넣어 주세요.";
    public const string HeaderNote = " (파일이 손상된 경우에도 이렇게 보일 수 있어요)";

    public WrongPasswordException(bool isHeaderEncrypted = false, Exception? inner = null, string? entryPath = null, bool mayBeCorrupt = false)
        : base(isHeaderEncrypted || mayBeCorrupt ? DefaultMessage + HeaderNote : DefaultMessage, isHeaderEncrypted, inner, entryPath)
    {
        MayBeCorrupt = mayBeCorrupt;
    }

    /// <summary>
    /// 암호 확인값은 통과했는데 풀린 데이터(Deflate·CRC·AES 인증값)가 틀려서 '틀린 암호'로 본 경우 — 손상일 수도 있다.
    /// ZIP 은 판정(<see cref="ZipPasswordProbe"/>)이 암호를 이미 충분히 확인했으면 이 예외 대신 손상 예외로 바꾼다.
    /// </summary>
    public bool MayBeCorrupt { get; }
}

/// <summary>압축 파일이 손상됐다(머리 손상·잘림·데이터 오류).</summary>
public class CorruptArchiveException : ArchiveException
{
    public const string DefaultMessage = "압축 파일이 손상됐어요. 다시 받아 보세요.";

    /// <summary>끝이 잘린 압축(받다 만 파일·복사가 덜 된 파일)이라고 알아냈을 때의 문구(W2-01).</summary>
    public const string TruncatedMessage = "압축 파일의 끝부분이 잘려 있어요. 내려받기나 복사가 끝까지 됐는지 확인해 주세요.";

    public CorruptArchiveException(Exception? inner = null, string? entryPath = null)
        : base(entryPath is null ? DefaultMessage : $"{DefaultMessage} (문제 위치: {entryPath})", inner, entryPath)
    {
    }

    /// <summary>파생 예외가 자기 문구를 쓸 때.</summary>
    protected CorruptArchiveException(string message, Exception? inner, string? entryPath)
        : base(message, inner, entryPath)
    {
    }

    /// <summary>파생 예외가 자기 문구를 쓰고 '잘림'인지도 정할 때(W2-01 — <see cref="VolumeIncompleteException"/> 등).</summary>
    protected CorruptArchiveException(string message, Exception? inner, string? entryPath, bool isTruncated)
        : base(message, inner, entryPath)
    {
        IsTruncated = isTruncated;
    }

    /// <summary>
    /// 끝이 잘린 압축이라고 알아낸 경우(W2-01). 형식별 구조 검사(<see cref="ArchiveCompleteness"/>)·7z.dll 의 '갑자기 끝남'·
    /// 압축된 tar 의 바깥 압축 잘림에서 true. 화면 문구는 <see cref="TruncatedMessage"/>.
    /// </summary>
    public bool IsTruncated { get; }

    /// <summary>끝이 잘린 압축의 손상 예외(<see cref="TruncatedMessage"/>, <see cref="IsTruncated"/> = true).</summary>
    public static CorruptArchiveException Truncated(Exception? inner = null, string? entryPath = null) =>
        new(entryPath is null ? TruncatedMessage : $"{TruncatedMessage} (문제 위치: {entryPath})", inner, entryPath, isTruncated: true);
}

/// <summary>
/// 풀린 내용의 CRC-32 가 압축 파일에 적힌 값과 다르다(B4). 깨진 그 파일은 지우고, 앞서 맞게 끝난 파일은 남긴다.
/// 암호 항목은 판정이 암호를 충분히 확인했을 때만 이 예외다. 확인이 약하면(ZipCrypto 항목 하나뿐 등) 틀린 암호가 1바이트 검사를
/// 우연히 통과한 경우일 수 있어 <see cref="WrongPasswordException"/>(손상일 수도)으로 알린다.
/// </summary>
public sealed class ChecksumMismatchException : CorruptArchiveException
{
    public ChecksumMismatchException(string entryPath, Exception? inner = null)
        : base($"'{entryPath}' 파일 내용이 원본과 달라요(CRC 오류). 압축 파일이 손상됐을 수 있어요.", inner, entryPath)
    {
    }
}

/// <summary>
/// 압축할 원본을 못 읽었다(I0-2) — 단일 파일 형식(GZ·BZ2)이라 그 파일을 건너뛰면 만들 것이 없을 때 던진다.
/// 여러 파일을 담는 형식(ZIP·TAR)은 못 읽은 파일만 건너뛰고 <see cref="Iljip.Models.CompressResult.Skipped"/> 에 적는다
/// (읽는 도중에 막힌 파일은 <see cref="SourceFileOpener.Read"/> 가 이 예외로 알리고, 서비스가 그 파일을 뺀 채 처음부터 다시 쓴다 — 밖으로 나가지 않음).
/// </summary>
public sealed class SourceUnreadableException : ArchiveException
{
    public SourceUnreadableException(string sourcePath, string reason, Exception? inner = null)
        : base($"'{System.IO.Path.GetFileName(sourcePath)}' 파일을 읽을 수 없어요({reason}). 다른 프로그램이 쓰고 있으면 닫고 다시 해 주세요.", inner)
    {
        SourcePath = sourcePath;
        Reason = reason;
    }

    /// <summary>못 읽은 원본 경로.</summary>
    public string SourcePath { get; }

    /// <summary>쉬운 한국어 이유(<see cref="SourceFileOpener"/>).</summary>
    public string Reason { get; }
}

/// <summary>
/// 압축할 원본을 하나도 담지 못했다(I0-2) — 여러 파일을 담는 형식(ZIP·TAR)에서 파일을 모두 건너뛴 경우.
/// 빈 압축 파일을 남기지 않는다(빈 zip 을 그대로 거래처에 보내는 일이 없게 — GZ·BZ2 의 <see cref="SourceUnreadableException"/> 과 같은 처리).
/// 서비스는 이때 압축 파일을 만들지 않는다(덮어쓰려던 기존 파일도 그대로). 화면은 <see cref="Skipped"/> 를 같이 보여 준다.
/// </summary>
public sealed class NothingToCompressException : ArchiveException
{
    public const string DefaultMessage = "읽을 수 있는 파일이 하나도 없어서 압축 파일을 만들지 않았어요.";

    public NothingToCompressException(IReadOnlyList<Iljip.Models.SkippedSource> skipped)
        : base(DefaultMessage)
    {
        Skipped = skipped;
    }

    /// <summary>건너뛴 원본(못 연 파일·못 들어간 폴더 등).</summary>
    public IReadOnlyList<Iljip.Models.SkippedSource> Skipped { get; }
}

/// <summary>형식은 알지만 그 안의 압축 방식을 풀 수 없다.</summary>
public sealed class UnsupportedMethodException : ArchiveException
{
    public UnsupportedMethodException(string method, Exception? inner = null, string? entryPath = null)
        : base($"이 압축 방식({method})은 아직 풀 수 없어요.", inner, entryPath)
    {
        Method = method;
    }

    public string Method { get; }
}

// ───── W2-01(7z.dll 해제 엔진·여는 형식 확대)에서 더한 것 ─────

/// <summary>
/// 나눠진 압축(.7z.001·.zip.001·.partN.rar·.rNN·.volN.egg)의 조각이 없다. <see cref="MissingVolumeName"/> 은 경로 없는 파일 이름.
/// 손상이 아니다 — 조각을 모두 같은 폴더에 두면 풀린다.
/// </summary>
public sealed class VolumeMissingException : ArchiveException
{
    public VolumeMissingException(string missingVolumeName, Exception? inner = null)
        : base($"'{missingVolumeName}' 조각이 없어요. 나눠진 조각을 모두 같은 폴더에 두고 다시 해 주세요.", inner)
    {
        MissingVolumeName = missingVolumeName;
    }

    /// <summary>없는 조각의 파일 이름(경로 없이).</summary>
    public string MissingVolumeName { get; }
}

/// <summary>
/// 나눠진 압축의 조각이 덜 받아졌다(마지막 조각의 끝이 잘림). 숫자 분할은 마지막 조각만 앞 조각보다 작으므로,
/// 모자란데 마지막 조각이 작으면 '다음 조각 없음'이 아니라 '그 조각이 덜 받아짐'이다. 잘림의 한 종류다(<see cref="CorruptArchiveException.IsTruncated"/>).
/// </summary>
public sealed class VolumeIncompleteException : CorruptArchiveException
{
    public VolumeIncompleteException(string volumeName, Exception? inner = null)
        : base($"'{volumeName}' 조각이 덜 받아졌어요. 다시 받아 주세요.", inner, null, isTruncated: true)
    {
        VolumeName = volumeName;
    }

    /// <summary>덜 받아진 조각의 파일 이름(경로 없이).</summary>
    public string VolumeName { get; }
}

/// <summary>
/// 일집이 열 수 없는 형식이다(확장자로도 내용으로도 못 고름, 또는 zstd 처럼 알아보지만 못 푸는 것).
/// 창 없이 풀기(--extract-to)는 이 예외면 종료 코드 2 + 영어 'Unsupported archive type: 확장자'(재사용장치 계약 — App 이 따로 씀).
/// </summary>
public sealed class UnsupportedFormatException : ArchiveException
{
    public UnsupportedFormatException(string extension, Exception? inner = null)
        : base($"일집이 열 수 없는 형식이에요({(string.IsNullOrEmpty(extension) ? "확장자 없음" : extension)}).", inner)
    {
        Extension = extension;
    }

    /// <summary>확장자('.zst' 처럼 점 포함, 없으면 빈 문자열).</summary>
    public string Extension { get; }
}

/// <summary>
/// 7z.dll 을 불러오지 못했고 대신 쓸 엔진도 없다(zipx·iso·cab·lzh·wim·xz·txz). 7z·RAR 은 SharpCompress 로 대신하므로 이 예외가 나지 않는다.
/// 서명 없는 네이티브 DLL 이라 다른 PC 의 보안 프로그램(AppLocker·WDAC·백신)이 막을 수 있다. 원인은 <see cref="Exception.InnerException"/>·로그.
/// </summary>
public sealed class EngineUnavailableException : ArchiveException
{
    public const string DefaultMessage =
        "이 형식을 여는 부품(7z.dll)을 불러오지 못했어요. 보안 프로그램이 막았을 수 있어요. 일집을 다시 설치하거나 담당자에게 알려 주세요.";

    public EngineUnavailableException(Exception? inner = null)
        : base(DefaultMessage, inner)
    {
    }
}
