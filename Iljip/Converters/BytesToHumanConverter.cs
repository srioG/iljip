using System.Globalization;
using System.Windows.Data;

namespace Iljip.Converters;

/// <summary>바이트를 KB/MB/GB로 사람이 읽기 좋게 변환.</summary>
public sealed class BytesToHumanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not long bytes) return string.Empty;
        if (bytes < 0) return string.Empty;

        const double KB = 1024;
        const double MB = KB * 1024;
        const double GB = MB * 1024;

        if (bytes >= GB) return $"{bytes / GB:F2} GB";
        if (bytes >= MB) return $"{bytes / MB:F2} MB";
        if (bytes >= KB) return $"{bytes / KB:F1} KB";
        return $"{bytes} B";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
