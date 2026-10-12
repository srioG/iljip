using System.Runtime.InteropServices;
using ComIStream = System.Runtime.InteropServices.ComTypes.IStream;

namespace Iljip.Tests.TestSupport;

/// <summary>
/// Windows 에 들어 있는 IMAPI2FS(디스크 굽기용 파일 시스템 이미지 만들기)로 시험용 ISO 를 만든다 — UDF·Joliet 조합을 고를 수 있어
/// 반디집·파이썬 픽스처(pycdlib, Joliet 있음)로는 못 만드는 'UDF 만'·'Joliet 없는 ISO9660+UDF 다리' 이미지를 시험할 수 있다.
/// 프로세스 안 COM 이라 창이 뜨지 않는다. IMAPI2FS 가 없는 PC 는 <see cref="DiscImageFactAttribute"/> 로 건너뛴다(환경 조건).
/// </summary>
public static class DiscImages
{
    /// <summary>IMAPI2FS FsiFileSystems 값(더해서 씀).</summary>
    public const int Iso9660 = 1, Joliet = 2, Udf = 4;

    /// <summary>이 PC 에 IMAPI2FS 가 등록돼 있는지(Windows 기본 구성요소 — 일부 서버·축소판 Windows 에는 없음).</summary>
    public static bool Available { get; } = Type.GetTypeFromProgID("IMAPI2FS.MsftFileSystemImage", throwOnError: false) is not null;

    internal const string UnavailableReason = "이 PC 에 IMAPI2FS(Windows 디스크 이미지 만들기)가 없어 시험 이미지를 만들 수 없음 — 환경 조건";

    /// <summary><paramref name="sourceDir"/> 폴더(이름째)를 담은 이미지를 만든다.</summary>
    public static void Create(string sourceDir, string imagePath, int fileSystems)
    {
        // IMAPI2FS 는 STA 를 원할 수 있어 전용 STA 스레드에서 만든다(xUnit 작업 스레드는 MTA).
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { CreateCore(sourceDir, imagePath, fileSystems); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new InvalidOperationException("IMAPI2FS 로 시험 이미지를 만들지 못함", error);
    }

    private static void CreateCore(string sourceDir, string imagePath, int fileSystems)
    {
        var type = Type.GetTypeFromProgID("IMAPI2FS.MsftFileSystemImage", throwOnError: true)!;
        dynamic fsi = Activator.CreateInstance(type)!;
        try
        {
            fsi.FileSystemsToCreate = fileSystems;
            fsi.VolumeName = "ILJIP_TEST";
            fsi.Root.AddTree(sourceDir, true);
            dynamic result = fsi.CreateResultImage();
            int blockSize = result.BlockSize;
            int blocks = result.TotalBlocks;
            var stream = (ComIStream)result.ImageStream;
            var buf = new byte[blockSize];
            IntPtr read = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                using var output = File.Create(imagePath);
                for (int i = 0; i < blocks; i++)
                {
                    stream.Read(buf, blockSize, read);
                    int n = Marshal.ReadInt32(read);
                    if (n <= 0) break;
                    output.Write(buf, 0, n);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(read);
                Marshal.ReleaseComObject(stream);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(fsi);
        }
    }
}

/// <summary>IMAPI2FS 가 있을 때만 도는 Fact(환경 조건 — 메운 빈틈을 숨기는 Skip 이 아님, <c>IljipExeFact</c> 와 같은 방식).</summary>
public sealed class DiscImageFactAttribute : FactAttribute
{
    public DiscImageFactAttribute()
    {
        if (!DiscImages.Available) Skip = DiscImages.UnavailableReason;
    }
}

/// <summary>IMAPI2FS 가 있을 때만 도는 Theory(환경 조건).</summary>
public sealed class DiscImageTheoryAttribute : TheoryAttribute
{
    public DiscImageTheoryAttribute()
    {
        if (!DiscImages.Available) Skip = DiscImages.UnavailableReason;
    }
}
