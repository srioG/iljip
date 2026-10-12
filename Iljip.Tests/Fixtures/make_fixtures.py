#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
일집 회귀(계약) 테스트용 '기준 압축 파일'(픽스처) 재생성 스크립트.

  python -I make_fixtures.py [--bz "C:\\Program Files\\Bandizip\\bz.exe"] [--out <폴더>] [--keep-temp]

★ 커밋한 뒤에는 다시 만들지 말 것. 같은 반디집으로 다시 만들어도 압축 파일 10개는 바이트가 달라진다
  - 암호 파일 8개(ZipCrypto·AES·헤더 암호 7z·py7zr 7z·zipx_aes): 매번 새 난수 소금(salt)·IV — 피할 수 없음
  - tgz·tar.gz: bz.exe 가 gzip 머리에 '지금 시각'을 넣음
  — 의미 변화 없이 이력에 바이너리 변경만 쌓인다. 나머지(원본·ZIP·7z·tar·iso·lzh·xz·분할 등)는 원본의 수정·접근·만든 시각을
  모두 고정하고 시스템 임시 폴더에서 만들기 때문에 매번 같다(2026-10-12 두 번 만들어 비교: 같음 41 / 다름 10).
  또 ZipCrypto 픽스처가 바뀌면 '1바이트 암호 검사를 우연히 통과하는 틀린 암호'(B5 근거, PasswordTests·BugEvidenceTests)를 다시 찾아야 한다.
  특히 ZIP 의 DOS 시각은 '이 PC 현지 시각'이라 한국 시간(UTC+9) PC 에서 만들어야 기존 기대값(03:04:06)과 같다.

안전장치: 기본 실행은 시스템 임시 폴더에 전부 새로 만든 뒤, 모든 픽스처가 만들어지고 검증이 하나도 틀리지 않았고
기존 매니페스트의 픽스처가 하나도 빠지지 않았을 때만 Fixtures/_staging/ 으로 복사해 기존 src/·bandizip/·python/·local/·manifest.json 과
바꿔 넣는다(실패하면 기존 파일은 그대로). --out <폴더> 를 주면 그 폴더에만 만들고 바꿔 넣지 않는다(재현성 비교·검토용).
bz.exe 가 없으면 아무것도 지우거나 만들기 전에 종료 코드 2 로 멈춘다(반디집을 지운 뒤 실수로 돌려도 안전).

만드는 것 (모두 출력 루트 아래 — 기본은 이 파일이 있는 Fixtures 폴더):
  src/        합성 원본 데이터(결정적 내용·고정 수정시각). 사용자 업무 파일은 쓰지 않는다.
  bandizip/   반디집 콘솔 bz.exe 로 만든 압축 파일 (커밋 대상)
  python/     반디집이 못 만드는 변형을 Python 으로 만든 것 — '반디집 아님' (커밋 대상)
  local/      재배포하면 안 되는 산출물(반디집 SFX 스텁이 든 exe) — git 제외
  manifest.json  픽스처마다 형식·암호·분할 볼륨·만든 명령·기대 항목(경로/크기/sha256/폴더 여부)

각 픽스처는 만든 직후 bz.exe 로 목록(l)과 실제 해제(x)를 해 보고, 원본(src)에서 계산한
기대 항목과 경로·크기·sha256 까지 일치하는지 검증한 뒤 매니페스트에 기록한다.
검증이 하나라도 틀리면 종료 코드 1.

bz.exe(반디집 7.x)가 없으면 bandizip/ 픽스처는 재생성할 수 없다 — 커밋된 파일을 그대로 쓸 것.

bz.exe 7.46 에서 확인한 사실(이 스크립트가 기대하는 동작):
  * 명령행 스위치는 압축 파일 이름보다 앞에 와야 한다.
  * 원본을 절대경로로 주면 마지막 폴더 이름이 압축 안의 최상위 폴더로 들어간다(-storeroot:no 로 끔).
  * -fmt:zip 은 기본 파일명 코드페이지가 시스템 ANSI(한국어 Windows=CP949) + 0x7075(Info-ZIP
    Unicode Path) 추가 필드. -cp:65001 이면 원시 이름이 UTF-8 이지만 EFS 플래그(bit 11)는 켜지 않는다.
  * -fmt:zip -p: 는 ZipCrypto 만 만든다. AES-256 은 -fmt:zipx 로만 나오며 -l:0 이면 내부 방식이
    저장(Store)이라 일반 WinZip-AES ZIP 과 같다.
  * -fmt:7z -p: 는 항상 헤더(파일명) 암호화. 파일명 노출 암호 7z 는 bz.exe 로 못 만든다(py7zr 로 대체).
  * -fmt:xz / -fmt:gz 는 파일 하나만 받는다(폴더 불가) → .tar.xz 는 tar 를 먼저 만든 뒤 xz 로 감싼다.
  * 한글 암호 바이트: ZIP 은 ZipCrypto·AES 모두 CP949(UTF-8 아님), 7z 는 UTF-16LE(규격). 매 실행마다 판정해 기록.
  * .tar.xz / .tbz2 는 반디집 콘솔이 안쪽 .tar 하나로 보여 주고 꺼낸다(.tgz/.tar.gz 는 tar 내용을 바로 보여 줌).
  * NFD 이름 ZIP 은 반디집이 목록·해제 모두 NFC 로 바꿔 보여 준다.
