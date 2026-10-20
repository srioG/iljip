using System.IO;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.GZip;
using Iljip.Models;
using SharpCompress.Archives;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Readers;

namespace Iljip.Services;

/// <summary>
/// GZIP 포맷 — 단일 파일 압축 전용.
/// 해제는 베이스 클래스가 처리(목록·이름은 SharpCompress, 풀린 내용은 확인값을 보는 SharpZipLib — <see cref="OpenEntryData"/>).
/// 압축은 SharpCompress.Compressors.Deflate.GZipStream 사용. 원본 이름(FNAME)은 넣지 않는다(반디집과 같은 형식).
/// 여러 파일이 입력되면 오류로 안내 (.tar.gz는 별도 향후 작업).
/// </summary>
public sealed class GZipArchiveService : SharpCompressArchiveServiceBase
{
    public override IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".gz" };

    /// <summary>
    /// 원본 이름(FNAME)이 없는 .gz(반디집·일집이 만든 것)는 엔진이 이름을 빈 문자열로 준다 →
    /// 압축 파일 이름에서 .gz 를 뗀 이름을 쓴다(반디집 동작). 이름이 있으면 그대로.
    /// </summary>
    protected override string MapEntryKey(string archivePath, string? key) =>
        string.IsNullOrEmpty(key) ? ArchiveNaming.SingleStreamName(archivePath) : key;

    /// <summary>
    /// gzip 서명(1F 8B)이 있으면 형식 판별을 건너뛰고 GZip 단일 스트림으로 연다.
    /// SharpCompress 0.50 의 형식 판별(ArchiveFactory)은 .tar.gz 를 열지 못하고 'Cannot determine…' 으로 실패한다(실측) —
    /// 그래서 .tar.gz 도 .tar.bz2(BZip2ArchiveService)처럼 안쪽 .tar 하나('이름.tar')로 보여 주고 꺼낸다.
    /// (tar 내용까지 한 번에 펼치는 것은 알려진 빈틈 I2-9 — 엔진 단계에서 따로)
    /// </summary>
    protected override IArchive OpenForRead(string archivePath, ReaderOptions options) =>
        HasGzipSignature(archivePath)
            ? SharpCompress.Archives.GZip.GZipArchive.OpenArchive(archivePath, options)
            : base.OpenForRead(archivePath, options);

    private static bool HasGzipSignature(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return fs.ReadByte() == 0x1F && fs.ReadByte() == 0x8B;
        }
        catch { return false; }
    }

    /// <summary>
    /// 풀린 내용은 SharpCompress 대신 SharpZipLib 의 GZipInputStream 으로 읽는다(목록·이름·크기는 그대로 SharpCompress).
    /// SharpCompress 0.50 의 gzip 해제와 .NET GZipStream 은 끝의 확인값(CRC32·원래 크기)을 보지 않고 잘린 파일도 알아채지 못해,
    /// 끝까지 받지 못한 .gz 가 '완료'로 끝나고 절반짜리 파일이 정상 이름으로 남았다(B2 를 고치며 흔한 .gz 가 모두 이 길로 옴).
    /// GZipInputStream 은 멤버마다 CRC32·크기를 맞춰 보고, 잘리면 예외를 내고, 이어 붙인 여러 멤버도 끝까지 풀고,
    /// 정상 멤버 뒤의 쓰레기 바이트(0 채움 등)는 gzip 처럼 무시한다. 예외는 쉬운 한국어 <see cref="InvalidDataException"/> 으로 바꾼다.
    /// </summary>
    protected override Stream OpenEntryData(string archivePath, IArchiveEntry entry)
    {
        if (entry is not SharpCompress.Archives.GZip.GZipArchiveEntry)
            return base.OpenEntryData(archivePath, entry);   // gzip 서명이 없어 다른 형식으로 열린 경우
        var gz = new GZipInputStream(new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read)) { IsStreamOwner = true };
        return new CheckedGzipStream(gz);
    }

    /// <summary>GZipInputStream 의 손상·잘림 예외를 사용자에게 보일 문구로 바꿔 주는 읽기 전용 감싸개.</summary>
    private sealed class CheckedGzipStream : Stream
    {
        private readonly Stream _inner;

        public CheckedGzipStream(Stream inner) => _inner = inner;

        public override int Read(byte[] buffer, int offset, int count)
        {
            try
            {
                return _inner.Read(buffer, offset, count);
            }
            catch (Exception ex) when (ex is SharpZipBaseException or EndOfStreamException)
            {
                throw Damaged(ex);
            }
        }

        /// <summary>
        /// 화면에 '압축 해제 실패:' 뒤에 그대로 보이는 문구. '암호'라는 말을 넣지 않는다 — 넣으면 화면이 암호 문제로 보고 암호 창을 띄운다.
        /// </summary>
        internal static InvalidDataException Damaged(Exception ex)
        {
            string why = ex switch
            {
                EndOfStreamException => "끝부분이 잘려 있어요",
                GZipException g when g.Message.Contains("crc", StringComparison.OrdinalIgnoreCase)
                                  || g.Message.Contains("mismatch", StringComparison.OrdinalIgnoreCase) => "내용 검사값·크기가 원래와 달라요",   // W2-08: 영어 'CRC' 없이
                _ when ex.Message.Contains("EOF", StringComparison.OrdinalIgnoreCase) => "끝부분이 잘려 있어요",
                _ => "압축된 데이터가 깨져 있어요",
            };
            return new InvalidDataException(
                $"이 압축 파일은 손상되었거나 끝까지 받아지지 않았어요({why}). 원본 파일을 다시 받아 보세요.", ex);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    public override Task<CompressResult> CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (options?.HasPassword == true)
                throw new NotSupportedException("GZIP은 암호화를 지원하지 않아요.");

            var srcList = sourcePaths.ToList();
            if (srcList.Count == 0)
                throw new InvalidOperationException("압축할 파일이 없습니다.");

            // GZ는 단일 파일 포맷이라 여러 항목을 담을 수 없다. 조용히 첫 파일만 압축하지 않고 명확히 안내.
            if (srcList.Count > 1)
                throw new InvalidOperationException(
                    "GZIP(.gz)은 파일 하나만 압축할 수 있어요.\n" +
                    "여러 개를 한 번에 압축하려면 ZIP을 사용하거나, 하나로 묶어 .tar.gz로 만들어주세요. (.tar.gz는 추후 지원 예정)");

            string sourceFile = srcList[0];
            if (Directory.Exists(sourceFile))
                throw new InvalidOperationException("GZIP은 단일 파일만 압축할 수 있어요. 폴더는 .tar.gz를 사용해주세요. (추후 지원 예정)");

            // 원본을 압축 파일보다 먼저 연다(I0-2): 쓰는 중이어도 공유를 허락했으면 읽고, 못 열면 빈 .gz 를 만들기 전에 알린다
            // (단일 파일 형식이라 건너뛰면 만들 것이 없다).
            if (!SourceFileOpener.TryOpenRead(sourceFile, out var opened, out var reason))
                throw new SourceUnreadableException(sourceFile, reason!);

            using (var input = opened!)
            {
                string? archiveDir = Path.GetDirectoryName(archivePath);
                if (!string.IsNullOrEmpty(archiveDir))
                    Directory.CreateDirectory(archiveDir);

                long totalBytes = input.Length;
                string entryName = Path.GetFileName(sourceFile);

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = entryName,
                    BytesProcessed = 0,
                    TotalBytes = totalBytes,
                    FilesProcessed = 0,
                    TotalFiles = 1
                });

                // I5-B: 같은 이름 파일이 있으면([바꾸기]) 옆 임시 파일에 다 쓴 뒤에만 바꿔 넣는다(ZIP·TAR 와 같음 — 취소·실패면 기존 파일 그대로)
                OutputFiles.WriteReplacing(archivePath, path =>
                {
                    using (var output = File.Create(path))
                    using (var gz = new GZipStream(output, CompressionMode.Compress, ToGZipLevel(options?.Level), System.Text.Encoding.UTF8))
                    {
                        var buffer = new byte[81920];
                        long copied = 0;
                        int read;
                        while ((read = SourceFileOpener.Read(input, buffer, sourceFile)) > 0)   // 읽는 도중 막히면 쉬운 안내(SourceUnreadableException)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            gz.Write(buffer, 0, read);
                            copied += read;

                            progress?.Report(new ArchiveProgress
                            {
                                CurrentFile = entryName,
                                BytesProcessed = copied,
                                TotalBytes = totalBytes,
                                FilesProcessed = 0,
                                TotalFiles = 1
                            });
                        }
                    }
                    return true;
                });

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = string.Empty,
                    BytesProcessed = totalBytes,
                    TotalBytes = totalBytes,
                    FilesProcessed = 1,
                    TotalFiles = 1
                });
            }
            return CompressResult.Empty;
        }, cancellationToken);
    }

    private static CompressionLevel ToGZipLevel(CompressionLevelOption? opt) => opt switch
    {
        CompressionLevelOption.Store => CompressionLevel.None,
        CompressionLevelOption.Fastest => CompressionLevel.BestSpeed,
        CompressionLevelOption.Normal => CompressionLevel.Default,
        CompressionLevelOption.Maximum => CompressionLevel.BestCompression,
        _ => CompressionLevel.Default
    };
}
