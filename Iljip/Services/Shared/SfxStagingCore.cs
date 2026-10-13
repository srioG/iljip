#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Iljip.Services;

// ★ 공유 소스(W2-10 검증 지적 1차): 일집 본체(net8.0-windows)와 자동 풀림 스텁(Iljip.SfxStub — .NET Framework 4.6.2)이 같은 파일을 컴파일하고, 시험도 이 이름·규칙을 쓴다.
//   두 쪽 모두에 있는 API 만 쓸 것(WinZipAesCore.cs 머리 주석 참고 — Path.GetRelativePath·string.Contains(char)·ToHashSet 등은 4.6.2 에 없음).

/// <summary>
/// 풀 곳 안 숨은 임시 폴더(<c>.일집-임시-8글자</c>)의 약속 — 본체(<c>StagingFolder</c>)와 스텁(<c>SfxExtractor</c>)이 같이 지킨다.
/// <list type="bullet">
/// <item>주인 표시: 스텁은 임시 폴더를 만들자마자 그 안의 <see cref="MarkerFileName"/> 을 아무도 못 열게 열어 두고 끝날 때 닫는다.
///   작업 관리자 종료·전원 꺼짐·로그오프로 죽으면 Windows 가 닫아 주므로 '열 수 있음' = 주인 없음 → 다음에 같은 곳에 풀 때 바로 정리한다(예전엔 하루를 기다림).
///   표시가 없는 임시 폴더(본체가 만든 것 — 본체는 표시를 두지 않음, 또는 표시를 못 만든 것)는 예전처럼 하루 지난 것만 정리한다.</item>
/// <item>사용자 원래 파일: 스텁이 덮어쓸 파일을 비켜 두는 <see cref="BackupFolderName"/>(파일 이름 0·1·2…, 원래 자리는 <see cref="BackupListName"/> 에 'N(탭)상대 경로' 한 줄씩)는
///   정리할 때 지우지 않는다 — 풀 곳의 보이는 폴더 '<see cref="RescueFolderPrefix"/> (날짜 시각)' 에 원래 자리 모양 그대로 옮긴다.
///   하나라도 못 옮기면 그 임시 폴더는 지우지 않는다(다음 정리 때 다시).</item>
/// <item>지울 때는 주인 표시를 쥔 채 나머지를 먼저 지우고 표시는 마지막에 닫는다(<see cref="DeleteOwnedTree"/> — W2-10 검증 지적 2차).</item>
/// <item>같은 풀 곳의 마무리(제자리로 옮기기·되돌리기)는 한 번에 하나씩: <see cref="DestinationMutexName"/>(같은 로그인 세션의 이름 있는 뮤텍스).</item>
/// </list>
/// </summary>
internal static class SfxStaging
{
    /// <summary>임시 폴더 이름 머리(숨김) — 본체 <c>StagingFolder.Prefix</c> 와 같다.</summary>
    public const string Prefix = ".일집-임시-";

    /// <summary>주인 표시 파일(스텁이 도는 동안 독점으로 열어 둠).</summary>
    public const string MarkerFileName = ".사용 중";

    /// <summary>덮어쓸 원래 파일을 비켜 두는 곳(임시 폴더 안).</summary>
    public const string BackupFolderName = ".덮어쓴 파일";

    /// <summary>비켜 둔 파일의 원래 자리 목록(UTF-8, 'N(탭)풀 곳 기준 상대 경로').</summary>
    public const string BackupListName = "목록.txt";

    /// <summary>정리하다 찾은 원래 파일을 옮겨 두는 보이는 폴더 이름 머리(뒤에 ' (yyyy-MM-dd HH-mm-ss)').</summary>
    public const string RescueFolderPrefix = "덮어쓰기 전 원래 파일";

    /// <summary>주인 표시가 없는 임시 폴더를 정리하기까지 기다리는 시간(예전 규칙 — 만들어지는 중일 수 있음).</summary>
    public static readonly TimeSpan UnmarkedStaleAge = TimeSpan.FromDays(1);

    /// <summary>임시 폴더의 주인.</summary>
    public enum Owner
    {
        /// <summary>주인 표시가 없음(본체가 만든 것·만들어지는 중) — 하루 규칙.</summary>
        Unmarked,
        /// <summary>주인이 살아 있음(표시 파일을 누가 열어 둠) — 건드리지 않음.</summary>
        Alive,
        /// <summary>주인이 없음(표시 파일을 독점으로 열 수 있음) — 정리해도 됨.</summary>
        Gone,
    }