"""
from __future__ import annotations

import argparse
import bz2
import datetime as dt
import hashlib
import json
import math
import os
import re
import shutil
import stat
import struct
import subprocess
import sys
import tarfile
import tempfile
import traceback
import unicodedata
import zipfile
from pathlib import Path

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

# ---------------------------------------------------------------- 상수

FIX = Path(__file__).resolve().parent          # 최종 픽스처 폴더(이 스크립트가 있는 곳)
OWNED = ("src", "bandizip", "python", "local", "manifest.json")   # 이 스크립트가 만들고 바꿔 넣는 것

# 출력 루트와 그 아래 경로 — set_root() 가 바꾼다(기본 실행은 FIX/_staging, --out 이면 그 폴더).
# 매니페스트의 'file' 등은 모두 ROOT 기준 상대경로라, 바꿔 넣은 뒤 FIX 기준으로도 그대로 맞다.
ROOT = FIX
SRC = FIX / "src"
OUT_BZ = FIX / "bandizip"
OUT_PY = FIX / "python"
OUT_LOCAL = FIX / "local"
MANIFEST = FIX / "manifest.json"
DEFAULT_BZ = Path(r"C:\Program Files\Bandizip\bz.exe")


def set_root(root: Path) -> None:
    global ROOT, SRC, OUT_BZ, OUT_PY, OUT_LOCAL, MANIFEST
    ROOT = root
    SRC = root / "src"
    OUT_BZ = root / "bandizip"
    OUT_PY = root / "python"
    OUT_LOCAL = root / "local"
    MANIFEST = root / "manifest.json"


PW = "Iljip-Test-1234"
PW_KO = "테스트암호"

SAMPLE = "일집_샘플"      # 일반 트리(한글·공백·괄호·기호·빈 파일·빈 폴더·3단 폴더)
SPLIT = "분할_샘플"       # 분할 압축용(160 KiB 난수 + 작은 텍스트)
NFD_ROOT = "맥_샘플"      # 맥 NFD 파일명 — 커밋 src 에 두지 않고 임시 폴더에 NFD 이름으로 만든다

# 고정 수정시각(짝수 초 — DOS 시간 2초 해상도). 한국 시간으로 못박는다 = 절대 시각 2026-01-01T18:04:06Z.
# (ZIP 의 DOS 시각은 bz.exe 가 '이 PC 현지 시각'으로 쓰므로, 한국 시간 PC 에서 만들어야 03:04:06 이 된다)
FIXED_LOCAL = dt.datetime(2026, 1, 2, 3, 4, 6, tzinfo=dt.timezone(dt.timedelta(hours=9)))
FIXED_MTIME = FIXED_LOCAL.timestamp()

SPLIT_RANDOM_SIZE = 160 * 1024   # 분할 원본 난수 크기
VOLUME = "64k"                   # 분할 볼륨 크기 → 160 KiB 원본이 3조각(64k·64k·나머지). 커밋 크기를 줄이려고 작게.
BZ_TIMEOUT = 180

ZIP_METHODS = {0: "store", 8: "deflate", 9: "deflate64", 12: "bzip2", 14: "lzma",
               93: "zstd", 95: "xz", 98: "ppmd", 99: "winzip-aes"}

KNOWN_GAPS_NOTE = "iljip_known_gaps 는 D:\\Python\\_기능대조_알PDF_반디집.md 의 ID (2026-10-08 기준 일집의 알려진 빈틈)"


class FixtureError(Exception):
    pass


# ---------------------------------------------------------------- 합성 원본

def det_bytes(seed: str, n: int) -> bytes:
    """SHA-256 카운터 스트림 — 파이썬 버전과 무관하게 결정적인 의사난수(압축 거의 안 됨)."""
    buf = bytearray()
    i = 0
    while len(buf) < n:
        buf += hashlib.sha256(f"{seed}#{i}".encode("ascii")).digest()
        i += 1
    return bytes(buf[:n])


def sample_files() -> dict[str, bytes]:
    # 실제 업무와 무관한 중립 문구(저장소 공개) — 바이트 길이는 예전 문구와 같게 맞춰 파일 크기(2464B)를 유지
    est = "".join(f"{i:03d}\t테스트용 가상품목 견적 항목 {i}번\t{i * 12500:,}원\r\n" for i in range(1, 41))
    readme = "".join(f"line {i:02d}: Iljip regression fixture (English only, LF)\n" for i in range(1, 21))
    report = "Report (final) v2 — 괄호·공백 포함 파일명 테스트\r\n" * 10
    csv = "\ufeff번호,품목,금액\r\n" + "".join(f"{i},품목{i},{i * 1000}\r\n" for i in range(1, 31))
    sym = "기호 #&+~!'@ 포함 파일명\r\n"
    rep = "압축이 잘 되는 반복 줄입니다. ABCDEFGHIJ 0123456789\n" * 1200
    sub = "하위폴더 보고서 최종본\r\n" * 5
    deep = "깊은 폴더 3단계 아래 파일입니다.\r\n대괄호 [끝] 포함 이름.\r\n"
    return {
        "견적서_2026.txt": est.encode("utf-8"),
        "readme.txt": readme.encode("ascii"),
        "Report (final) v2.txt": report.encode("utf-8"),
        "빈파일.txt": b"",
        "기호 #&+~!'@.txt": sym.encode("utf-8"),
        "반복_압축잘됨.txt": rep.encode("utf-8"),
        "data/sample.csv": csv.encode("utf-8"),
        "하위폴더/보고서 최종.txt": sub.encode("utf-8"),
        "하위폴더/2단계/3단계/깊은 파일 [끝].txt": deep.encode("utf-8"),
    }


SAMPLE_EMPTY_DIRS = ["빈폴더"]


def split_files() -> dict[str, bytes]:
    return {
        "난수_160KiB.bin": det_bytes("iljip-split-random", SPLIT_RANDOM_SIZE),
        "설명.txt": "분할 압축 테스트용 원본입니다. 난수 파일은 압축이 거의 안 됩니다.\r\n".encode("utf-8"),
    }


def nfd_files() -> dict[str, bytes]:
    return {
        "맥에서_만든_문서.txt": "맥(NFD) 파일명 테스트 — 내용은 NFC UTF-8\n".encode("utf-8"),
        "결재 폴더/결재_완료(최종).txt": "자모 분리 파일명 + 공백 폴더 + 괄호\n".encode("utf-8"),
        "english.txt": b"ascii name inside NFD root\n",
    }


def nfc(s: str) -> str:
    return unicodedata.normalize("NFC", s)


def nfd(s: str) -> str:
    return unicodedata.normalize("NFD", s)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def set_fixed_times(path: str) -> None:
    """수정·접근 시각(os.utime)에 더해 Windows '만든 날짜'까지 FIXED_MTIME 으로 고정한다.
    bz.exe 의 ZIP NTFS 추가필드(0x000a)는 만든 날짜(ctime)도 기록하므로, 이걸 안 고치면 다시 만들 때마다 바이트가 달라진다."""
    os.utime(path, (FIXED_MTIME, FIXED_MTIME))
    if os.name != "nt":
        return
    import ctypes
    from ctypes import wintypes
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.CreateFileW.restype = wintypes.HANDLE
    k32.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID,
                                wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
    k32.SetFileTime.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p]
    k32.CloseHandle.argtypes = [wintypes.HANDLE]
    FILE_WRITE_ATTRIBUTES, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS = 0x100, 3, 0x02000000
    h = k32.CreateFileW(str(path), FILE_WRITE_ATTRIBUTES, 7, None, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, None)
    if h in (None, wintypes.HANDLE(-1).value):
        raise OSError(ctypes.get_last_error(), f"만든 날짜 고정 실패(열기): {path}")
    try:
        ft = ctypes.c_ulonglong(int(round(FIXED_MTIME)) * 10_000_000 + 116444736000000000)   # FILETIME(UTC, 1601 기준 100ns)
        if not k32.SetFileTime(h, ctypes.byref(ft), ctypes.byref(ft), ctypes.byref(ft)):
            raise OSError(ctypes.get_last_error(), f"만든 날짜 고정 실패: {path}")
    finally:
        k32.CloseHandle(h)


def write_tree(base: Path, files: dict[str, bytes], empty_dirs=(), form=nfc) -> None:
    for rel, data in files.items():
        p = base.joinpath(*[form(part) for part in rel.split("/")])
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)
    for d in empty_dirs:
        base.joinpath(*[form(part) for part in d.split("/")]).mkdir(parents=True, exist_ok=True)
    # 아래에서 위로(파일을 쓰면 폴더 시각이 바뀌므로 폴더는 그 안을 다 고친 다음에)
    for root, _dirs, fnames in os.walk(base, topdown=False):
        for f in fnames:
            set_fixed_times(os.path.join(root, f))
        set_fixed_times(root)


def scan_tree(base: Path, prefix: str | None = None, with_hash: bool = True) -> list[dict]:
    """폴더를 훑어 항목 목록(경로는 '/' 구분·NFC, 폴더는 끝 '/' 없음)."""
    out = []
    pre = (nfc(prefix) + "/") if prefix else ""
    if prefix:
        out.append({"path": nfc(prefix), "size": 0, "sha256": None, "is_dir": True})
    for root, dirs, files in os.walk(base):
        dirs.sort()
        rr = Path(root).relative_to(base).as_posix()
        rr = "" if rr == "." else rr + "/"
        for d in dirs:
            out.append({"path": pre + nfc(rr + d), "size": 0, "sha256": None, "is_dir": True,
                        "_raw": rr + d})
        for f in sorted(files):
            data = (Path(root) / f).read_bytes()
            out.append({"path": pre + nfc(rr + f), "size": len(data),
                        "sha256": sha256(data) if with_hash else None, "is_dir": False,
                        "_raw": rr + f})
    out.sort(key=lambda e: e["path"])
    return out


def public(entries: list[dict]) -> list[dict]:
    return [{k: v for k, v in e.items() if not k.startswith("_")} for e in entries]


# ---------------------------------------------------------------- bz.exe

def decode_console(b: bytes) -> str:
    try:
        return b.decode("utf-8")
    except UnicodeDecodeError:
        return b.decode("cp949", "replace")


class Bz:
    def __init__(self, exe: Path):
        self.exe = exe
        rc, out = self.run([], check=False)
        m = re.search(r"bz (\d+\.\d+)", out)
        self.version = m.group(1) if m else "?"
        self.banner = out.splitlines()[0].strip() if out.strip() else ""

    def run(self, args, check=True, timeout=BZ_TIMEOUT):
        cmd = [str(self.exe), *[str(a) for a in args]]
        try:
            p = subprocess.run(cmd, stdin=subprocess.DEVNULL, capture_output=True, timeout=timeout)
        except subprocess.TimeoutExpired as e:
            raise FixtureError(f"bz.exe 시간 초과({timeout}s): {args}") from e
        out = decode_console(p.stdout) + decode_console(p.stderr)
        if check and p.returncode != 0:
            raise FixtureError(f"bz.exe 실패 rc={p.returncode}: {' '.join(map(str, args))}\n{out[-2000:]}")
        return p.returncode, out


LIST_RE = re.compile(r"^(.{19}) ([A-Z_]{4}) +(-?\d+) +(-?\d+) (.+?)\s*$")  # tgz 는 CompSize 가 -1


def bz_list(bz: Bz, archive: Path, password: str | None = None, check=True):
    args = ["l", "-consolemode:utf8"] + ([f"-p:{password}"] if password else []) + [archive]
    rc, out = bz.run(args, check=check)
    entries = []
    for line in out.splitlines():
        m = LIST_RE.match(line)
        if not m:
            continue
        name = m.group(5)
        is_dir = m.group(2).startswith("D") or name.endswith(("\\", "/"))
        raw = name.replace("\\", "/").rstrip("/")
        entries.append({"path": nfc(raw), "_raw": raw, "size": 0 if is_dir else int(m.group(3)),
                        "is_dir": is_dir})
    return rc, out, entries


def bz_extract(bz: Bz, archive: Path, dest: Path, password: str | None = None):
    dest.mkdir(parents=True, exist_ok=True)
    args = ["x", "-y", "-aoa", f"-o:{dest}"] + ([f"-p:{password}"] if password else []) + [archive]
    return bz.run(args)


def compare(expected: list[dict], actual: list[dict], what: str, check_hash: bool) -> list[str]:
    errs = []
    exp = {e["path"]: e for e in expected}
    act = {}
    for e in actual:
        if e["path"] in act:
            errs.append(f"{what}: 중복 항목 {e['path']!r}")
        act[e["path"]] = e
    for p in sorted(exp.keys() - act.keys()):
        errs.append(f"{what}: 없음 {p!r}")
    for p in sorted(act.keys() - exp.keys()):
        errs.append(f"{what}: 예상 밖 항목 {p!r}")
    for p in sorted(exp.keys() & act.keys()):
        a, b = exp[p], act[p]
        if a["is_dir"] != b["is_dir"]:
            errs.append(f"{what}: 폴더 여부 다름 {p!r}")
            continue
        if a["is_dir"]:
            continue
        if a["size"] != b["size"]:
            errs.append(f"{what}: 크기 다름 {p!r} 기대 {a['size']} 실제 {b['size']}")
        if check_hash and a["sha256"] != b.get("sha256"):
            errs.append(f"{what}: sha256 다름 {p!r}")
    return errs


# ---------------------------------------------------------------- 형식별 세부 판독(기록용)

def parse_zip_cd(data: bytes):
    """중앙 디렉터리 원시 판독(파이썬 zipfile 의 이름 해석에 기대지 않음)."""
    eocd = data.rfind(b"PK\x05\x06")
    if eocd < 0:
        return None
    _sig, disk_no, cd_disk, n_this, _n_total, _cd_size, cd_off, _cl = struct.unpack(
        "<IHHHHIIH", data[eocd:eocd + 22])
    if disk_no != cd_disk:
        return None
    pos = cd_off
    ents = []
    for _ in range(n_this):
        if data[pos:pos + 4] != b"PK\x01\x02":
            return None
        (_s, made, need, flags, method, _t, _d, crc, csize, usize, nlen, xlen, clen,
         _ds, _ia, eattr, lho) = struct.unpack("<IHHHHHHIIIHHHHHII", data[pos:pos + 46])
        name = data[pos + 46:pos + 46 + nlen]
        extra = data[pos + 46 + nlen:pos + 46 + nlen + xlen]
        xs = {}
        k = 0
        while k + 4 <= len(extra):
            hid, sz = struct.unpack("<HH", extra[k:k + 4])
            xs[hid] = extra[k + 4:k + 4 + sz]
            k += 4 + sz
        ents.append({"flags": flags, "method": method, "name": name, "extra": xs, "lho": lho,
                     "csize": csize, "usize": usize, "made_by_os": made >> 8})
        pos += 46 + nlen + xlen + clen
    return ents


def guess_encoding(raw: bytes, efs: bool) -> str:
    if all(b < 0x80 for b in raw):
        return "ascii"
    if efs:
        return "utf-8(EFS 플래그)"
    try:
        raw.decode("utf-8")
        return "utf-8(플래그 없음)"
    except UnicodeDecodeError:
        pass
    try:
        raw.decode("cp949")
        return "cp949"
    except UnicodeDecodeError:
        return "unknown"


def zip_details(path: Path) -> dict:
    ents = parse_zip_cd(path.read_bytes())
    if ents is None:
        return {"note": "중앙 디렉터리를 이 파일 하나로 판독 불가(분할 등)"}
    encs, methods, aes_inner, enc_kinds = set(), set(), set(), set()
    efs_vals, up_vals, nfd_raw = set(), set(), False
    for e in ents:
        efs = bool(e["flags"] & 0x800)
        raw = e["name"]
        enc = guess_encoding(raw, efs)
        if enc != "ascii":
            encs.add(enc)
            efs_vals.add(efs)
            up_vals.add(0x7075 in e["extra"])
        methods.add(ZIP_METHODS.get(e["method"], str(e["method"])))
        if 0x9901 in e["extra"]:
            ver, vid, strength, inner = struct.unpack("<H2sBH", e["extra"][0x9901][:7])
            aes_inner.add(ZIP_METHODS.get(inner, str(inner)))
            enc_kinds.add(f"winzip-aes-{ {1: 128, 2: 192, 3: 256}.get(strength, strength)} (AE-{ver})")
        elif e["flags"] & 1:
            enc_kinds.add("zipcrypto")
        # NFD 여부: 원시 UTF-8 또는 0x7075 의 이름
        cand = []
        try:
            cand.append(raw.decode("utf-8"))
        except UnicodeDecodeError:
            pass
        if 0x7075 in e["extra"]:
            try:
                cand.append(e["extra"][0x7075][5:].decode("utf-8"))
            except UnicodeDecodeError:
                pass
        if any(c != nfc(c) for c in cand):
            nfd_raw = True
    d = {
        "raw_name_encoding": sorted(encs) or ["ascii"],
        "efs_flag_on_non_ascii": ("mixed" if len(efs_vals) > 1 else (efs_vals.pop() if efs_vals else None)),
        "unicode_path_extra_0x7075": ("mixed" if len(up_vals) > 1 else (up_vals.pop() if up_vals else None)),
        "methods": sorted(methods),
        "encryption": sorted(enc_kinds) or ["none"],
    }
    if aes_inner:
        d["aes_inner_methods"] = sorted(aes_inner)
    if nfd_raw:
        d["names_stored_as_nfd"] = True
    return d


def zipcrypto_password_encoding(path: Path, password: str) -> str | None:
    with zipfile.ZipFile(path) as z:
        target = next(i for i in z.infolist() if not i.is_dir() and i.file_size > 0 and i.flag_bits & 1)
        for enc in ("cp949", "utf-8"):
            try:
                z.read(target, pwd=password.encode(enc))
                return enc
            except Exception:
                continue
    return None


def aes_password_encoding(path: Path, password: str) -> str | None:
    data = path.read_bytes()
    for e in parse_zip_cd(data) or []:
        if 0x9901 not in e["extra"] or e["usize"] == 0:
            continue
        strength = e["extra"][0x9901][4]
        salt_len, key_len = {1: (8, 16), 2: (12, 24), 3: (16, 32)}[strength]
        lho = e["lho"]
        nlen, xlen = struct.unpack("<HH", data[lho + 26:lho + 30])
        start = lho + 30 + nlen + xlen
        salt = data[start:start + salt_len]
        pv = data[start + salt_len:start + salt_len + 2]
        for enc in ("utf-8", "cp949"):
            dk = hashlib.pbkdf2_hmac("sha1", password.encode(enc), salt, 1000, 2 * key_len + 2)
            if dk[-2:] == pv:
                return enc
        return None
    return None


def tar_details(path: Path, mode: str) -> dict:
    with tarfile.open(path, mode, encoding="utf-8", errors="surrogateescape") as t:
        encs, pax = set(), False
        for m in t.getmembers():
            raw = m.name.encode("utf-8", "surrogateescape")
            enc = guess_encoding(raw, False)
            if enc != "ascii":
                encs.add(enc.replace("(플래그 없음)", ""))
            if m.pax_headers:
                pax = True
    return {"raw_name_encoding": sorted(encs) or ["ascii"], "pax_headers": pax}


def tar_magic(raw_tar: bytes) -> str:
    mg = raw_tar[257:265]
    return {b"ustar\x0000": "ustar(POSIX)", b"ustar  \x00": "GNU"}.get(mg, repr(mg))


def gzip_fname(path: Path) -> dict:
    d = path.read_bytes()
    flg = d[3]
    pos = 10
    if flg & 0x04:
        xlen = struct.unpack("<H", d[pos:pos + 2])[0]
        pos += 2 + xlen
    if not flg & 0x08:
        return {"gzip_fname": None}
    end = d.index(b"\x00", pos)
    raw = d[pos:end]
    return {"gzip_fname_raw_hex": raw.hex(), "gzip_fname_encoding": guess_encoding(raw, False),
            "gzip_fname_decoded": raw.decode("cp949", "replace") if guess_encoding(raw, False) == "cp949"
            else raw.decode("utf-8", "replace")}


def py7zr_details(path: Path, password: str | None) -> dict:
    try:
        import py7zr
    except Exception:
        return {"note": "py7zr 없음 — 7z 세부 판독 생략"}
    try:
        with py7zr.SevenZipFile(path, "r", password=password) as z:
            info = z.archiveinfo()
            d = {"methods": list(info.method_names), "solid": bool(info.solid)}
            if password:
                z.testzip()
                d["password_encoding"] = "utf-16le (7z 규격; py7zr 로 해제 확인)"
            return d
    except Exception as e:  # 판독 실패는 기록만
        return {"note": f"py7zr 판독 실패: {type(e).__name__}: {e}"}


# ---------------------------------------------------------------- 생성기

class Generator:
    def __init__(self, bz: Bz | None, tmp: Path):
        self.bz = bz
        self.tmp = tmp
        self.fixtures: list[dict] = []
        self.could_not: list[dict] = []
        self.errors: list[str] = []
        self._n = 0

    # ---- 공통
    def tmpdir(self, name: str) -> Path:
        self._n += 1
        p = self.tmp / f"{self._n:02d}_{name}"
        p.mkdir(parents=True)
        return p

    def disp(self, p: Path) -> str:
        try:
            return str(p.relative_to(ROOT))
        except ValueError:
            return "<임시>\\" + p.name

    def verify_tree(self, fx: dict, archive: Path, expected: list[dict], password: str | None,
                    list_password: str | None):
        """목록 + 실제 해제 결과를 기대 항목과 비교."""
        rc, out, listed = bz_list(self.bz, archive, list_password)
        errs = compare(expected, listed, f"[{fx['id']}] 목록", check_hash=False)
        raw_nfd = any(e["_raw"] != e["path"] for e in listed)
        if raw_nfd or fx.get("nfd_names"):
            fx.setdefault("bandizip_view", {})["list_names_nfd"] = raw_nfd
        dest = self.tmpdir("x_" + fx["id"])
        bz_extract(self.bz, archive, dest, password)
        got = scan_tree(dest)
        errs += compare(expected, got, f"[{fx['id']}] 해제", check_hash=True)
        if any(e["_raw"] != nfc(e["_raw"]) for e in got):
            fx.setdefault("bandizip_view", {})["extracted_names_nfd"] = True
        elif raw_nfd or fx.get("nfd_names"):
            fx.setdefault("bandizip_view", {})["extracted_names_nfd"] = False
        return errs

    def verify_single(self, fx: dict, archive: Path, expected_one: dict):
        rc, out, listed = bz_list(self.bz, archive)
        fx.setdefault("bandizip_view", {})["listed_names"] = [e["path"] for e in listed]
        dest = self.tmpdir("x_" + fx["id"])
        bz_extract(self.bz, archive, dest)
        got = [e for e in scan_tree(dest) if not e["is_dir"]]
        errs = []
        if len(got) != 1:
            errs.append(f"[{fx['id']}] 해제 결과 파일 수 {len(got)} (기대 1)")
        else:
            g = got[0]
            fx["bandizip_view"]["extracted_name"] = g["path"]
            if g["size"] != expected_one["size"] or g["sha256"] != expected_one["sha256"]:
                errs.append(f"[{fx['id']}] 해제 내용 불일치 {g['path']}")
        return errs

    def verify_two_level(self, fx: dict, archive: Path, opener, expected: list[dict], tar_encoding: str):
        """.tar.xz / .tbz2: 반디집은 안쪽 .tar 하나로 보여 줌 → (1) 반디집 목록·해제가 .tar 하나이고
        내용이 inner_tar 와 같은지, (2) 안쪽 tar 의 항목이 expected 와 같은지(파이썬 tarfile) 둘 다 검증."""
        errs = []
        with opener(archive) as f:
            inner = f.read()
        rc, out, listed = bz_list(self.bz, archive)
        fx.setdefault("bandizip_view", {})["listed_names"] = [e["path"] for e in listed]
        fx["bandizip_view"]["shows_inner_tar_only"] = (len(listed) == 1 and listed[0]["path"].endswith(".tar"))
        dest = self.tmpdir("x_" + fx["id"])
        bz_extract(self.bz, archive, dest)
        got = [e for e in scan_tree(dest) if not e["is_dir"]]
        if len(got) == 1 and got[0]["path"].endswith(".tar"):
            if got[0]["sha256"] != sha256(inner):
                errs.append(f"[{fx['id']}] 반디집이 꺼낸 .tar 가 inner_tar 와 다름")
        elif compare(expected, got, "", True):
            errs.append(f"[{fx['id']}] 반디집 해제 결과가 .tar 하나도, 기대 트리도 아님: {[e['path'] for e in got][:5]}")
        import io
        ents = []
        with tarfile.open(fileobj=io.BytesIO(inner), mode="r:", encoding=tar_encoding) as t:
            for m in t.getmembers():
                p = nfc(m.name.rstrip("/"))
                if m.isdir():
                    ents.append({"path": p, "size": 0, "sha256": None, "is_dir": True})
                else:
                    data = t.extractfile(m).read()
                    ents.append({"path": p, "size": len(data), "sha256": sha256(data), "is_dir": False})
        errs += compare(expected, ents, f"[{fx['id']}] 안쪽 tar({tar_encoding})", check_hash=True)
        return errs

    KEY_ORDER = ["id", "file", "format", "password", "password_encoding", "header_encrypted", "split_volumes",
                 "root_folder", "created_by", "bandizip_made", "commit_ok", "verified", "verify_errors",
                 "iljip_known_gaps", "nfd_names", "single_file_stream", "inner_tar", "bandizip_view", "details",
                 "notes", "expected_entries"]

    @classmethod
    def reorder(cls, fx: dict):
        ordered = {k: fx[k] for k in cls.KEY_ORDER if k in fx}
        ordered.update({k: v for k, v in fx.items() if k not in ordered})
        fx.clear()
        fx.update(ordered)

    def add(self, fx: dict, errs: list[str]):
        fx["verified"] = not errs
        fx.setdefault("bandizip_made", True)
        if errs:
            fx["verify_errors"] = errs
            self.errors += errs
        self.reorder(fx)
        self.fixtures.append(fx)
        status = "OK" if not errs else f"검증 실패 {len(errs)}건"
        print(f"  - {fx['id']:<32} {status}  {fx['file']}")

    def safe(self, fid: str, fn, *a, **k):
        try:
            fn(*a, **k)
        except Exception as e:
            msg = f"{type(e).__name__}: {e}"
            print(f"  ! {fid}: {msg}")
            traceback.print_exc()
            self.could_not.append({"id": fid, "reason": f"생성/검증 중 예외 — {msg}"})

    # ---- bz.exe 로 만드는 일반 트리 픽스처
    def bz_tree(self, fid: str, fmt: str, out: Path, args: list[str], src_dir: Path, *,
                password: str | None = None, header_encrypted: bool = False, store_root: bool = True,
                gaps=(), notes: str = "", commit_ok: bool = True, expected=None, volumes_glob=None,
                open_file: Path | None = None, details=None, src_label: str | None = None, extra=None):
        cmd = ["c", "-y", *args, out, src_dir]
        self.bz.run(cmd)
        if expected is None:
            expected = scan_tree(src_dir, src_dir.name if store_root else None)
        open_file = open_file or out
        vols = sorted(out.parent.glob(volumes_glob)) if volumes_glob else []
        if volumes_glob and open_file not in vols:
            vols.append(open_file)
        fx = {
            "id": fid,
            "file": open_file.relative_to(ROOT).as_posix(),
            "format": fmt,
            "password": password,
            "header_encrypted": header_encrypted,
            "split_volumes": [v.relative_to(ROOT).as_posix() for v in vols],
            "created_by": f"bandizip {self.bz.version} bz.exe " + " ".join(
                [*map(str, cmd[:-2]), f'"{self.disp(out)}"', f'"{src_label or self.disp(src_dir)}"']),
            "commit_ok": commit_ok,
            "root_folder": nfc(src_dir.name) if store_root else None,
        }
        if extra:
            fx.update(extra)
        if gaps:
            fx["iljip_known_gaps"] = list(gaps)
        if notes:
            fx["notes"] = notes
        errs = []
        # 헤더 암호화 실측: 암호 없이 목록이 실패해야 함
        if password:
            rc, _o, _l = bz_list(self.bz, open_file, None, check=False)
            measured = rc != 0
            if measured != header_encrypted:
                errs.append(f"[{fid}] 헤더 암호화 실측={measured} 기대={header_encrypted}")
        errs += self.verify_tree(fx, open_file, expected, password,
                                 password if header_encrypted else None)
        if details:
            fx["details"] = details(open_file)
        fx["expected_entries"] = public(expected)
        self.add(fx, errs)
        return fx

    # ---- bz.exe 로 만드는 단일 파일 스트림(gz/xz)
    def bz_single(self, fid: str, fmt: str, out: Path, args: list[str], src_file: Path, gaps=(),
                  notes="", details=None):
        cmd = ["c", "-y", *args, out, src_file]
        self.bz.run(cmd)
        data = src_file.read_bytes()
        exp = {"path": nfc(src_file.name), "size": len(data), "sha256": sha256(data), "is_dir": False}
        fx = {
            "id": fid, "file": out.relative_to(ROOT).as_posix(), "format": fmt, "password": None,
            "header_encrypted": False, "split_volumes": [],
            "created_by": f"bandizip {self.bz.version} bz.exe " + " ".join(
                [*map(str, cmd[:-2]), f'"{self.disp(out)}"', f'"{self.disp(src_file)}"']),
            "commit_ok": True, "root_folder": None, "single_file_stream": True,
        }
        if gaps:
            fx["iljip_known_gaps"] = list(gaps)
        if notes:
            fx["notes"] = notes
        errs = self.verify_single(fx, out, exp)
        if details:
            fx["details"] = details(out)
        fx["expected_entries"] = [exp]
        self.add(fx, errs)

    def python_fixture(self, fx: dict, expected: list[dict], password=None, header_encrypted=False,
                       verify_with_bz=True, extra_errs=()):
        fx.setdefault("password", password)
        fx.setdefault("header_encrypted", header_encrypted)
        fx.setdefault("split_volumes", [])
        fx.setdefault("commit_ok", True)
        fx["bandizip_made"] = False
        errs = list(extra_errs)
        if verify_with_bz and self.bz:
            if password:
                rc, _o, _l = bz_list(self.bz, ROOT / fx["file"], None, check=False)
                if (rc != 0) != header_encrypted:
                    errs.append(f"[{fx['id']}] 헤더 암호화 실측={rc != 0} 기대={header_encrypted}")
            errs += self.verify_tree(fx, ROOT / fx["file"], expected, password,
                                     password if header_encrypted else None)
        fx["expected_entries"] = public(expected)
        self.add(fx, errs)


# ---------------------------------------------------------------- 메인 절차

def rm_owned(p: Path):
    assert p.parent == ROOT, p
    if p.exists():
        shutil.rmtree(p)


def build(bz: Bz | None, tmp: Path) -> Generator:
    g = Generator(bz, tmp)

    # 1) 원본
    for d in (SRC, OUT_BZ, OUT_PY, OUT_LOCAL):
        rm_owned(d)
        d.mkdir(parents=True)
    sample_dir = SRC / SAMPLE
    split_dir = SRC / SPLIT
    write_tree(sample_dir, sample_files(), SAMPLE_EMPTY_DIRS)
    write_tree(split_dir, split_files())
    nfd_parent = g.tmpdir("nfd_src")
    nfd_dir = nfd_parent / nfd(NFD_ROOT)
    write_tree(nfd_dir, nfd_files(), form=nfd)
    est_file = sample_dir / "견적서_2026.txt"
    print(f"원본 생성: {SRC}")

    if bz is None:
        raise FixtureError("bz.exe 없음")

    B = OUT_BZ
    T = g.bz_tree
    S = g.bz_single
    pw, pwk = f"-p:{PW}", f"-p:{PW_KO}"

    print("반디집 픽스처:")
    # ---- ZIP
    g.safe("zip_default", T, "zip_default", "zip", B / "zip_default.zip", ["-fmt:zip"], sample_dir,
           details=zip_details,
           notes="bz.exe 기본값. 한국어 Windows 에서 원시 이름=CP949(EFS 플래그 없음) + 0x7075 UTF-8 이름 추가필드. Deflate, 레벨 5.")
    g.safe("zip_cp949", T, "zip_cp949", "zip", B / "zip_cp949.zip", ["-fmt:zip", "-cp:949"], sample_dir,
           details=zip_details, notes="코드페이지 명시(-cp:949). 원시 이름 CP949 + 0x7075 추가필드.")
    g.safe("zip_utf8", T, "zip_utf8", "zip", B / "zip_utf8.zip", ["-fmt:zip", "-cp:65001"], sample_dir,
           details=zip_details,
           notes="-cp:65001: 원시 이름이 UTF-8 이지만 EFS 플래그(bit 11)는 꺼져 있음 + 0x7075. 플래그만 보고 CP437/CP949 로 풀면 깨짐.")
    g.safe("zip_store_l0", T, "zip_store_l0", "zip", B / "zip_store_l0.zip", ["-fmt:zip", "-l:0"], sample_dir,
           details=zip_details, notes="레벨 0 = 저장(Store).")
    g.safe("zip_storeroot_no", T, "zip_storeroot_no", "zip", B / "zip_storeroot_no.zip",
           ["-fmt:zip", "-storeroot:no"], sample_dir, store_root=False, details=zip_details,
           notes="-storeroot:no: 최상위 폴더 없이 내용물이 압축 루트에 바로 들어감(여러 파일 선택 압축과 같은 모양).")
    g.safe("zip_zipcrypto", T, "zip_zipcrypto", "zip", B / "zip_zipcrypto.zip", ["-fmt:zip", pw], sample_dir,
           password=PW, details=zip_details, gaps=["I2-11"],
           notes="bz.exe 의 -fmt:zip -p: 는 ZipCrypto 고정(AES 선택 스위치 없음). 파일명은 노출. 일집은 이 방식으로 '만들기'를 못 함(I2-11) — 열기는 되어야 함.")
    g.safe("zip_zipcrypto_korean_pw", lambda: _zip_korean_crypto(g, sample_dir))
    g.safe("zip_aes256", T, "zip_aes256", "zip", B / "zip_aes256.zip", ["-fmt:zipx", "-l:0", pw], sample_dir,
           password=PW, details=zip_details,
           notes="WinZip AES-256(AE-2) + 내부 방식 Store. bz.exe 는 -fmt:zip 으로 AES 를 못 만들어 -fmt:zipx -l:0 으로 만듦 → 결과는 일반 AES ZIP(EFS 플래그 켜짐).")
    g.safe("zip_aes256_korean_pw", lambda: _zip_korean_aes(g, sample_dir))
    g.safe("zip_split", T, "zip_split", "zip", B / "zip_split.zip", ["-fmt:zip", f"-v:{VOLUME}"], split_dir,
           volumes_glob="zip_split.z[0-9][0-9]", details=None, gaps=["I2-2(확인 대상: .zip+.z01)"],
           notes=f"PKZIP 방식 분할(-v:{VOLUME}): .z01, .z02 … 마지막이 .zip. 여는 파일은 .zip.")
    g.safe("zipx_default", T, "zipx_default", "zipx", B / "zipx_default.zipx", ["-fmt:zipx"], sample_dir,
           details=zip_details, gaps=["I2-9"], notes="ZIPX: 항목 압축 방식 XZ(95), EFS 플래그 켜짐.")
    g.safe("zipx_aes", T, "zipx_aes", "zipx", B / "zipx_aes.zipx", ["-fmt:zipx", pw], sample_dir,
           password=PW, details=zip_details, gaps=["I2-9"], notes="ZIPX + AES-256(내부 방식 XZ).")

    # ---- 7Z
    g.safe("7z_default", T, "7z_default", "7z", B / "7z_default.7z", ["-fmt:7z"], sample_dir,
           details=lambda p: py7zr_details(p, None))
    g.safe("7z_header_encrypted", T, "7z_header_encrypted", "7z", B / "7z_header_encrypted.7z",
           ["-fmt:7z", pw], sample_dir, password=PW, header_encrypted=True,
           details=lambda p: py7zr_details(p, PW), gaps=["I1-7"],
           notes="bz.exe 의 -fmt:7z -p: 는 항상 헤더(파일명) 암호화 — 암호 없이는 목록도 안 나옴.")
    g.safe("7z_header_encrypted_korean_pw", T, "7z_header_encrypted_korean_pw", "7z",
           B / "7z_header_encrypted_korean_pw.7z", ["-fmt:7z", pwk], sample_dir, password=PW_KO,
           header_encrypted=True, details=lambda p: py7zr_details(p, PW_KO), gaps=["I1-7"],
           extra={"password_encoding": "utf-16le"},
           notes="한글 암호. 7z 규격상 암호는 UTF-16LE.")
    g.safe("7z_split", T, "7z_split", "7z", B / "7z_split.7z", ["-fmt:7z", f"-v:{VOLUME}"], split_dir,
           volumes_glob="7z_split.7z.[0-9][0-9][0-9]", open_file=B / "7z_split.7z.001", gaps=["I2-2"],
           notes=f"숫자 분할(-v:{VOLUME}): .7z.001, .002, .003. 여는 파일은 .001.")

    # ---- TAR 계열
    g.safe("tar_default", T, "tar_default", "tar", B / "tar_default.tar", ["-fmt:tar"], sample_dir,
           details=lambda p: {**tar_details(p, "r:"), "magic": tar_magic(p.read_bytes()[:512])},
           notes="bz.exe 기본 tar: 이름이 CP949 원시 바이트(PAX/UTF-8 아님).")
    g.safe("tar_utf8", T, "tar_utf8", "tar", B / "tar_utf8.tar", ["-fmt:tar", "-cp:65001"], sample_dir,
           details=lambda p: {**tar_details(p, "r:"), "magic": tar_magic(p.read_bytes()[:512])},
           notes="-cp:65001: tar 이름이 UTF-8 원시 바이트.")
    g.safe("tgz_default", lambda: _tgz(g, sample_dir, "tgz_default", B / "tgz_default.tgz",
                                       ["I2-9(.tgz 미등록)"]))
    g.safe("targz_default", lambda: _tgz(g, sample_dir, "targz_default", B / "targz_default.tar.gz",
                                         ["I2-9(.tar.gz 를 GZ 처리기가 받아 .tar 하나만 꺼냄)"]))
    g.safe("txz_two_step", lambda: _txz(g, sample_dir))
    g.safe("gz_single", S, "gz_single", "gz", B / "견적서_2026.txt.gz", ["-fmt:gz"], est_file,
           details=gzip_fname,
           notes="단일 파일 gzip. -fmt:gz 는 폴더를 못 받음. 반디집은 gzip 헤더에 원본 이름(FNAME)을 넣지 않음 → 이름은 압축 파일 이름에서 .gz 를 뗀 것.")
    g.safe("xz_single", S, "xz_single", "xz", B / "견적서_2026.txt.xz", ["-fmt:xz"], est_file,
           gaps=["I2-9"], notes="단일 파일 xz. -fmt:xz 는 폴더를 못 받음(0xa0000411).")

    # ---- 기타 형식
    g.safe("iso_default", T, "iso_default", "iso", B / "iso_default.iso", ["-fmt:iso"], sample_dir,
           gaps=["I2-9"], notes="반디집이 만드는 ISO. 크기 대부분이 0 채움.")
    g.safe("lzh_default", T, "lzh_default", "lzh", B / "lzh_default.lzh", ["-fmt:lzh"], sample_dir,
           gaps=["I2-9(대조표에 .lzh 명시 없음)"], notes="LZH(LHA). 일집 미지원 형식 — '못 연다' 오류 처리 확인용.")

    # ---- 맥 NFD 파일명 (반디집)
    nfd_expected = scan_tree(nfd_dir, nfd_dir.name)
    g.safe("nfd_bandizip_default", T, "nfd_bandizip_default", "zip", B / "nfd_bandizip_default.zip",
           ["-fmt:zip"], nfd_dir, expected=nfd_expected, details=zip_details, gaps=["I2-10"],
           src_label=f"<임시>\\{NFD_ROOT}(NFD 이름)", extra={"nfd_names": True},
           notes="Windows 에 NFD 이름 파일을 만들어 bz.exe 기본값으로 압축. 원시 CP949 이름은 호환 자모(ㅁㅐㄱ…)로 풀어진 형태, 0x7075 는 NFD UTF-8. expected_entries 경로는 NFC.")
    g.safe("nfd_bandizip_utf8", T, "nfd_bandizip_utf8", "zip", B / "nfd_bandizip_utf8.zip",
           ["-fmt:zip", "-cp:65001"], nfd_dir, expected=nfd_expected, details=zip_details, gaps=["I2-10"],
           src_label=f"<임시>\\{NFD_ROOT}(NFD 이름)", extra={"nfd_names": True},
           notes="맥 압축과 가장 비슷한 반디집 산출물: 원시 이름 UTF-8 NFD, EFS 플래그 없음(+0x7075 NFD). expected_entries 경로는 NFC.")

    # ---- SFX (커밋 금지)
    g.safe("sfx_default", T, "sfx_default", "exe-sfx", OUT_LOCAL / "sfx_default.exe", ["-fmt:exe"], sample_dir,
           commit_ok=False, details=_sfx_details,
           notes="반디집 SFX 스텁(bdzsfx.x86.sfx)이 들어 있어 재배포 불가 → local/ (git 제외). 없으면 테스트는 건너뛸 것.")

    # ---- Python (반디집 아님)
    print("Python 픽스처(반디집 아님):")
    g.safe("py7z_pw_names_visible", lambda: _py7z(g, sample_dir))
    g.safe("nfd_python_efs", lambda: _nfd_python(g, nfd_expected, efs=True))
    g.safe("nfd_python_noflag", lambda: _nfd_python(g, nfd_expected, efs=False))
    g.safe("zip_numbered_split", lambda: _numbered_split(g))
    g.safe("tbz2_python", lambda: _tbz2(g, sample_dir))
    g.safe("bz2_single_python", lambda: _bz2_single(g, est_file))
    return g


# ---- 개별 생성 도우미

def _zip_korean_crypto(g: Generator, sample_dir: Path):
    out = OUT_BZ / "zip_zipcrypto_korean_pw.zip"
    fx = g.bz_tree("zip_zipcrypto_korean_pw", "zip", out, ["-fmt:zip", f"-p:{PW_KO}"], sample_dir,
                   password=PW_KO, details=zip_details, gaps=["I2-11"],
                   notes="한글 암호 ZipCrypto. 암호 바이트 인코딩은 password_encoding 참고(반디집=CP949).")
    enc = zipcrypto_password_encoding(out, PW_KO)
    fx["password_encoding"] = enc
    if enc is None:
        g.errors.append("[zip_zipcrypto_korean_pw] 암호 인코딩 판정 실패")


def _zip_korean_aes(g: Generator, sample_dir: Path):
    out = OUT_BZ / "zip_aes256_korean_pw.zip"
    fx = g.bz_tree("zip_aes256_korean_pw", "zip", out, ["-fmt:zipx", "-l:0", f"-p:{PW_KO}"], sample_dir,
                   password=PW_KO, details=zip_details,
                   notes="한글 암호 AES-256(내부 Store). 암호 바이트 인코딩은 password_encoding(PBKDF2 검증값으로 판정).")
    enc = aes_password_encoding(out, PW_KO)
    fx["password_encoding"] = enc
    if enc is None:
        g.errors.append("[zip_aes256_korean_pw] 암호 인코딩 판정 실패")


def _tar_inner(path: Path, opener) -> dict:
    with opener(path) as f:
        raw = f.read()
    return {"size": len(raw), "sha256": sha256(raw), "magic": tar_magic(raw[:512])}


def _tgz(g: Generator, sample_dir: Path, fid: str, out: Path, gaps):
    import gzip
    fx = g.bz_tree(fid, "tar.gz", out, ["-fmt:tgz"], sample_dir, gaps=gaps,
                   details=lambda p: {**tar_details(p, "r:gz"), **gzip_fname(p)},
                   notes="expected_entries 는 안쪽 tar 의 내용. inner_tar 는 gzip 을 한 겹만 벗긴 .tar 자체(현재 GZ 처리기 동작 비교용).")
    inner = _tar_inner(out, gzip.open)
    inner["name"] = out.name[: -len(".tgz")] + ".tar" if out.name.endswith(".tgz") else out.name[:-3]
    fx["inner_tar"] = inner


def _txz(g: Generator, sample_dir: Path):
    import lzma
    tdir = g.tmpdir("txz")
    tar = tdir / "txz_two_step.tar"
    g.bz.run(["c", "-y", "-fmt:tar", tar, sample_dir])
    out = OUT_BZ / "txz_two_step.tar.xz"
    g.bz.run(["c", "-y", "-fmt:xz", out, tar])
    expected = scan_tree(sample_dir, sample_dir.name)
    fx = {
        "id": "txz_two_step", "file": out.relative_to(ROOT).as_posix(), "format": "tar.xz",
        "password": None, "header_encrypted": False, "split_volumes": [],
        "created_by": (f'bandizip {g.bz.version} bz.exe c -y -fmt:tar "<임시>\\txz_two_step.tar" "src\\{SAMPLE}"'
                       f' → bz.exe c -y -fmt:xz "bandizip\\txz_two_step.tar.xz" "<임시>\\txz_two_step.tar"'),
        "commit_ok": True, "root_folder": SAMPLE, "iljip_known_gaps": ["I2-9"],
        "notes": ("bz.exe 는 폴더를 바로 xz 로 못 묶어 tar → xz 두 단계. expected_entries 는 안쪽 tar 내용(이름 CP949 원시), "
                  "inner_tar 는 .tar 자체. 반디집 7.46 콘솔은 .tar.xz 를 안쪽 .tar 하나로 보여 주고 꺼냄(bandizip_view)."),
    }
    errs = g.verify_two_level(fx, out, lzma.open, expected, "cp949")
    inner = _tar_inner(out, lzma.open)
    inner["name"] = "txz_two_step.tar"
    fx["inner_tar"] = inner
    fx["details"] = tar_details(out, "r:xz")
    fx["expected_entries"] = public(expected)
    g.add(fx, errs)


def _sfx_details(p: Path) -> dict:
    d = {"size": p.stat().st_size}
    try:
        with zipfile.ZipFile(p) as z:
            d["payload"] = "zip(파이썬 zipfile 로 열림)"
            d["payload_entries"] = len(z.infolist())
    except zipfile.BadZipFile:
        data = p.read_bytes()
        d["payload"] = "7z" if b"7z\xbc\xaf\x27\x1c" in data else "unknown"
    return d


def _py7z(g: Generator, sample_dir: Path):
    try:
        import py7zr
    except Exception:
        g.could_not.append({"id": "py7z_pw_names_visible",
                            "reason": "py7zr 없음 — 파일명 노출 암호 7z 는 bz.exe 로 못 만들고(항상 헤더 암호화) 대체 생성도 불가"})
        return
    out = OUT_PY / "7z_pw_names_visible.7z"
    with py7zr.SevenZipFile(out, "w", password=PW, header_encryption=False) as z:
        z.writeall(sample_dir, arcname=SAMPLE)
    expected = scan_tree(sample_dir, SAMPLE)
    fx = {"id": "py7z_pw_names_visible", "file": out.relative_to(ROOT).as_posix(), "format": "7z",
          "created_by": f"python py7zr {getattr(py7zr, '__version__', '?')} SevenZipFile(password=PW, header_encryption=False).writeall(src/{SAMPLE})",
          "root_folder": SAMPLE,
          "notes": "반디집 아님. bz.exe 는 암호 7z 를 항상 헤더 암호화로 만들어 '파일명 노출 암호 7z' 를 py7zr 로 대체. 목록은 암호 없이, 해제는 암호 필요.",
          "details": py7zr_details(out, PW)}
    g.python_fixture(fx, expected, password=PW, header_encrypted=False)


def _clear_efs(data: bytes) -> bytes:
    b = bytearray(data)
    eocd = b.rfind(b"PK\x05\x06")
    _s, _dn, _cd, n, _nt, _sz, off, _cl = struct.unpack("<IHHHHIIH", bytes(b[eocd:eocd + 22]))
    pos = off
    for _ in range(n):
        flags, = struct.unpack("<H", b[pos + 8:pos + 10])
        struct.pack_into("<H", b, pos + 8, flags & ~0x800)
        nlen, xlen, clen = struct.unpack("<HHH", b[pos + 28:pos + 34])
        lho, = struct.unpack("<I", b[pos + 42:pos + 46])
        lflags, = struct.unpack("<H", b[lho + 6:lho + 8])
        struct.pack_into("<H", b, lho + 6, lflags & ~0x800)
        pos += 46 + nlen + xlen + clen
    return bytes(b)


def _nfd_python(g: Generator, nfd_expected: list[dict], efs: bool):
    fid = "nfd_python_efs" if efs else "nfd_python_noflag"
    out = OUT_PY / f"{fid}.zip"
    files = nfd_files()
    dirs = sorted({"/".join(k.split("/")[:i]) for k in files for i in range(1, len(k.split("/")))})
    bio_path = out
    with zipfile.ZipFile(bio_path, "w", zipfile.ZIP_DEFLATED) as z:
        def info(name: str, is_dir: bool):
            zi = zipfile.ZipInfo(name, date_time=FIXED_LOCAL.timetuple()[:6])
            zi.create_system = 3  # Unix(맥)
            zi.external_attr = ((0o40755 << 16) | 0x10) if is_dir else (0o100644 << 16)
            zi.compress_type = zipfile.ZIP_STORED if is_dir else zipfile.ZIP_DEFLATED
            return zi
        root = nfd(NFD_ROOT)
        z.writestr(info(root + "/", True), b"")
        for d in dirs:
            z.writestr(info(root + "/" + nfd(d) + "/", True), b"")
        for k in sorted(files):
            z.writestr(info(root + "/" + nfd(k), False), files[k])
    if not efs:
        out.write_bytes(_clear_efs(out.read_bytes()))
    fx = {"id": fid, "file": out.relative_to(ROOT).as_posix(), "format": "zip",
          "created_by": "python zipfile (NFD 이름 직접 기록" + ("" if efs else ", 이후 EFS 플래그 bit 11 을 지워 맥 '압축 유틸리티' 모양으로") + ")",
          "root_folder": NFD_ROOT, "nfd_names": True, "iljip_known_gaps": ["I2-10"],
          "notes": ("반디집 아님. 원시 이름 UTF-8 NFD + EFS 플래그 켜짐(맥/리눅스 zip 명령 모양)." if efs else
                    "반디집 아님. 원시 이름 UTF-8 NFD, EFS 플래그 없음, 0x7075 없음(맥 기본 '압축' 모양) — 이름 판별을 휴리스틱에만 의존.")
          + " expected_entries 경로는 NFC.",
          "details": zip_details(out)}
    g.python_fixture(fx, nfd_expected)


def _numbered_split(g: Generator):
    srcz = OUT_BZ / "zip_default.zip"
    data = srcz.read_bytes()
    n = 3
    chunk = math.ceil(len(data) / n)
    parts = []
    for i in range(n):
        p = OUT_PY / f"zip_numbered_split.zip.{i + 1:03d}"
        p.write_bytes(data[i * chunk:(i + 1) * chunk])
        parts.append(p)
    base = next(f for f in g.fixtures if f["id"] == "zip_default")
    expected = [dict(e) for e in base["expected_entries"]]
    fx = {"id": "zip_numbered_split", "file": parts[0].relative_to(ROOT).as_posix(), "format": "zip",
          "split_volumes": [p.relative_to(ROOT).as_posix() for p in parts],
          "created_by": f"python: bandizip/zip_default.zip 를 {chunk}바이트씩 단순 절단(7-Zip '분할' 방식 .zip.001 모사)",
          "root_folder": SAMPLE, "iljip_known_gaps": ["I2-2"],
          "notes": "반디집 아님(내용물 zip 은 반디집 산출물). 조각을 이어 붙이면 zip_default.zip 과 바이트 동일.",
          "details": {"joined_sha256": sha256(data)}}
    g.python_fixture(fx, expected)


def _tbz2(g: Generator, sample_dir: Path):
    out = OUT_PY / "tbz2_python.tbz2"

    def norm(ti: tarfile.TarInfo):
        ti.uid = ti.gid = 0
        ti.uname = ti.gname = ""
        ti.mtime = int(FIXED_MTIME)
        return ti
    with tarfile.open(out, "w:bz2", format=tarfile.PAX_FORMAT, encoding="utf-8") as t:
        t.add(sample_dir, arcname=SAMPLE, filter=norm)
    expected = scan_tree(sample_dir, SAMPLE)
    fx = {"id": "tbz2_python", "file": out.relative_to(ROOT).as_posix(), "format": "tar.bz2",
          "created_by": f"python tarfile w:bz2 PAX UTF-8 (src/{SAMPLE})", "root_folder": SAMPLE,
          "iljip_known_gaps": ["I2-9(.tbz2 미등록)"],
          "notes": ("반디집 아님(bz.exe 는 bzip2 생성 불가). expected_entries 는 안쪽 tar 내용(PAX UTF-8), inner_tar 는 .tar 자체. "
                    "반디집 7.46 콘솔은 .tbz2 를 안쪽 .tar 하나로 보여 주고 꺼냄(bandizip_view)."),
          "details": tar_details(out, "r:bz2")}
    inner = _tar_inner(out, bz2.open)
    inner["name"] = "tbz2_python.tar"
    fx["inner_tar"] = inner
    errs = g.verify_two_level(fx, out, bz2.open, expected, "utf-8")
    g.python_fixture(fx, expected, verify_with_bz=False, extra_errs=errs)


def _bz2_single(g: Generator, est_file: Path):
    data = est_file.read_bytes()
    out = OUT_PY / (est_file.name + ".bz2")
    out.write_bytes(bz2.compress(data, 9))
    exp = {"path": nfc(est_file.name), "size": len(data), "sha256": sha256(data), "is_dir": False}
    fx = {"id": "bz2_single_python", "file": out.relative_to(ROOT).as_posix(), "format": "bz2",
          "password": None, "header_encrypted": False, "split_volumes": [],
          "created_by": "python bz2.compress(level 9)", "commit_ok": True, "root_folder": None,
          "single_file_stream": True, "bandizip_made": False,
          "notes": "반디집 아님(bz.exe 는 bzip2 생성 불가). 단일 파일 스트림."}
    errs = g.verify_single(fx, out, exp) if g.bz else []
    fx["expected_entries"] = [exp]
    g.add(fx, errs)


# ---------------------------------------------------------------- 진입점

def replace_retry(src: Path, dst: Path, tries: int = 15, delay: float = 1.0) -> None:
    """os.replace(이름 바꾸기)를 몇 번만 다시 해 본다. 갓 복사한 폴더는 백신·DLP·동기화 프로그램이 잠깐 파일을 잡고 있어
    '액세스가 거부되었습니다'(WinError 5)가 날 수 있다(2026-10-12 실측). 정해진 횟수만 — 무한 반복하지 않는다."""
    import time
    for i in range(tries):
        try:
            os.replace(src, dst)
            return
        except PermissionError:
            if i == tries - 1:
                raise
            time.sleep(delay)


def swap_in(staging: Path) -> None:
    """검증을 통과한 staging 의 결과물(OWNED)을 FIX 로 바꿔 넣는다. 중간에 실패하면 기존 것을 되돌려 놓는다."""
    backup = FIX / "_backup"
    if backup.exists():
        shutil.rmtree(backup)
    backup.mkdir()
    try:
        for name in OWNED:
            if (FIX / name).exists():
                replace_retry(FIX / name, backup / name)
        for name in OWNED:
            if (staging / name).exists():
                replace_retry(staging / name, FIX / name)
    except Exception:
        print("  ! 바꿔 넣기 실패 — 기존 픽스처를 되돌립니다", file=sys.stderr)
        for name in OWNED:
            if not (backup / name).exists():
                continue
            cur = FIX / name
            if cur.exists():   # 이미 들어간 새 것은 staging 으로 돌려보낸다(staging 쪽에 같은 이름이 남아 있으면 지움)
                if (staging / name).exists():
                    shutil.rmtree(cur) if cur.is_dir() else cur.unlink()
                else:
                    replace_retry(cur, staging / name)
            replace_retry(backup / name, cur)
        if not any(backup.iterdir()):
            backup.rmdir()
        raise
    shutil.rmtree(backup)


def old_fixture_ids() -> set[str]:
    m = FIX / "manifest.json"
    if not m.is_file():
        return set()
    try:
        return {f["id"] for f in json.loads(m.read_text(encoding="utf-8"))["fixtures"]}
    except Exception:
        return set()


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--bz", default=str(DEFAULT_BZ), help="bz.exe 경로 (기본: %(default)s)")
    ap.add_argument("--out", help="이 폴더(없거나 빈 폴더)에만 만들고 Fixtures 로 바꿔 넣지 않음 — 재현성 비교·검토용")
    ap.add_argument("--keep-temp", action="store_true", help="임시 폴더(실패 시 _staging 포함)를 지우지 않음(디버그)")
    a = ap.parse_args()

    bz_path = Path(a.bz)
    if not bz_path.is_file():
        print(f"오류: 반디집 콘솔 bz.exe 를 찾을 수 없습니다: {bz_path}\n"
              f"  이 스크립트는 반디집 7.x 의 bz.exe 로 기준 압축 파일을 만듭니다.\n"
              f"  반디집이 삭제된 뒤에는 재생성할 수 없으니 저장소에 커밋된 Fixtures/bandizip/ 을 그대로 쓰세요.\n"
              f"  다른 위치에 있으면 --bz 로 지정하세요.", file=sys.stderr)
        return 2

    if a.out:
        out_root = Path(a.out).resolve()
        if out_root == FIX or FIX in out_root.parents and out_root.name in OWNED:
            print(f"오류: --out 은 Fixtures 폴더 자체나 그 안의 {OWNED} 일 수 없습니다: {out_root}", file=sys.stderr)
            return 2
        if out_root.exists() and any(out_root.iterdir()):
            print(f"오류: --out 폴더가 비어 있지 않습니다: {out_root}", file=sys.stderr)
            return 2
        swap = False
    else:
        # 시스템 임시 폴더에서 만든다: 저장소 폴더가 OneDrive 같은 동기화 영역이면 새 파일에 동기화 속성(0x80000)이 붙고
        # bz.exe 의 7z 가 그 속성을 그대로 기록해, 같은 내용인데도 어디서 만드느냐에 따라 바이트가 달라진다.
        out_root = Path(tempfile.mkdtemp(prefix="iljip_fixtures_out_"))
        swap = True
    out_root.mkdir(parents=True, exist_ok=True)
    set_root(out_root)
    print(f"출력: {out_root}" + ("  (검증을 모두 통과하면 Fixtures 로 바꿔 넣음)" if swap else "  (바꿔 넣지 않음)"))

    bz = Bz(bz_path)
    print(f"bz.exe: {bz_path}  ({bz.banner})")

    tmp = Path(tempfile.mkdtemp(prefix="iljip_fixtures_"))
    try:
        g = build(bz, tmp)
    finally:
        if a.keep_temp:
            print(f"임시 폴더 유지: {tmp}")
        else:
            shutil.rmtree(tmp, onerror=lambda f, p, e: print(f"  (임시 정리 실패: {p}: {e[1]})"))

    could_not = [
        {"id": "7z_pw_names_visible(bandizip)",
         "reason": "bz.exe 의 -fmt:7z -p: 는 항상 헤더(파일명) 암호화 — 끄는 스위치 없음. python/7z_pw_names_visible.7z(py7zr)로 대체."},
        {"id": "zip_aes256(-fmt:zip)",
         "reason": "bz.exe 의 -fmt:zip -p: 는 ZipCrypto 고정. AES-256 은 -fmt:zipx 로만 가능 → zip_aes256.zip 은 -fmt:zipx -l:0 산출물(일반 AES ZIP 과 동일 구조)."},
        {"id": "zip_utf8_efs(bandizip)",
         "reason": "bz.exe 의 -fmt:zip 은 -cp:65001 이어도 EFS 플래그(bit 11)를 켜지 않음. EFS 켜진 UTF-8 ZIP 은 zipx 계열(zip_aes256.zip, zipx_*)과 python/nfd_python_efs.zip 이 대신 다룸."},
        {"id": "xz(폴더)", "reason": "-fmt:xz 는 파일 하나만 받음(폴더 시 0xa0000411). → txz_two_step.tar.xz(tar→xz 두 단계)와 xz_single."},
        {"id": "tbz2/bz2(bandizip)", "reason": "bz.exe -fmt: 목록에 bzip2 없음. python/ 에 tbz2_python.tbz2, 견적서_2026.txt.bz2 로 대체."},
        {"id": "rar/egg/alz(bandizip)", "reason": "반디집은 RAR·EGG·ALZ 생성 불가(-fmt: 목록에 없음)."},
        {"id": "zip_numbered_split(bandizip)", "reason": "bz.exe 의 zip 분할은 .z01 방식만. .zip.001 은 python 단순 절단으로 모사(python/zip_numbered_split.zip.001~)."},
    ] + g.could_not
    for fx in g.fixtures:
        Generator.reorder(fx)

    manifest = {
        "schema": 1,
        "generator": "Iljip.Tests/Fixtures/make_fixtures.py (python -I make_fixtures.py)",
        "generated_at": dt.datetime.now().astimezone().isoformat(timespec="seconds"),
        "bandizip": {"bz_exe": str(bz_path), "banner": bz.banner, "version": bz.version},
        "passwords": {"default": PW, "korean": PW_KO},
        "fixed_mtime_local": FIXED_LOCAL.isoformat(),
        "conventions": {
            "file": "Fixtures 폴더 기준 상대경로('/' 구분). 분할이면 여는 파일(첫 .001 또는 마지막 .zip).",
            "expected_entries.path": "'/' 구분, NFC, 폴더는 끝 '/' 없음, 압축 안 최상위 폴더(root_folder) 포함.",
            "expected_entries.sha256": "파일 내용 sha256(소문자 16진). 폴더는 null.",
            "header_encrypted": "true 면 암호 없이는 목록도 못 읽음(bz.exe 로 실측).",
            "commit_ok": "false 면 git 제외(local/) — 파일이 없으면 테스트는 건너뛸 것.",
            "single_file_stream": "gz/xz/bz2 단일 파일. expected_entries 한 개(원본 이름).",
            "inner_tar": "tar.gz/tar.xz/tar.bz2 의 압축 한 겹만 벗긴 .tar 자체(크기·sha256).",
            "bandizip_made": "false 면 Python 산출물(반디집 아님).",
            "iljip_known_gaps": KNOWN_GAPS_NOTE,
            "verified": "만든 직후 bz.exe 목록·해제 결과가 expected_entries 와 일치했는지.",
        },
        "fixtures": g.fixtures,
        "could_not_create": could_not,
    }
    MANIFEST.write_text(json.dumps(manifest, ensure_ascii=False, indent=1) + "\n", encoding="utf-8", newline="\n")
    print(f"매니페스트: {MANIFEST}")

    # 크기 요약
    def du(p: Path) -> int:
        return sum(f.stat().st_size for f in p.rglob("*") if f.is_file())
    commit = du(SRC) + du(OUT_BZ) + du(OUT_PY) + MANIFEST.stat().st_size + Path(__file__).stat().st_size
    print(f"크기: src {du(SRC):,} / bandizip {du(OUT_BZ):,} / python {du(OUT_PY):,} / local(제외) {du(OUT_LOCAL):,}"
          f"  → 커밋 대상 합계 {commit:,} 바이트 ({commit / 1048576:.2f} MiB)")
    dropped = sorted(old_fixture_ids() - {f["id"] for f in g.fixtures})
    if g.errors or g.could_not or dropped:
        print(f"\n검증 오류 {len(g.errors)}건, 생성 실패 {len(g.could_not)}건, 기존보다 빠진 픽스처 {len(dropped)}개:")
        for e in g.errors:
            print("  -", e)
        for c in g.could_not:
            print("  -", c["id"], c["reason"])
        for d in dropped:
            print("  - 빠짐:", d)
        if swap:
            if a.keep_temp:
                print(f"기존 Fixtures 는 그대로 둠. 실패한 결과물: {out_root}")
            else:
                shutil.rmtree(out_root, ignore_errors=True)
                print("기존 Fixtures 는 그대로 둠(실패한 결과물은 지움).")
        return 1
    if swap:
        # 먼저 Fixtures/_staging 으로 통째로 복사(여기서 실패해도 기존 픽스처는 그대로) → 같은 볼륨 안 이름 바꾸기로 교체
        staging = FIX / "_staging"
        if staging.exists():
            shutil.rmtree(staging)
        staging.mkdir()
        try:
            for name in OWNED:
                src_p = out_root / name
                if src_p.is_dir():
                    shutil.copytree(src_p, staging / name)
                elif src_p.is_file():
                    shutil.copy2(src_p, staging / name)
            swap_in(staging)
        finally:
            shutil.rmtree(staging, ignore_errors=True)
            if not a.keep_temp:
                shutil.rmtree(out_root, ignore_errors=True)
        print(f"\n완료: 픽스처 {len(g.fixtures)}개, 모두 검증 통과 → {FIX} 로 바꿔 넣음.")
    else:
        print(f"\n완료: 픽스처 {len(g.fixtures)}개, 모두 검증 통과 → {out_root} (바꿔 넣지 않음).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
