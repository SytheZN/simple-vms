#!/usr/bin/env python3
"""Generate H.264 CAVLC and CABAC table files from cisco/openh264 sources.

Run from the project root:

    ./scripts/generators/build-h264-tables.py

Downloads its sources from cisco/openh264 master into the script's own directory
(gitignored), parses every numeric table, and writes four C# files into
src/plugins/shared/H264:

  CavlcTables.cs                    CAVLC lookup tables
  CabacArithmeticTables.cs          RangeTabLps / TransIdxLps / TransIdxMps
                                    (HEVC reuses this set verbatim)
  CabacContextInitTables.cs         InitM / InitN per-context init slope/offset
  ResidualTables.cs                 zig-zag scans, 8x8 significance context maps,
                                    block-category context offsets, dequant
                                    coefficients, default scaling lists, and the
                                    inverse transform basis

Everything is parsed out of upstream except the two inverse transform functions,
which are transcribed by hand into inverse4/inverse8 because openh264 states them
as butterflies rather than as tables. The basis matrices are then their impulse
response, so they cannot drift from the code they came from. Every generated file
carries upstream's licence block verbatim.
"""

# inverse4, inverse8 and windows() below are transcribed from cisco/openh264 source
# rather than parsed out of it, which makes this script itself a derivative work.
#
# \copy
#     Copyright (c)  2013, Cisco Systems
#     All rights reserved.
#
#     Redistribution and use in source and binary forms, with or without
#     modification, are permitted provided that the following conditions
#     are met:
#
#        * Redistributions of source code must retain the above copyright
#          notice, this list of conditions and the following disclaimer.
#
#        * Redistributions in binary form must reproduce the above copyright
#          notice, this list of conditions and the following disclaimer in
#          the documentation and/or other materials provided with the
#          distribution.
#
#     THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
#     "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
#     LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS
#     FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
#     COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
#     INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
#     BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
#     LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
#     CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
#     LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN
#     ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
#     POSSIBILITY OF SUCH DAMAGE.

import math
import re
import subprocess
import sys
from fractions import Fraction
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parent.parent
SHARED_FORMATS = REPO_ROOT / "src/plugins/shared/H264"

CAVLC_SRC = SCRIPT_DIR / "decoder_data_tables.cpp"
CABAC_SRC = SCRIPT_DIR / "common_tables.cpp"
BASIS_SRC = SCRIPT_DIR / "wels_common_basis.h"
SYNTAX_SRC = SCRIPT_DIR / "parse_mb_syn_cabac.cpp"
IDCT_SRC = SCRIPT_DIR / "decode_mb_aux.cpp"
PRED_SRC = SCRIPT_DIR / "get_intra_predictor.cpp"
OUT_CAVLC = SHARED_FORMATS / "CavlcTables.cs"
OUT_ARITH = SHARED_FORMATS / "CabacArithmeticTables.cs"
OUT_CINIT = SHARED_FORMATS / "CabacContextInitTables.cs"
OUT_RESID = SHARED_FORMATS / "ResidualTables.cs"

UPSTREAM = "https://raw.githubusercontent.com/cisco/openh264/master/codec/"
CAVLC_URL = UPSTREAM + "decoder/core/src/decoder_data_tables.cpp"
CABAC_URL = UPSTREAM + "common/src/common_tables.cpp"
BASIS_URL = UPSTREAM + "decoder/core/inc/wels_common_basis.h"
SYNTAX_URL = UPSTREAM + "decoder/core/src/parse_mb_syn_cabac.cpp"
IDCT_URL = UPSTREAM + "decoder/core/src/decode_mb_aux.cpp"
PRED_URL = UPSTREAM + "decoder/core/src/get_intra_predictor.cpp"

SOURCES = [
  (CAVLC_SRC, CAVLC_URL),
  (CABAC_SRC, CABAC_URL),
  (BASIS_SRC, BASIS_URL),
  (SYNTAX_SRC, SYNTAX_URL),
  (IDCT_SRC, IDCT_URL),
  (PRED_SRC, PRED_URL),
]


def ensure_sources():
  for path, url in SOURCES:
    if not path.exists():
      print(f"fetching {url}", file=sys.stderr)
      subprocess.run(["wget", "-q", "-O", str(path), url], check=True)


def strip_comments(s):
  s = re.sub(r"//[^\n]*", "", s)
  s = re.sub(r"/\*.*?\*/", "", s, flags=re.DOTALL)
  return s