    /// <summary>
    /// 임시 폴더의 주인을 본다. <see cref="Owner.Gone"/> 이면 표시 파일을 독점으로 연 채 <paramref name="claim"/> 으로 돌려준다
    /// (정리하는 동안 다른 정리와 겹치지 않게 — 다 하면 부르는 쪽이 닫는다). 모르면(권한 등) 살아 있는 것으로 본다.
    /// </summary>
    public static Owner Probe(string stagingDir, out FileStream? claim)
    {
        claim = null;
        string marker = Path.Combine(stagingDir, MarkerFileName);
        if (!File.Exists(marker)) return Owner.Unmarked;
        try
        {
            claim = new FileStream(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return Owner.Gone;
        }
        catch (FileNotFoundException)
        {
            return Owner.Unmarked;
        }
        catch (DirectoryNotFoundException)
        {
            return Owner.Unmarked;
        }
        catch (Exception)
        {
            return Owner.Alive;   // 공유 위반(누가 열어 둠)·권한 — 모르면 건드리지 않음
        }
    }

    /// <summary>
    /// <paramref name="destination"/> 안의 남은 임시 폴더를 정리한다: 주인이 없으면(<see cref="Owner.Gone"/>) 바로, 표시가 없으면 하루 지난 것만,
    /// 살아 있으면 건드리지 않음. 지우기 전에 비켜 둔 원래 파일을 보이는 폴더로 옮긴다(못 옮기면 그 임시 폴더는 남김). 실패는 삼킨다(정리는 덤).
    /// </summary>
    /// <returns>옮긴 원래 파일 수·못 옮긴 수·옮긴 폴더 이름.</returns>
    public static RescueSession CleanupStale(string destination, DateTime now, int deleteAttempts = 1)
    {
        var rescue = new RescueSession(destination, now);
        List<string> dirs;
        try
        {
            dirs = new List<string>(Directory.EnumerateDirectories(destination, Prefix + "*"));
        }
        catch (Exception)
        {
            return rescue;
        }
        foreach (string d in dirs)
        {
            try
            {
                FileStream? claim = null;
                try
                {
                    var owner = Probe(d, out claim);
                    if (owner == Owner.Alive) continue;
                    if (owner == Owner.Unmarked && DateTime.UtcNow - Directory.GetCreationTimeUtc(d) < UnmarkedStaleAge) continue;
                    // 지우는 동안에도 표시를 쥐고 있다가 마지막에 닫는다(DeleteOwnedTree — 다른 정리와 겹치지 않게)
                    if (rescue.Rescue(d))
                    {
                        var owned = claim;
                        claim = null;
                        DeleteOwnedTree(d, owned, deleteAttempts);
                    }
                }
                finally
                {
                    claim?.Dispose();
                }
            }
            catch (Exception)
            {
                // 지우지 못한 것은 그대로(다음에 다시)
            }
        }
        rescue.Finish();
        return rescue;
    }

    /// <summary>폴더째 지운다(읽기 전용 표시를 풀고, 잠기면 200ms 간격으로 다시). 다 지웠으면(또는 원래 없으면) true.</summary>
    public static bool DeleteTree(string path, int attempts)
    {
        for (int attempt = 0; attempt < Math.Max(1, attempts); attempt++)
        {
            try
            {
                if (!Directory.Exists(path)) return true;
                ClearReadOnly(path);
                Directory.Delete(path, recursive: true);
                return true;
            }
            catch (Exception) when (attempt < attempts - 1)
            {
                Thread.Sleep(200);
            }
            catch (Exception)
            {
                return false;
            }
        }
        return false;
    }

    /// <summary>
    /// 임시 폴더를 지운다 — 주인 표시(<paramref name="marker"/> — 이 프로세스가 열어 둔 것)는 <b>마지막에</b>: 표시를 쥔 채 나머지(풀다 남은 것·덮어쓴 원래 파일)를
    /// 모두 지운 뒤 표시를 닫고 표시와 폴더를 지운다. 표시를 먼저 닫으면 지우는 동안 같은 풀 곳의 다른 풀기가 '주인 없음'으로 보고
    /// 방금 일부러 덮어쓴 원래 파일을 '지난번에 끝까지 못 푼 흔적'으로 보이는 폴더에 옮기며 잘못 알렸다(W2-10 검증 지적 2차).
    /// 표시가 없으면(null) <see cref="DeleteTree"/> 와 같다. 다 지웠으면 true(못 지운 것은 남음 — 다음 정리 때).
    /// </summary>
    public static bool DeleteOwnedTree(string path, FileStream? marker, int attempts)
    {
        if (marker is null) return DeleteTree(path, attempts);
        bool rest = true;
        try
        {
            foreach (string entry in Directory.GetFileSystemEntries(path))
            {
                if (string.Equals(Path.GetFileName(entry), MarkerFileName, StringComparison.OrdinalIgnoreCase)) continue;
                rest &= Directory.Exists(entry) ? DeleteTree(entry, attempts) : DeleteFile(entry, attempts);
            }
        }
        catch (Exception)
        {
            rest = false;   // 목록을 못 읽음 — 아래에서 폴더째 다시 해 봄
        }
        finally
        {
            marker.Dispose();
        }
        return DeleteTree(path, attempts) && rest;
    }

    /// <summary>파일 하나를 지운다(읽기 전용 표시를 풀고, 잠기면 200ms 간격으로 다시). 지웠으면(또는 원래 없으면) true.</summary>
    private static bool DeleteFile(string path, int attempts)
    {
        for (int attempt = 0; attempt < Math.Max(1, attempts); attempt++)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return true;
                if ((fi.Attributes & FileAttributes.ReadOnly) != 0) fi.Attributes &= ~FileAttributes.ReadOnly;
                fi.Delete();
                return true;
            }
            catch (Exception) when (attempt < attempts - 1)
            {
                Thread.Sleep(200);
            }
            catch (Exception)
            {
                return false;
            }
        }
        return false;
    }

    /// <summary>
    /// 읽기 전용 표시가 있는 파일만 푼다(<see cref="Directory.Delete(string, bool)"/> 는 읽기 전용 파일에서 멈춤). 표시는 폴더를 훑을 때 같이 오므로
    /// 파일마다 따로 묻지 않는다(예전엔 모든 파일의 표시를 다시 써서 덮어쓴 원래 파일이 많으면 느렸음).
    /// </summary>
    private static void ClearReadOnly(string path)
    {
        foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            try
            {
                if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes &= ~FileAttributes.ReadOnly;
            }
            catch (Exception)
            {
                // 아래 지우기에서 다시(실패하면 몇 번 더)
            }
        }
    }

    /// <summary>
    /// 같은 풀 곳의 마무리를 한 번에 하나씩 하게 하는 뮤텍스 이름(같은 로그인 세션 — 'Local\'). 풀 곳 전체 경로(대소문자 무시)의 SHA-256 앞 16바이트.
    /// </summary>
    public static string DestinationMutexName(string fullDestination)
    {
        string key = (fullDestination ?? string.Empty).TrimEnd('\\', '/').ToUpperInvariant();
        byte[] hash;
        using (var sha = SHA256.Create()) hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        var sb = new StringBuilder("Local\\Iljip.SfxDest.", 60);
        for (int i = 0; i < 16; i++) sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>
    /// 비켜 둔 원래 파일의 원래 자리 목록(<see cref="BackupListName"/> — 'N(탭)풀 곳 기준 상대 경로' 한 줄씩, UTF-8)을 쓰는 것.
    /// 파일을 처음 쓸 때 한 번 열고 <see cref="Dispose"/> 때 닫는다 — 예전엔 줄마다 열고 닫아(File.AppendAllText) 같은 이름 수천 개를 덮어쓰면
    /// 회사 PC 에서 파일 하나에 수 ms 씩 더 걸렸다(W2-10 검증 지적 2차). 줄마다 OS 로 넘기므로(Flush) 그 뒤에 프로세스가 강제로 죽어도 줄은 남는다.
    /// 쓰는 동안은 다른 쪽이 읽지 못하므로 목록을 읽기(<see cref="RescueSession.Rescue"/>)·폴더 지우기 전에 닫을 것.
    /// 못 쓰면 그냥 둔다(그 원래 파일은 숫자 이름으로 옮겨짐) — 다음 줄에서 다시 열어 본다.
    /// </summary>
    internal sealed class BackupList : IDisposable
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private readonly string _path;
        private FileStream? _stream;

        public BackupList(string backupFolder) => _path = Path.Combine(backupFolder, BackupListName);

        /// <summary>한 줄 더한다(원래 파일을 비키기 <b>전에</b> — 그 사이에 죽어도 이름을 안다).</summary>
        public void Add(int number, string relativePath)
        {
            try
            {
                _stream ??= new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
                byte[] line = Utf8.GetBytes(number.ToString(CultureInfo.InvariantCulture) + "\t" + relativePath + "\r\n");
                _stream.Write(line, 0, line.Length);
                _stream.Flush();
            }
            catch (Exception)
            {
                // 이름은 못 남겨도 파일은 남는다(숫자 이름으로 옮겨짐) — 다음 줄은 다시 열어 본다
                Dispose();
            }
        }

        public void Dispose()
        {
            try { _stream?.Dispose(); } catch (Exception) { /* 닫기 실패는 무시 */ }
            _stream = null;
        }
    }

    /// <summary>
    /// 임시 폴더에 남은 원래 파일을 풀 곳의 보이는 폴더 '<see cref="RescueFolderPrefix"/> (날짜 시각)' 로 옮기는 한 번의 작업(폴더는 처음 옮길 때 만들고, 여러 임시 폴더가 같이 씀).
    /// </summary>
    internal sealed class RescueSession
    {
        private readonly string _destination;
        private readonly DateTime _now;
        private string? _folder;

        public RescueSession(string destination, DateTime now)
        {
            _destination = destination;
            _now = now;
        }

        /// <summary>옮긴 원래 파일 수.</summary>
        public int Moved { get; private set; }

        /// <summary>못 옮긴(임시 폴더에 남은) 원래 파일 수.</summary>
        public int Left { get; private set; }

        /// <summary>옮겨 둔 폴더 이름(풀 곳 안 — 하나도 안 옮겼으면 null).</summary>
        public string? FolderName => Moved > 0 && _folder is not null ? Path.GetFileName(_folder) : null;

        /// <summary>
        /// 임시 폴더 하나의 <see cref="BackupFolderName"/> 에 남은 파일을 옮긴다(목록의 원래 자리 모양 — 목록에 없거나 풀 곳 밖을 가리키면 숫자 이름).
        /// 다 옮겼거나 옮길 것이 없으면 true(임시 폴더를 지워도 됨).
        /// </summary>
        public bool Rescue(string stagingDir)
        {
            string backups = Path.Combine(stagingDir, BackupFolderName);
            if (!Directory.Exists(backups)) return true;
            var names = ReadList(Path.Combine(backups, BackupListName));
            var files = new List<string>();
            foreach (var f in Directory.GetFiles(backups))
                if (!string.Equals(Path.GetFileName(f), BackupListName, StringComparison.OrdinalIgnoreCase)) files.Add(f);
            files.Sort((a, b) => NumberOf(a).CompareTo(NumberOf(b)));

            bool all = true;
            foreach (string f in files)
            {
                string number = Path.GetFileName(f);
                try
                {
                    string to = TargetFor(names.TryGetValue(number, out var rel) ? rel : number, number);
                    string? parent = Path.GetDirectoryName(to);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    MoveWithRetry(f, to);
                    Moved++;
                }
                catch (FileNotFoundException)
                {
                    // 다른 정리가 먼저 옮김
                }
                catch (Exception)
                {
                    Left++;
                    all = false;
                }
            }
            return all;
        }

        /// <summary>만들었는데 하나도 못 옮긴 빈 폴더는 지운다.</summary>
        public void Finish()
        {
            if (_folder is null || Moved > 0) return;
            try
            {
                if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: false);
            }
            catch (Exception)
            {
                // 빈 폴더가 남을 뿐
            }
        }

        private string TargetFor(string relative, string number)
        {
            string folder = EnsureFolder();
            string root = folder.TrimEnd('\\') + "\\";
            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(folder, relative.Replace('/', '\\')));
                if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) || relative.IndexOf(':') >= 0)
                    candidate = Path.Combine(folder, number);
            }
            catch (Exception)
            {
                candidate = Path.Combine(folder, number);
            }
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            string dir = Path.GetDirectoryName(candidate) ?? folder;
            string stem = Path.GetFileNameWithoutExtension(candidate), ext = Path.GetExtension(candidate);
            for (int n = 2; n < 10000; n++)
            {
                string p = Path.Combine(dir, $"{stem} ({n}){ext}");
                if (!File.Exists(p) && !Directory.Exists(p)) return p;
            }
            throw new IOException("같은 이름이 너무 많아요: " + candidate);
        }

        private string EnsureFolder()
        {
            if (_folder is not null) return _folder;
            string baseName = $"{RescueFolderPrefix} ({_now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture)})";
            string path = Path.Combine(_destination, baseName);
            for (int n = 2; Directory.Exists(path) || File.Exists(path); n++)
                path = Path.Combine(_destination, $"{baseName} ({n})");
            Directory.CreateDirectory(path);
            _folder = path;
            return path;
        }

        private static Dictionary<string, string> ReadList(string listPath)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(listPath)) return map;
                foreach (string line in File.ReadAllLines(listPath, new UTF8Encoding(false)))
                {
                    int tab = line.IndexOf('\t');
                    if (tab <= 0 || tab == line.Length - 1) continue;
                    map[line.Substring(0, tab)] = line.Substring(tab + 1);
                }
            }
            catch (Exception)
            {
                // 목록을 못 읽으면 숫자 이름으로
            }
            return map;
        }

        private static long NumberOf(string path) =>
            long.TryParse(Path.GetFileName(path), NumberStyles.None, CultureInfo.InvariantCulture, out long n) ? n : long.MaxValue;

        private static void MoveWithRetry(string from, string to)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(from, to);
                    return;
                }
                catch (Exception ex) when (attempt < 3 && !(ex is FileNotFoundException) && (ex is IOException || ex is UnauthorizedAccessException))
                {
                    Thread.Sleep(200);
                }
            }
        }
    }
}
