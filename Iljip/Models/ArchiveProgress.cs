namespace Iljip.Models;

/// <summary>
/// 압축/해제 진행률 보고용 모델.
/// </summary>
public sealed class ArchiveProgress
{
    public string CurrentFile { get; init; } = string.Empty;
    public long BytesProcessed { get; init; }
    public long TotalBytes { get; init; }
    public int FilesProcessed { get; init; }
    public int TotalFiles { get; init; }

    public double Percentage =>
        TotalBytes <= 0 ? 0 : (double)BytesProcessed / TotalBytes * 100.0;
}