def parse_init(s, idx):
  assert s[idx] == "{", f"expected '{{' at {idx}, got {s[idx:idx+10]!r}"
  idx += 1
  out = []
  while True:
    while idx < len(s) and (s[idx].isspace() or s[idx] == ","):
      idx += 1
    if s[idx] == "}":
      return out, idx + 1
    if s[idx] == "{":
      sub, idx = parse_init(s, idx)
      out.append(sub)
      continue
    m = re.match(r"-\s*\d+|\d+|CTX_NA|IDX_UNUSED", s[idx:])
    if not m:
      raise ValueError(f"unexpected token at {idx}: {s[idx:idx+30]!r}")
    tok = m.group(0)
    # Both mark an entry the decoder never indexes; zero keeps the array dense.
    out.append(0 if tok in ("CTX_NA", "IDX_UNUSED") else int(tok.replace(" ", "")))
    idx += m.end()


def extract(src, name):
  # The trailing ", 16)" arm covers ALIGNED_DECLARE(type, name[a][b], alignment).
  pat = re.compile(
    r"\b" + re.escape(name) + r"\b\s*(?:\[[^\]]*\])*\s*(?:,\s*\d+\s*\))?\s*=\s*")
  m = pat.search(src)
  if not m:
    raise ValueError(f"array not found: {name}")
  idx = m.end()
  while src[idx].isspace():
    idx += 1
  val, _ = parse_init(src, idx)
  return val


def chunked(lst, n):
  for i in range(0, len(lst), n):
    yield lst[i:i + n]


def fmt_pair(p):
  return f"({p[0]}, {p[1]})"


def fmt_pairs_flat(name, f0, f1, data, per_line, indent):
  out = [f"{indent}public static readonly (byte {f0}, byte {f1})[] {name} ="]
  out.append(f"{indent}[")
  for c in chunked(data, per_line):
    out.append(indent + "  " + ", ".join(fmt_pair(p) for p in c) + ",")
  out.append(f"{indent}];")
  return "\n".join(out)


def fmt_pairs_jagged(name, f0, f1, data, per_line, indent):
  out = [f"{indent}public static readonly (byte {f0}, byte {f1})[][] {name} ="]
  out.append(f"{indent}[")
  for sub in data:
    if len(sub) <= per_line:
      out.append(f"{indent}  [{', '.join(fmt_pair(p) for p in sub)}],")
    else:
      out.append(f"{indent}  [")
      for c in chunked(sub, per_line):
        out.append(indent + "    " + ", ".join(fmt_pair(p) for p in c) + ",")
      out.append(f"{indent}  ],")
  out.append(f"{indent}];")
  return "\n".join(out)


def fmt_pairs_3d(name, f0, f1, data, per_line, indent):
  out = [f"{indent}public static readonly (byte {f0}, byte {f1})[][][] {name} ="]
  out.append(f"{indent}[")
  for layer in data:
    out.append(f"{indent}  [")
    for sub in layer:
      if len(sub) <= per_line:
        out.append(f"{indent}    [{', '.join(fmt_pair(p) for p in sub)}],")
      else:
        out.append(f"{indent}    [")
        for c in chunked(sub, per_line):
          out.append(indent + "      " + ", ".join(fmt_pair(p) for p in c) + ",")
        out.append(f"{indent}    ],")
    out.append(f"{indent}  ],")
  out.append(f"{indent}];")
  return "\n".join(out)


def fmt_byte_array(name, data, per_line, indent):
  out = [f"{indent}public static readonly byte[] {name} ="]
  out.append(f"{indent}[")
  for c in chunked(data, per_line):
    out.append(indent + "  " + ", ".join(str(x) for x in c) + ",")
  out.append(f"{indent}];")
  return "\n".join(out)


def fmt_byte_jagged_inline(name, data, indent):
  out = [f"{indent}public static readonly byte[][] {name} ="]
  out.append(f"{indent}[")
  for sub in data:
    out.append(f"{indent}  [{', '.join(str(x) for x in sub)}],")
  out.append(f"{indent}];")
  return "\n".join(out)


def fmt_sbyte_jagged(name, data, per_line, indent, headers=None):
  out = [f"{indent}public static readonly sbyte[][] {name} ="]
  out.append(f"{indent}[")
  for i, sub in enumerate(data):
    if headers and i < len(headers):
      out.append(f"{indent}  // {headers[i]}")
    out.append(f"{indent}  [")
    for c in chunked(sub, per_line):
      row = ", ".join(f"{x:>4}" for x in c)
      out.append(indent + "    " + row + ",")
    out.append(f"{indent}  ],")
  out.append(f"{indent}];")
  return "\n".join(out)


