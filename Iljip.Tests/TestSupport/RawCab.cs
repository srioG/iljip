using System.Text;

namespace Iljip.Tests.TestSupport;

/// <summary>
/// CAB 최소 작성기(압축 없음 — typeCompress 0, 데이터 블록 확인값 0 = 검사 안 함). 반디집·파이썬 픽스처에 CAB 가 없어
/// Microsoft 'Cabinet File Format' 문서대로 머리를 손으로 짠다(W2-01 — 7z.dll CAB 풀기·잘림 검사 시험용).
/// 이름은 UTF-8(_A_NAME_IS_UTF 0x80). 폴더 하나에 파일을 차례로 넣고, 풀린 바이트를 32KB 블록으로 나눈다.
/// </summary>
public static class RawCab
{
    public static byte[] Build(IEnumerable<(string Name, byte[] Data)> files)
    {
        var list = files.ToList();
        const int headerSize = 36, folderSize = 8;
        var fileEntries = new MemoryStream();
        var w = new BinaryWriter(fileEntries);
        uint offset = 0;
        foreach (var (name, data) in list)
        {
            w.Write((uint)data.Length);       // cbFile
            w.Write(offset);                  // uoffFolderStart
            w.Write((ushort)0);               // iFolder
            w.Write((ushort)(((2026 - 1980) << 9) | (1 << 5) | 2));   // 날짜 2026-01-02
            w.Write((ushort)((3 << 11) | (4 << 5) | 3));              // 시각 03:04:06
            w.Write((ushort)(0x20 | 0x80));   // 보관 + 이름 UTF-8
            w.Write(Encoding.UTF8.GetBytes(name.Replace('/', '\\')));
            w.Write((byte)0);
            offset += (uint)data.Length;
        }
        w.Flush();

        byte[] all = list.SelectMany(f => f.Data).ToArray();
        var blocks = new MemoryStream();
        var bw = new BinaryWriter(blocks);
        int count = 0;
        for (int i = 0; i < all.Length || count == 0; i += 32768)
        {
            int n = Math.Min(32768, all.Length - i);
            bw.Write(0u);                     // csum(0 = 검사 안 함)
            bw.Write((ushort)n);              // cbData
            bw.Write((ushort)n);              // cbUncomp
            bw.Write(all, i, n);
            count++;
            if (all.Length == 0) break;
        }
        bw.Flush();

        uint coffFiles = headerSize + folderSize;
        uint coffData = coffFiles + (uint)fileEntries.Length;
        uint total = coffData + (uint)blocks.Length;
        var ms = new MemoryStream();
        var hw = new BinaryWriter(ms);
        hw.Write(Encoding.ASCII.GetBytes("MSCF"));
        hw.Write(0u);                         // reserved1
        hw.Write(total);                      // cbCabinet
        hw.Write(0u);                         // reserved2
        hw.Write(coffFiles);                  // coffFiles
        hw.Write(0u);                         // reserved3
        hw.Write((byte)3); hw.Write((byte)1); // 판 1.3
        hw.Write((ushort)1);                  // cFolders
        hw.Write((ushort)list.Count);         // cFiles
        hw.Write((ushort)0);                  // flags
        hw.Write((ushort)0x1234);             // setID
        hw.Write((ushort)0);                  // iCabinet
        hw.Write(coffData);                   // CFFOLDER.coffCabStart
        hw.Write((ushort)count);              // cCFData
        hw.Write((ushort)0);                  // typeCompress = 없음
        hw.Write(fileEntries.ToArray());
        hw.Write(blocks.ToArray());
        hw.Flush();
        return ms.ToArray();
    }
}
