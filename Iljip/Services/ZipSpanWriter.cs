using System.Globalization;
using System.IO;

namespace Iljip.Services;

/// <summary>
/// PKZIP 나눔(.z01 … .zip) 쓰기(W2-03, I2-2 만들기). 다 만든 보통 ZIP 한 개(<see cref="ZipArchiveService"/> 가 임시 폴더에 만든 것 —
/// 암호·AES·ZipCrypto·한글 이름 규칙 그대로)를 다시 압축하지 않고 조각으로 바꾼다.
/// <list type="number">
/// <item>첫 조각 맨 앞에 나눔 표시 <c>50 4B 07 08</c> 를 붙인다(반디집 실물과 같음 — 그래서 모든 위치가 4 밀린다).</item>
/// <item>본문(로컬 머리 + 데이터)을 조각 크기로 자른다. 자를 자리가 로컬 머리(30 + 이름 + 추가 필드) 안이면 그 머리 시작으로 당긴다 —
///   머리는 늘 한 조각 안에 있다(옛 프로그램 호환). 데이터·데이터 서술자는 조각을 넘어가도 된다.</item>
/// <item>중앙 디렉터리 기록마다 '시작 디스크 번호'를 그 머리가 있는 조각 번호로, '로컬 머리 위치'를 그 조각 안 위치로 바꾼다(<see cref="ZipDirectory.CentralRecord.Rewrite"/>).</item>
/// <item>중앙 디렉터리 + 끝 머리는 마지막 데이터 뒤에 둔다: 마지막 조각에 다 들어가면 거기, 아니면 한 조각에 다 들어가는 한 새 조각 하나에
///   (반디집처럼 목록이 마지막 조각에만 있음 — SharpCompress 등도 바로 읽음), 그것도 안 되면(항목이 아주 많음) 기록 경계에서 다음 조각으로 넘긴다.
///   이때도 마지막 조각(.zip)에는 기록이 적어도 하나 있게 한다 — 끝 머리만 든 .zip 은 반디집이 '열 수 없음'(0xa0000061)으로 거절했다(2026-10-13 실측).</item>
/// <item>끝 머리 값: 이 디스크 = 마지막, 목록 시작 디스크·그 안 위치, 이 디스크의 항목 수, 전체 항목 수, 목록 크기. 항목이 65,535개 이상이거나
///   목록·조각이 4GB 를 넘을 수 있으면 ZIP64 끝 머리·위치 표도 같은 디스크 번호 규칙으로 쓴다.</item>
/// <item>이름은 이름.z01, 이름.z02 …, 마지막은 이름.zip. 99조각을 넘으면 <see cref="TooManyVolumesException"/>(조각을 만들기 전에 거절).</item>
/// </list>
/// 디스크: 원래 ZIP 을 뒤에서부터 잘라 내며(SetLength) 조각을 만들어, 원래 크기 + 조각 하나만큼만 더 쓴다. 끝나면 원래 ZIP 은 없다.
/// 실패·취소면 만들던 조각·잘린 원래 ZIP 이 임시 폴더에 남는다 — 부르는 쪽(<see cref="SplitArchiveOutput"/>)이 임시 폴더째 지운다.
/// 반디집 7.46(bz.exe t)·7z.dll 26.03 이 모두 읽는다(원형 span_conv.py·CD 넘김 실측 — HANDOFF W2-03).
/// </summary>
internal static class ZipSpanWriter
{
    /// <summary>조각 수 한도(.z01 … .z99 + .zip 은 100개지만, 반디집·WinZip 관례대로 99개까지).</summary>
    public const int MaxVolumes = 99;

    /// <summary>나눔 표시 길이(첫 조각 맨 앞).</summary>
    private const int SpanSigLength = 4;

    /// <summary>조각 이름: <paramref name="index"/>(0부터)가 마지막이면 '이름.zip', 아니면 '이름.z01' ….</summary>
    public static string VolumeName(string stem, int index, int count) =>
        index == count - 1 ? stem + ".zip" : stem + ".z" + (index + 1).ToString("00", CultureInfo.InvariantCulture);

    /// <summary>세트의 조각 이름 전부(차례대로).</summary>
    public static IReadOnlyList<string> VolumeNames(string stem, int count) =>
        Enumerable.Range(0, count).Select(i => VolumeName(stem, i, count)).ToList();

    /// <summary>나누는 계획(파일은 건드리지 않고 읽기만 해서 만든다).</summary>
    internal sealed class Plan
    {
        /// <summary>본문(나눔 표시 4바이트 + 원래 ZIP 의 중앙 디렉터리 앞까지)의 자를 자리. 처음은 0, 끝은 본문 길이.</summary>
        public required long[] Cuts { get; init; }

        /// <summary>조각마다 본문 뒤에 붙일 바이트(중앙 디렉터리 기록·끝 머리). 길이 = 조각 수.</summary>
        public required List<byte[]>[] Appendix { get; init; }

