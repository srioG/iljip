using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Iljip.Services;

/// <summary>읽은 그림. <see cref="ByteSize"/> 는 메모리에서 차지하는 대략의 크기(가로 × 세로 × 4바이트) — 미리 보기 캐시 상한에 쓴다.</summary>
public sealed record DecodedImage(ImageSource? Source, int PixelWidth, int PixelHeight, long ByteSize)
{
    /// <summary>원래 그림의 크기(줄여 읽었으면 줄이기 전). 모르면 <see cref="PixelWidth"/>·<see cref="PixelHeight"/> 와 같다.</summary>
    public int OriginalWidth { get; init; } = PixelWidth;
    public int OriginalHeight { get; init; } = PixelHeight;
}

/// <summary>
/// 그림 파일 읽기(W2-04 — I3-7 미리 보기). 진짜는 WIC(<see cref="WicImageDecoder"/>), 시험은 가짜(TestSupport/FakeImageDecoder).
/// 읽은 그림은 다른 스레드에서 써도 되게 얼려(Freeze) 돌려준다. 못 읽으면 예외(코덱 없음 등).
/// </summary>
public interface IImageDecoder
{
    DecodedImage Decode(string path);
}

/// <summary>그림이 너무 커서(화소 수) 읽지 않았다 — 미리 보기는 '미리 보기에는 너무 커요'(IJ3-01).</summary>
public sealed class ImageTooLargeException(int width, int height)
    : Exception($"그림이 너무 커서 읽지 않았어요({width} × {height})")
{
    public int Width { get; } = width;
    public int Height { get; } = height;
}

/// <summary>
/// WIC 로 그림 읽기. 큰 그림이 메모리를 튀게 하지 않도록 프레임 크기를 먼저 읽고(<see cref="BitmapDecoder"/>, 아직 풀지 않음),
/// 4,000만 화소를 넘으면 가로 4,096 이하·4,000만 화소 이하로 줄여 읽는다. <see cref="BitmapCacheOption.OnLoad"/> 로 다 읽은 뒤 파일을 닫으므로
/// 꺼낸 임시 파일을 곧바로 지울 수 있다. WebP 는 Windows 의 WebP 코덱이 있는 PC 에서만 열린다(없으면 예외 → '미리 볼 수 없어요').
/// IJ3-01: 화소가 아주 많은 그림(작은 파일에 큰 그림을 담은 '그림 폭탄' 포함)은 줄여 읽기도 하지 않고 거절한다(<see cref="ImageTooLargeException"/>) —
/// PNG·BMP·TIFF·GIF 는 줄여 읽어도 WIC 가 원래 크기를 한 번 다 풀 수 있어서(2억 5천만 화소면 약 1GB). JPEG 는 풀면서 1/8 까지 줄여 읽어 상한이 더 크다.
/// </summary>
public sealed class WicImageDecoder : IImageDecoder
{
    /// <summary>이보다 화소가 많으면 줄여 읽는다(40MP).</summary>
    public const long MaxPixels = 40_000_000;

    /// <summary>줄여 읽을 때 가로 상한.</summary>
    public const int DownscaleWidth = 4096;

    /// <summary>이보다 화소가 많으면 읽지 않는다(JPEG 밖 — 250MP, 원래 크기로 풀면 약 1GB).</summary>
    public const long RefusePixels = 250_000_000;

    /// <summary>JPEG 는 풀면서 줄여 읽으므로 이보다 많을 때만 거절(1,000MP).</summary>
    public const long RefusePixelsJpeg = 1_000_000_000;

    public DecodedImage Decode(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        // 1) 크기만 먼저(지연 생성 — 화소는 아직 풀지 않음)
        var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
        if (decoder.Frames.Count == 0) throw new NotSupportedException("그림 프레임이 없어요");
        var frame = decoder.Frames[0];
        int w = frame.PixelWidth, h = frame.PixelHeight;
        if (IsTooManyPixels(w, h, decoder is JpegBitmapDecoder)) throw new ImageTooLargeException(w, h);
        int? target = TargetDecodeWidth(w, h);

        // 2) 실제로 읽기(필요하면 줄여서). OnLoad 라 EndInit 에서 다 읽고 스트림을 더 쓰지 않는다.
        fs.Position = 0;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.StreamSource = fs;
        if (target is { } tw) bmp.DecodePixelWidth = tw;
        bmp.EndInit();
        bmp.Freeze();
        return new DecodedImage(bmp, bmp.PixelWidth, bmp.PixelHeight, (long)bmp.PixelWidth * bmp.PixelHeight * 4)
        {
            OriginalWidth = w,
            OriginalHeight = h,
        };
    }

    /// <summary>
    /// 줄여 읽을 가로 화소(줄일 필요 없으면 null). 4,000만 화소 이하가 되는 가로이면서 <see cref="DownscaleWidth"/> 이하
    /// (아주 긴 파노라마처럼 가로가 원래 작으면 원래보다 키우지 않고 화소 수로만 줄임).
    /// </summary>
    /// <summary>읽지 않고 거절할 만큼 화소가 많은가(<see cref="RefusePixels"/>·<see cref="RefusePixelsJpeg"/>).</summary>
    public static bool IsTooManyPixels(int width, int height, bool jpeg) =>
        (long)Math.Max(0, width) * Math.Max(0, height) > (jpeg ? RefusePixelsJpeg : RefusePixels);

    public static int? TargetDecodeWidth(int width, int height)
    {
        if (width <= 0 || height <= 0) return null;
        long pixels = (long)width * height;
        if (pixels <= MaxPixels) return null;
        double scale = Math.Sqrt((double)MaxPixels / pixels);
        int byPixels = Math.Max(1, (int)Math.Floor(width * scale));
        return Math.Min(DownscaleWidth, byPixels);
    }
}