def fmt_byte_2d(name, data, indent):
  rows, cols = len(data), len(data[0])
  out = [f"{indent}public static readonly byte[,] {name} = new byte[{rows}, {cols}]"]
  out.append(f"{indent}{{")
  for row in data:
    out.append(indent + "  { " + ", ".join(f"{x:>3}" for x in row) + " },")
  out.append(f"{indent}}};")
  return "\n".join(out)


def fmt_flat(name, ctype, data, per_line, indent):
  out = [f"{indent}public static readonly {ctype}[] {name} =", f"{indent}["]
  for c in chunked(data, per_line):
    out.append(indent + "  " + ", ".join(str(x) for x in c) + ",")
  out.append(f"{indent}];")
  return "\n".join(out)


def fmt_jagged(name, ctype, data, per_line, indent):
  out = [f"{indent}public static readonly {ctype}[][] {name} =", f"{indent}["]
  for sub in data:
    if len(sub) <= per_line:
      out.append(f"{indent}  [{', '.join(str(x) for x in sub)}],")
    else:
      out.append(f"{indent}  [")
      for c in chunked(sub, per_line):
        out.append(indent + "    " + ", ".join(str(x) for x in c) + ",")
      out.append(f"{indent}  ],")
  out.append(f"{indent}];")
  return "\n".join(out)


def windows(src, names):
  """Where each row of a directional 4x4 predictor starts in that mode's list.

  Derived from cisco/openh264, BSD-2-Clause: the WelsI4x4LumaPred* functions in
  codec/decoder/core/src/get_intra_predictor.cpp (PRED_URL below). Each builds a
  short list of filtered reference samples and stores four overlapping windows out
  of it, one per row. The offsets are the whole difference between the modes and
  are only stated as the argument of each store, so they are read back out of those
  stores rather than copied by hand.
  """
  out = {}
  for name in names:
    at = src.index(f"void {name} (")
    body = src[at:src.index("\n}", at)]
    found = re.findall(r"ST32A4\s*\([^,]*,\s*LD32\s*\(\s*kuiList\s*(?:\+\s*(\d+))?\s*\)", body)
    if len(found) != 4:
      raise ValueError(f"{name}: expected 4 row stores, found {len(found)}")
    out[name] = [int(o) if o else 0 for o in found]
  return out


def inverse4(p):
  """Transcribed by hand from IdctResAddPred_c, cisco/openh264, BSD-2-Clause:
  codec/decoder/core/src/decode_mb_aux.cpp (IDCT_URL below). Its row pass, with
  the arithmetic shifts read as exact division. Not parsed from the source, so it
  is the one thing in this file a reader must check against upstream themselves.
  """
  t0 = p[0] + p[2]
  t1 = p[0] - p[2]
  t2 = p[1] / 2 - p[3]
  t3 = p[1] + p[3] / 2
  return [t0 + t3, t1 + t2, t1 - t2, t0 - t3]


def inverse8(p):
  """Transcribed by hand from IdctResAddPred8x8_c, cisco/openh264, BSD-2-Clause:
  codec/decoder/core/src/decode_mb_aux.cpp (IDCT_URL below). Its horizontal pass,
  with the arithmetic shifts read as exact division. Not parsed from the source, so
  it is the one thing in this file a reader must check against upstream themselves.
  """
  a = [p[0] + p[4], p[0] - p[4], p[6] - p[2] / 2, p[2] + p[6] / 2]
  b = [None] * 8
  b[0] = a[0] + a[3]
  b[2] = a[1] - a[2]
  b[4] = a[1] + a[2]
  b[6] = a[0] - a[3]

  a = [
    -p[3] + p[5] - p[7] - p[7] / 2,
    p[1] + p[7] - p[3] - p[3] / 2,
    -p[1] + p[7] + p[5] + p[5] / 2,
    p[3] + p[5] + p[1] + p[1] / 2,
  ]
  b[1] = a[0] + a[3] / 4
  b[3] = a[1] + a[2] / 4
  b[5] = a[2] - a[1] / 4
  b[7] = a[3] - a[0] / 4

  return [
    b[0] + b[7], b[2] - b[5], b[4] + b[3], b[6] + b[1],
    b[6] - b[1], b[4] - b[3], b[2] + b[5], b[0] - b[7],
  ]