        /// <summary>원래 ZIP 의 중앙 디렉터리 위치(여기서부터 잘라 버림).</summary>
        public long CentralStart { get; init; }

        public int DataPieces => Cuts.Length - 1;
        public int PieceCount => Appendix.Length;
    }

    /// <summary>
    /// <paramref name="zipPath"/>(보통 ZIP)을 <paramref name="volumeSize"/> 바이트 이하 조각으로 바꾼다. 만든 조각 경로를 차례대로 돌려준다
    /// (<paramref name="outputStem"/>.z01 … <paramref name="outputStem"/>.zip). 원래 ZIP 은 지워진다.
    /// </summary>
    /// <param name="onBytes">옮겨 담은 바이트(진행률용).</param>
    public static IReadOnlyList<string> Split(string zipPath, string outputStem, long volumeSize, CancellationToken ct, Action<long>? onBytes = null)
    {
        var plan = MakePlan(zipPath, volumeSize);
        var names = VolumeNames(outputStem, plan.PieceCount);
        using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1 << 16))
        {
            fs.SetLength(plan.CentralStart);   // 중앙 디렉터리·끝 머리는 계획(메모리)에 고쳐 쓴 것이 있다
            for (int p = plan.PieceCount - 1; p >= 0; p--)
            {
                ct.ThrowIfCancellationRequested();
                using (var output = new FileStream(names[p], FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    if (p < plan.DataPieces)
                    {
                        if (p == 0) output.Write(BitConverter.GetBytes(ZipDirectory.SpanSig));
                        long from = Math.Max(0, plan.Cuts[p] - SpanSigLength), to = plan.Cuts[p + 1] - SpanSigLength;
                        VolumeSplitter.Copy(fs, from, to, output, ct, onBytes);
                    }
                    foreach (var part in plan.Appendix[p]) output.Write(part);
                }
                // 옮겨 담은 꼬리를 잘라 낸다 — 디스크를 원래 크기 + 조각 하나만큼만 쓰게
                if (p > 0 && p < plan.DataPieces) fs.SetLength(plan.Cuts[p] - SpanSigLength);
            }
        }
        File.Delete(zipPath);
        return names;
    }

    /// <summary>
    /// 나누는 계획. 원래 ZIP 이 나눌 수 없는 모양이면 <see cref="InvalidDataException"/>, 조각이 99개를 넘으면 <see cref="TooManyVolumesException"/>,
    /// 머리·목록 한 줄이 한 조각에 안 들어가면 <see cref="SplitSizeTooSmallException"/>.
    /// </summary>
    internal static Plan MakePlan(string zipPath, long volumeSize)
    {
        if (volumeSize < 1) throw new ArgumentOutOfRangeException(nameof(volumeSize));
        using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        var end = ZipDirectory.FindEnd(fs) ?? throw new InvalidDataException("ZIP 끝 머리를 찾지 못함");
        if (end.HasZip64Locator)
        {
            if (end.Zip64Disk != 0) throw new InvalidDataException("이미 나뉜 ZIP");
            ZipDirectory.ApplyZip64(fs, end, end.Zip64Offset);
        }
        if (end.ThisDisk != 0 || end.CdDisk != 0) throw new InvalidDataException("이미 나뉜 ZIP");
        long cdStart = end.CdOffset;
        if (cdStart < 0 || end.CdSize < 0 || end.CdSize > int.MaxValue || cdStart + end.CdSize > end.DirectoryLimit)
            throw new InvalidDataException("ZIP 중앙 디렉터리 위치가 맞지 않음");
        var records = ZipDirectory.ParseCentral(ZipDirectory.ReadAt(fs, cdStart, (int)end.CdSize), end.TotalEntries);

        // 로컬 머리 자리(본문 좌표 = 원래 위치 + 4). 첫 머리는 나눔 표시와 붙여 둔다(표시 바로 뒤에 첫 머리가 와야 함).
        var starts = new long[records.Count];
        var ends = new long[records.Count];
        var head = new byte[ZipDirectory.LocalHeaderLength];
        for (int i = 0; i < records.Count; i++)
        {
            var r = records[i];
            if (r.DiskStart != 0 || r.LocalOffset < 0 || r.LocalOffset + head.Length > cdStart)
                throw new InvalidDataException("ZIP 로컬 머리 위치가 맞지 않음");
            fs.Position = r.LocalOffset;
            fs.ReadExactly(head);
            if (ZipDirectory.U32(head, 0) != ZipDirectory.LocalHeaderSig) throw new InvalidDataException("ZIP 로컬 머리를 찾지 못함");
            long len = ZipDirectory.LocalHeaderLength + ZipDirectory.U16(head, 26) + ZipDirectory.U16(head, 28);
            long s = r.LocalOffset + SpanSigLength;
            starts[i] = s == SpanSigLength ? 0 : s;
            ends[i] = s + len;
        }
        Array.Sort(starts, ends);

        // 본문 자르기 — 머리 안이면 그 머리 시작으로 당긴다
        long bodyLen = SpanSigLength + cdStart;
        var cuts = new List<long> { 0 };
        long pos = 0;
        while (bodyLen - pos > volumeSize)
        {
            long c = pos + volumeSize;
            int k = LastBelow(starts, c);
            if (k >= 0 && c < ends[k]) c = starts[k];
            if (c <= pos) throw new SplitSizeTooSmallException();
            cuts.Add(c);
            pos = c;
            if (cuts.Count > MaxVolumes) throw new TooManyVolumesException(MaxVolumes);   // 데이터 조각만으로도 넘음
        }
        cuts.Add(bodyLen);
        int dataPieces = cuts.Count - 1;

        // 중앙 디렉터리 기록: 머리가 있는 조각 번호와 그 안 위치로
        var cutArray = cuts.ToArray();
        var recBytes = new byte[records.Count][];
        long cdTotal = 0;
        for (int i = 0; i < records.Count; i++)
        {
            long b = records[i].LocalOffset + SpanSigLength;
            int d = LastAtOrBelow(cutArray, b);
            recBytes[i] = records[i].Rewrite(d, b - cutArray[d]);
            cdTotal += recBytes[i].Length;
        }

        bool zip64 = records.Count >= ushort.MaxValue || cdTotal >= uint.MaxValue || volumeSize >= uint.MaxValue;
        int tailLen = ZipDirectory.EndBytesLength(zip64, end.Comment.Length);

        var appendix = new List<List<byte[]>>();
        for (int i = 0; i < dataPieces; i++) appendix.Add(new List<byte[]>());
        int cur = dataPieces - 1;
        long used = cutArray[^1] - cutArray[^2];
        long cdDisk, cdOffset;
        int onLast;
        if (used + cdTotal + tailLen <= volumeSize || cdTotal + tailLen <= volumeSize)
        {
            // 목록 전체를 한 조각에: 마지막 데이터 조각에 들어가면 거기, 아니면 새 조각
            if (used + cdTotal + tailLen > volumeSize)
            {
                appendix.Add(new List<byte[]>());
                cur++;
                used = 0;
            }
            cdDisk = cur;
            cdOffset = used;
            appendix[cur].AddRange(recBytes);
            used += cdTotal;
            onLast = records.Count;
        }
        else
        {
            // 목록이 한 조각보다 크다 — 기록 경계에서 넘긴다. 마지막 기록은 끝 머리와 같은 조각에.
            cdDisk = -1;
            cdOffset = 0;
            onLast = 0;
            for (int i = 0; i < recBytes.Length; i++)
            {
                long need = recBytes[i].Length + (i == recBytes.Length - 1 ? tailLen : 0);
                if (need > volumeSize) throw new SplitSizeTooSmallException();
                if (used + need > volumeSize)
                {
                    appendix.Add(new List<byte[]>());
                    cur++;
                    used = 0;
                    onLast = 0;
                    if (appendix.Count > MaxVolumes) throw new TooManyVolumesException(MaxVolumes);
                }
                if (cdDisk < 0)
                {
                    cdDisk = cur;
                    cdOffset = used;
                }
                appendix[cur].Add(recBytes[i]);
                used += recBytes[i].Length;
                onLast++;
            }
        }
        if (appendix.Count > MaxVolumes) throw new TooManyVolumesException(MaxVolumes);

        long last = appendix.Count - 1;
        appendix[cur].Add(ZipDirectory.BuildEnd(zip64, last, cdDisk, onLast, records.Count, cdTotal, cdOffset, end.Comment,
            zip64EndOffset: used, totalDisks: appendix.Count));

        return new Plan
        {
            Cuts = cutArray,
            Appendix = appendix.ToArray(),
            CentralStart = cdStart,
        };
    }

    /// <summary>정렬된 <paramref name="sorted"/> 에서 값이 <paramref name="value"/> 보다 작은 마지막 자리(없으면 -1).</summary>
    private static int LastBelow(long[] sorted, long value)
    {
        int i = Array.BinarySearch(sorted, value);
        if (i < 0) i = ~i;
        else while (i > 0 && sorted[i - 1] == value) i--;
        return i - 1;
    }

    /// <summary>정렬된 <paramref name="sorted"/> 에서 값이 <paramref name="value"/> 이하인 마지막 자리.</summary>
    private static int LastAtOrBelow(long[] sorted, long value)
    {
        int i = Array.BinarySearch(sorted, value);
        if (i >= 0)
        {
            while (i + 1 < sorted.Length && sorted[i + 1] == value) i++;
            return Math.Min(i, sorted.Length - 2);   // 끝(본문 길이)과 같은 값은 마지막 조각
        }
        return ~i - 1;
    }
}
