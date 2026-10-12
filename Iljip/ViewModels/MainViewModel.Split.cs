using System.IO;

namespace Iljip.ViewModels;

// 나눠 만들기(분할 압축) 화면 쪽 — W2-03(I2-2 만들기). 나누기 크기는 압축 옵션 창(CompressionOptionsRules)이 정하고 서비스가 조각을 만든다.
// 화면은 끝난 뒤 실제 조각 이름으로 알리고(같은 이름 세트가 있으면 '이름 (2)' 로 바뀜) 그 세트를 연다(MainViewModel.CompressAsync).
public partial class MainViewModel
{
    /// <summary>
    /// 나눠 만들기를 마쳤을 때의 안내(설계 W2-03 §5): '3개 조각으로 나눠 만들었어요(이름.z01 … 이름.zip). 받는 사람은 모든 조각을 같은 폴더에 두고
    /// 이름.zip을 열면 돼요.' + Windows 탐색기로는 안 열린다는 한 줄. 조각이 둘이면 '…' 대신 쉼표.
    /// </summary>
    internal static string SplitDoneMessage(IReadOnlyList<string> volumes, string? openPath)
    {
        string first = Path.GetFileName(volumes[0]);
        string last = Path.GetFileName(volumes[^1]);
        string open = Path.GetFileName(openPath ?? volumes[^1]);
        string range = volumes.Count == 2 ? $"{first}, {last}" : $"{first} … {last}";
        return $"{volumes.Count}개 조각으로 나눠 만들었어요({range}). 받는 사람은 모든 조각을 같은 폴더에 두고 {open}을 열면 돼요.\n" +
               "(Windows 탐색기로는 열리지 않아요 — 반디집·알집·7-Zip 같은 압축 프로그램으로 열어 주세요.)";
    }
}