def basis(inverse, size):
  """Row k is what coefficient k alone contributes to each output sample - the
  transform's impulse response, taken from the transform itself rather than copied
  from anywhere, so the two cannot disagree.

  The shifts are read as exact division. Upstream they are arithmetic shifts, which
  floor rather than divide, so a basis row is the transform without its rounding;
  the decoder rounds once at the end of its own accumulation instead.
  """
  rows = [inverse([Fraction(int(i == k)) for i in range(size)]) for k in range(size)]

  scale = 1
  for row in rows:
    for value in row:
      scale = scale * value.denominator // math.gcd(scale, value.denominator)

  return [[int(value * scale) for value in row] for row in rows], scale


def extract_license(path):
  """Pull the leading /* ... */ comment block from `path` verbatim and convert
  it to C# // line comments. Wording is taken byte-for-byte from upstream."""
  text = path.read_text()
  m = re.search(r"/\*.*?\*/", text, re.S)
  if not m:
    raise SystemExit(f"no leading comment block in {path}")
  inner = m.group(0)[2:-2]
  if inner.startswith("!"):
    inner = inner[1:]
  out = []
  for line in inner.split("\n"):
    m2 = re.match(r"\s*\*+\s?(.*)", line)
    content = (m2.group(1) if m2 else line).rstrip()
    out.append("//" if content == "" else "// " + content)
  while out and out[0] == "//":
    out.pop(0)
  while out and out[-1] == "//":
    out.pop()
  return "\n".join(out)


def main():
  ensure_sources()

  cavlc_src = strip_comments(CAVLC_SRC.read_text())
  cabac_src = strip_comments(CABAC_SRC.read_text())
  basis_src = strip_comments(BASIS_SRC.read_text())
  syntax_src = strip_comments(SYNTAX_SRC.read_text())

  cv = {
    "chroma": extract(cavlc_src, "g_kuiVlcChromaTable"),
    "prim0": extract(cavlc_src, "g_kuiVlcTable_0"),
    "prim1": extract(cavlc_src, "g_kuiVlcTable_1"),
    "prim2": extract(cavlc_src, "g_kuiVlcTable_2"),
    "fixed": extract(cavlc_src, "g_kuiVlcTable_3"),
    "sub0": [extract(cavlc_src, f"g_kuiVlcTable_0_{i}") for i in range(4)],
    "sub1": [extract(cavlc_src, f"g_kuiVlcTable_1_{i}") for i in range(4)],
    "sub2": [extract(cavlc_src, f"g_kuiVlcTable_2_{i}") for i in range(8)],
    "thresh": extract(cavlc_src, "g_kuiVlcTableNeedMoreBitsThread"),
    "mbcount": [extract(cavlc_src, f"g_kuiVlcTableMoreBitsCount{i}") for i in range(3)],
    "ncmap": extract(cavlc_src, "g_kuiNcMapTable"),
    "symmap": extract(cavlc_src, "g_kuiVlcTrailingOneTotalCoeffTable"),
    "tz4": [extract(cavlc_src, f"g_kuiTotalZerosTable{i}") for i in range(15)],
    "tz4bw": extract(cavlc_src, "g_kuiTotalZerosBitNumMap"),
    "tzc": [extract(cavlc_src, f"g_kuiTotalZerosChromaTable{i}") for i in range(3)],
    "tzcbw": extract(cavlc_src, "g_kuiTotalZerosBitNumChromaMap"),
    "rb": [extract(cavlc_src, f"g_kuiZeroLeftTable{i}") for i in range(7)],
    "rbbw_full": extract(cavlc_src, "g_kuiZeroLeftBitNumMap"),
    "cbpintra": extract(cavlc_src, "g_kuiIntra4x4CbpTable"),
    "cbpinter": extract(cavlc_src, "g_kuiInterCbpTable"),
  }

  assert len(cv["cbpintra"]) == 48, "intra coded block pattern mapping"
  assert len(cv["cbpinter"]) == 48, "inter coded block pattern mapping"

  range_lps = extract(cabac_src, "g_kuiCabacRangeLps")
  state_trans = extract(cabac_src, "g_kuiStateTransTable")
  ctx_init = extract(cabac_src, "g_kiCabacGlobalContextIdx")

  rs = {
    "lumadc": extract(cavlc_src, "g_kuiLumaDcZigzagScan"),
    "chromadc": extract(cavlc_src, "g_kuiChromaDcScan"),
    "zigzag4": extract(basis_src, "g_kuiZigzagScan"),
    "zigzag8": extract(basis_src, "g_kuiZigzagScan8x8"),
    "sig8": extract(basis_src, "g_kuiIdx2CtxSignificantCoeffFlag8x8"),
    "last8": extract(basis_src, "g_kuiIdx2CtxLastSignificantCoeffFlag8x8"),
    "cbf": extract(syntax_src, "g_kBlockCat2CtxOffsetCBF"),
    "map": extract(syntax_src, "g_kBlockCat2CtxOffsetMap"),
    "last": extract(syntax_src, "g_kBlockCat2CtxOffsetLast"),
    "one": extract(syntax_src, "g_kBlockCat2CtxOffsetOne"),
    "abs": extract(syntax_src, "g_kBlockCat2CtxOffsetAbs"),
    "maxpos": extract(syntax_src, "g_kMaxPos"),
    "maxc2": extract(syntax_src, "g_kMaxC2"),
    "chromaqp": extract(cabac_src, "g_kuiChromaQpTable"),
    "dequant4": extract(cabac_src, "g_kuiDequantCoeff"),
    "dequant8": extract(cabac_src, "g_kuiDequantCoeff8x8"),
    "scaling4": extract(cabac_src, "g_kuiDequantScaling4x4Default"),
    "scaling8": extract(cabac_src, "g_kuiDequantScaling8x8Default"),
  }

  basis4, scale4 = basis(inverse4, 4)
  basis8, scale8 = basis(inverse8, 8)

  pred_src = strip_comments(PRED_SRC.read_text())
  win = windows(pred_src, [
    "WelsI4x4LumaPredDDL_c", "WelsI4x4LumaPredDDLTop_c",
    "WelsI4x4LumaPredDDR_c",
    "WelsI4x4LumaPredVR_c",
    "WelsI4x4LumaPredHD_c",
    "WelsI4x4LumaPredVL_c", "WelsI4x4LumaPredVLTop_c",
    "WelsI4x4LumaPredHU_c",
  ])

  # Vertical is the window that never moves, and horizontal and DC are not built from a list at
  # all, so their rows stay zero and the predictor handles them apart.
  rs["windows"] = [
    [0, 0, 0, 0],
    [0, 0, 0, 0],
    [0, 0, 0, 0],
    win["WelsI4x4LumaPredDDL_c"],
    win["WelsI4x4LumaPredDDR_c"],
    win["WelsI4x4LumaPredVR_c"],
    win["WelsI4x4LumaPredHD_c"],
    win["WelsI4x4LumaPredVL_c"],
    win["WelsI4x4LumaPredHU_c"],
  ]

  assert len(cv["chroma"]) == 256 and all(len(p) == 2 for p in cv["chroma"])
  assert len(cv["prim0"]) == 256
  assert len(cv["prim1"]) == 256
  assert len(cv["prim2"]) == 256
  assert len(cv["fixed"]) == 64
  assert [len(t) for t in cv["sub0"]] == [256, 4, 2, 2]
  assert [len(t) for t in cv["sub1"]] == [64, 8, 2, 2]
  assert [len(t) for t in cv["sub2"]] == [4, 4, 4, 4, 2, 2, 2, 2]
  assert cv["thresh"] == [4, 4, 8]
  assert [len(c) for c in cv["mbcount"]] == [4, 4, 8]
  assert len(cv["ncmap"]) == 17
  assert len(cv["symmap"]) == 62
  assert [len(t) for t in cv["tz4"]] == [512, 64, 64, 32, 32, 64, 64, 64, 64, 32, 16, 16, 8, 4, 2]
  assert len(cv["tz4bw"]) == 15
  assert [len(t) for t in cv["tzc"]] == [8, 4, 2]
  assert len(cv["tzcbw"]) == 3
  assert [len(t) for t in cv["rb"]] == [2, 4, 4, 8, 8, 8, 8]

  assert len(range_lps) == 64 and all(len(r) == 4 for r in range_lps)
  assert len(state_trans) == 64 and all(len(r) == 2 for r in state_trans)
  assert len(ctx_init) == 460 and all(len(r) == 4 and all(len(p) == 2 for p in r) for r in ctx_init)

  assert range_lps[0] == [128, 176, 208, 240], "RangeTabLPS[0] mismatch"
  assert range_lps[63] == [2, 2, 2, 2], "RangeTabLPS[63] mismatch"
  assert state_trans[0] == [0, 1] and state_trans[63] == [63, 63]
  assert ctx_init[0] == [[20, -15], [20, -15], [20, -15], [20, -15]], "ctx 0 m,n mismatch"
  assert ctx_init[64][0] == [-9, 83], "ctx 64 initSet 0 m,n mismatch"
  assert ctx_init[459][0] == [14, 67], "ctx 459 initSet 0 m,n mismatch"

  trans_lps = [r[0] for r in state_trans]
  trans_mps = [r[1] for r in state_trans]
  init_m = [[ctx_init[i][s][0] for i in range(460)] for s in range(4)]
  init_n = [[ctx_init[i][s][1] for i in range(460)] for s in range(4)]

  assert init_m[0][0] == 20 and init_n[0][0] == -15
  assert init_m[0][64] == -9 and init_n[0][64] == 83
  assert init_m[3][459] == 20 and init_n[3][459] == 64

  # Upstream states both direct-term scans as byte offsets into a buffer holding sixteen
  # coefficients per block, so dividing by that says which block each one belongs to.
  assert all(v % 16 == 0 for v in rs["lumadc"]), "luma direct scan is not in whole blocks"
  assert all(v % 16 == 0 for v in rs["chromadc"]), "chroma direct scan is not in whole blocks"
  rs["lumadc"] = [v // 16 for v in rs["lumadc"]]
  rs["chromadc"] = [v // 16 for v in rs["chromadc"]]
  assert sorted(rs["lumadc"]) == list(range(16)), "luma direct scan is not a permutation"
  assert rs["chromadc"] == [0, 1, 2, 3], "chroma direct scan is no longer the identity"

  assert sorted(rs["zigzag4"]) == list(range(16)), "4x4 zig-zag is not a permutation"
  assert sorted(rs["zigzag8"]) == list(range(64)), "8x8 zig-zag is not a permutation"
  assert rs["zigzag4"][:3] == [0, 1, 4] and rs["zigzag8"][:3] == [0, 1, 8]
  assert len(rs["sig8"]) == 64 and max(rs["sig8"]) == 14, "8x8 significance context map"
  assert len(rs["last8"]) == 64 and max(rs["last8"]) == 8, "8x8 last context map"

  # Index 0 is the IDX_UNUSED placeholder upstream; categories run 1..10.
  for key, head in (("cbf", 0), ("map", 0), ("last", 0), ("one", 0), ("abs", 0)):
    assert len(rs[key]) == 11 and rs[key][1] == head
  assert rs["cbf"][1:6] == [0, 4, 8, 12, 16]
  assert rs["map"][1:6] == [0, 15, 29, 44, 47]
  assert rs["one"][1:6] == [0, 10, 20, 30, 39]
  assert rs["maxpos"][1:7] == [15, 14, 15, 3, 14, 63], "coefficients per block category"
  assert rs["maxc2"][1:7] == [4, 4, 4, 3, 4, 4]

  # Chroma follows luma exactly up to the point where it starts being quantised more finely, and
  # the table is only interesting past there - a plain clamp is right below it and wrong above.
  assert len(rs["chromaqp"]) == 52
  assert rs["chromaqp"][:30] == list(range(30)), "chroma QP diverges from luma too early"
  assert rs["chromaqp"][51] == 39 and rs["chromaqp"][30] == 29

  assert len(rs["dequant4"]) == 52 and all(len(r) == 8 for r in rs["dequant4"])
  assert len(rs["dequant8"]) == 52 and all(len(r) == 64 for r in rs["dequant8"])
  # The first column across the first six rows is the flat-matrix normAdjust.
  assert [rs["dequant4"][q][0] for q in range(6)] == [10, 11, 13, 14, 16, 18]
  assert len(rs["scaling4"]) == 2 and all(len(r) == 16 for r in rs["scaling4"])
  assert len(rs["scaling8"]) == 2 and all(len(r) == 64 for r in rs["scaling8"])

  # A basis row is one coefficient's contribution, so row 0 is the flat one every
  # block shares and the rest must cancel over the block - which is what makes a
  # reduced cell the DC term alone.
  # Losing the above-right neighbour changes how a list is built but not where its rows start, so
  # the variant sharing its base's offsets is what says the table needs only one row per mode.
  assert win["WelsI4x4LumaPredDDLTop_c"] == win["WelsI4x4LumaPredDDL_c"]
  assert win["WelsI4x4LumaPredVLTop_c"] == win["WelsI4x4LumaPredVL_c"]
  assert all(max(r) + 3 < 10 for r in rs["windows"]), "a row window runs past its list"

  assert scale4 == 2 and basis4[0] == [2, 2, 2, 2]
  assert all(sum(row) == 0 for row in basis4[1:]), "4x4 basis rows are not zero-mean"
  assert basis8[0] == [scale8] * 8
  assert all(sum(row) == 0 for row in basis8[1:]), "8x8 basis rows are not zero-mean"

  print("parsed and verified all openh264 tables", file=sys.stderr)

  cavlc_lic = extract_license(CAVLC_SRC)
  cabac_lic = extract_license(CABAC_SRC)

  resid_pieces = [
    extract_license(IDCT_SRC),
    "",
    f"// Sources: {BASIS_URL}",
    f"//          {SYNTAX_URL}",
    f"//          {CABAC_URL}",
    f"//          {IDCT_URL}",
    f"//          {PRED_URL}",
    "//",
    "// LumaDirectScan and ChromaDirectScan say which 4x4 block each direct term belongs to.",
    "// Upstream states them as byte offsets into a buffer holding sixteen coefficients per",
    "// block; they are divided by that here so they name blocks rather than addresses.",
    "//",
    "// Intra4x4Windows is where each row of a directional 4x4 predictor starts in that",
    "// mode's list, read back out of the row stores in get_intra_predictor.cpp. Vertical",
    "// is the window that never moves; horizontal and DC build no list and stay zero.",
    "//",
    "// Basis4 and Basis8 are the impulse response of decode_mb_aux.cpp's IdctResAddPred_c",
    "// and IdctResAddPred8x8_c, which state the transform as a butterfly rather than a",
    "// table. Row k is coefficient k's contribution to each output sample, scaled by",
    "// BasisScale to clear the halves the shifts introduce.",
    "",
    "namespace H264;",
    "",
    "public static class ResidualTables",
    "{",
    fmt_jagged("Intra4x4Windows", "byte", rs["windows"], 4, "  "),
    "",
    fmt_byte_array("LumaDirectScan", rs["lumadc"], 16, "  "),
    "",
    fmt_byte_array("ChromaDirectScan", rs["chromadc"], 4, "  "),
    "",
    fmt_byte_array("Zigzag4x4", rs["zigzag4"], 16, "  "),
    "",
    fmt_byte_array("Zigzag8x8", rs["zigzag8"], 16, "  "),
    "",
    fmt_byte_array("SignificantCoeffFlag8x8", rs["sig8"], 16, "  "),
    "",
    fmt_byte_array("LastSignificantCoeffFlag8x8", rs["last8"], 16, "  "),
    "",
    fmt_byte_array("CategoryOffsetCbf", rs["cbf"], 11, "  "),
    "",
    fmt_byte_array("CategoryOffsetMap", rs["map"], 11, "  "),
    "",
    fmt_byte_array("CategoryOffsetLast", rs["last"], 11, "  "),
    "",
    fmt_byte_array("CategoryOffsetOne", rs["one"], 11, "  "),
    "",
    fmt_byte_array("CategoryOffsetAbs", rs["abs"], 11, "  "),
    "",
    fmt_byte_array("CategoryMaxPosition", rs["maxpos"], 11, "  "),
    "",
    fmt_byte_array("CategoryMaxContext2", rs["maxc2"], 11, "  "),
    "",
    fmt_byte_array("ChromaQp", rs["chromaqp"], 13, "  "),
    "",
    fmt_jagged("DequantCoeff4x4", "ushort", rs["dequant4"], 8, "  "),
    "",
    fmt_jagged("DequantCoeff8x8", "ushort", rs["dequant8"], 16, "  "),
    "",
    fmt_jagged("DefaultScaling4x4", "byte", rs["scaling4"], 16, "  "),
    "",
    fmt_jagged("DefaultScaling8x8", "byte", rs["scaling8"], 16, "  "),
    "",
    f"  public const int Basis4Scale = {scale4};",
    "",
    fmt_jagged("Basis4", "short", basis4, 4, "  "),
    "",
    f"  public const int Basis8Scale = {scale8};",
    "",
    fmt_jagged("Basis8", "short", basis8, 8, "  "),
    "}",
    "",
  ]

  resid_text = "\n".join(resid_pieces)
  OUT_RESID.write_text(resid_text)
  print(f"wrote {OUT_RESID.relative_to(REPO_ROOT)} ({len(resid_text.splitlines())} lines)",
        file=sys.stderr)

  cavlc_pieces = [
    cavlc_lic,
    "",
    f"// Source: {CAVLC_URL}",
    "",
    "namespace H264;",
    "",
    "public static class CavlcTables",
    "{",
    fmt_pairs_flat("CoeffTokenChromaDc", "Symbol", "Length", cv["chroma"], 16, "  "),
    "",
    fmt_pairs_flat("CoeffTokenPrimary0", "Symbol", "Length", cv["prim0"], 16, "  "),
    "",
    fmt_pairs_flat("CoeffTokenPrimary1", "Symbol", "Length", cv["prim1"], 16, "  "),
    "",
    fmt_pairs_flat("CoeffTokenPrimary2", "Symbol", "Length", cv["prim2"], 16, "  "),
    "",
    fmt_pairs_flat("CoeffTokenFixed", "Symbol", "Length", cv["fixed"], 16, "  "),
    "",
    fmt_byte_array("CoeffTokenMoreBitsThreshold", cv["thresh"], 16, "  "),
    "",
    fmt_byte_jagged_inline("CoeffTokenMoreBitsCount", cv["mbcount"], "  "),
    "",
    fmt_pairs_3d("CoeffTokenSub", "Symbol", "Length", [cv["sub0"], cv["sub1"], cv["sub2"]], 16, "  "),
    "",
    fmt_byte_array("NcMap", cv["ncmap"], 17, "  "),
    "",
    fmt_pairs_flat("CoeffTokenSymbolMap", "TrailingOnes", "TotalCoeff", cv["symmap"], 16, "  "),
    "",
    "  public static (int TotalCoeff, int TrailingOnes) SymbolToCoeff(int symbol)",
    "  {",
    "    if ((uint)symbol >= (uint)CoeffTokenSymbolMap.Length) return (-1, 0);",
    "    var (trailing, total) = CoeffTokenSymbolMap[symbol];",
    "    return (total, trailing);",
    "  }",
    "",
    fmt_pairs_jagged("TotalZeros4x4", "Symbol", "Length", cv["tz4"], 16, "  "),
    "",
    fmt_byte_array("TotalZeros4x4BitWidths", cv["tz4bw"], 15, "  "),
    "",
    fmt_pairs_jagged("TotalZerosChromaDc", "Symbol", "Length", cv["tzc"], 16, "  "),
    "",
    fmt_byte_array("TotalZerosChromaDcBitWidths", cv["tzcbw"], 16, "  "),
    "",
    fmt_pairs_jagged("RunBefore", "Symbol", "Length", cv["rb"], 16, "  "),
    "",
    fmt_byte_array("RunBeforeBitWidths", [1, 2, 2, 3, 3, 3, 3], 7, "  "),
    "",
    fmt_byte_array("Intra4x4CbpTable", cv["cbpintra"], 16, "  "),
    "",
    fmt_byte_array("Inter4x4CbpTable", cv["cbpinter"], 16, "  "),
    "}",
    "",
  ]

  cavlc_text = "\n".join(cavlc_pieces)
  cavlc_text = cavlc_text.replace(
    "(byte Symbol, byte Length)[][] TotalZeros4x4",
    "(byte Zeros, byte Length)[][] TotalZeros4x4",
  )
  cavlc_text = cavlc_text.replace(
    "(byte Symbol, byte Length)[][] TotalZerosChromaDc",
    "(byte Zeros, byte Length)[][] TotalZerosChromaDc",
  )
  cavlc_text = cavlc_text.replace(
    "(byte Symbol, byte Length)[][] RunBefore",
    "(byte Run, byte Length)[][] RunBefore",
  )

  OUT_CAVLC.write_text(cavlc_text)
  print(f"wrote {OUT_CAVLC.relative_to(REPO_ROOT)} ({len(cavlc_text.splitlines())} lines)", file=sys.stderr)

  arith_pieces = [
    cabac_lic,
    "",
    f"// Source: {CABAC_URL}",
    "",
    "namespace H264;",
    "",
    "public static class CabacArithmeticTables",
    "{",
    fmt_byte_2d("RangeTabLps", range_lps, "  "),
    "",
    fmt_byte_array("TransIdxLps", trans_lps, 16, "  "),
    "",
    fmt_byte_array("TransIdxMps", trans_mps, 16, "  "),
    "}",
    "",
  ]
  arith_text = "\n".join(arith_pieces)
  OUT_ARITH.write_text(arith_text)
  print(f"wrote {OUT_ARITH.relative_to(REPO_ROOT)} ({len(arith_text.splitlines())} lines)", file=sys.stderr)

  cinit_pieces = [
    cabac_lic,
    "",
    f"// Source: {CABAC_URL}",
    "",
    "namespace H264;",
    "",
    "public static class CabacContextInitTables",
    "{",
    "  public const int CtxCount = 460;",
    "",
    fmt_sbyte_jagged("InitM", init_m, 16, "  "),
    "",
    fmt_sbyte_jagged("InitN", init_n, 16, "  "),
    "}",
    "",
  ]
  cinit_text = "\n".join(cinit_pieces)
  OUT_CINIT.write_text(cinit_text)
  print(f"wrote {OUT_CINIT.relative_to(REPO_ROOT)} ({len(cinit_text.splitlines())} lines)", file=sys.stderr)


if __name__ == "__main__":
  main()
