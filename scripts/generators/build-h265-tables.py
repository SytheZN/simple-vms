#!/usr/bin/env python3
"""Generate HEVC CABAC InitValue table from HM ContextTables.h.

Run from the project root:

    ./scripts/generators/build-h265-tables.py

Downloads HM ContextTables.h into the script's own directory (gitignored),
parses every per-syntax-element INIT_* array, and writes one C# file:

  src/plugins/shared/H265/CabacContextInitTables.cs

HEVC reuses the H264 project's CabacArithmeticTables.cs for the state-machine tables
(RangeTabLps / TransIdxLps / TransIdxMps); only the per-context init
values differ from H.264.

Layout: the HEVC analyzer walker addresses contexts via a flat global
ctxIdx layout (slot 0..148, with two unused gaps). Per-syntax-element
HM arrays are placed into a flat byte[3][CtxCount] at the slot offsets
the walker uses. HM rows are [B, P, I]; the initType ordering the walker
expects is [I, P-default, B-default], so output rows = HM[2, 1, 0].
"""

import re
import subprocess
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parent.parent

HM_SRC = SCRIPT_DIR / "ContextTables.h"
ROM_SRC = SCRIPT_DIR / "TComRom.cpp"
PRED_SRC = SCRIPT_DIR / "TComPrediction.cpp"
TRQUANT_SRC = SCRIPT_DIR / "TComTrQuant.cpp"
ROM_HDR = SCRIPT_DIR / "TComRom.h"
OUT_CINIT = REPO_ROOT / "src/plugins/shared/H265/CabacContextInitTables.cs"
OUT_ROM = REPO_ROOT / "src/plugins/shared/H265/ResidualTables.cs"

# Tables the residual parser needs to select contexts and reconstruct the last-significant
# position. Reconstruction-only constants (chroma QP mapping) are approximated instead.
ROM_TABLES = [
  ("SigCtxMap4x4", "ctxIndMap4x4"),
  ("MinInGroup", "g_uiMinInGroup"),
  ("GroupIdx", "g_uiGroupIdx"),
]

PREDICTION_TABLES = [
  # Indexed by the distance of the mode from horizontal or vertical, whichever it is nearer.
  ("IntraPredAngle", "angTable", 9),
  ("IntraInvAngle", "invAngTable", 9),
  # [channel][log2(size) - 2]: how far from horizontal or vertical a mode must be to be smoothed.
  ("IntraFilterThreshold", "m_aucIntraFilter", 5),
]

# 4x4 intra luma uses DST-VII; every other transform block uses DCT-II at its own size.
TRANSFORM_MATRICES = [
  ("Dst4", "g_as_DST_MAT_4", "DEFINE_DST4x4_MATRIX", 4),
  ("Dct4", "g_aiT4", "DEFINE_DCT4x4_MATRIX", 4),
  ("Dct8", "g_aiT8", "DEFINE_DCT8x8_MATRIX", 8),
  ("Dct16", "g_aiT16", "DEFINE_DCT16x16_MATRIX", 16),
  ("Dct32", "g_aiT32", "DEFINE_DCT32x32_MATRIX", 32),
]

HM_BASE = "https://vcgit.hhi.fraunhofer.de/jvet/HM/-/raw/master/source/Lib/TLibCommon"
HM_URL = f"{HM_BASE}/ContextTables.h"
ROM_URL = f"{HM_BASE}/TComRom.cpp"
TRQUANT_URL = f"{HM_BASE}/TComTrQuant.cpp"

PRED_URL = f"{HM_BASE}/TComPrediction.cpp"

SOURCES = [
  (HM_SRC, HM_URL),
  (ROM_SRC, ROM_URL),
  (TRQUANT_SRC, TRQUANT_URL),
  (ROM_HDR, f"{HM_BASE}/TComRom.h"),
  (PRED_SRC, PRED_URL),
]

CNU = 154
CTX_COUNT = 256

# Walker slot allocation -> source.
LAYOUT = [
  ("SaoMergeFlag",            0,  1, ("array", "INIT_SAO_MERGE_FLAG", 0)),
  ("SaoTypeIdx",              1,  1, ("array", "INIT_SAO_TYPE_IDX", 0)),
  ("SplitCu",                 2,  3, ("array", "INIT_SPLIT_FLAG", 0)),
  ("CuTransquantBypass",      5,  1, ("array", "INIT_CU_TRANSQUANT_BYPASS_FLAG", 0)),
  ("CuSkipFlag",              6,  3, ("array", "INIT_SKIP_FLAG", 0)),
  ("PaletteModeFlag",         9,  1, ("zero",)),
  ("PredModeFlag",           10,  1, ("array", "INIT_PRED_MODE", 0)),
  ("PartMode",               11,  4, ("array", "INIT_PART_SIZE", 0)),
  ("PrevIntraLumaPred",      15,  1, ("array", "INIT_INTRA_PRED_MODE", 0)),
  ("IntraChromaPredMode",    16,  1, ("array", "INIT_CHROMA_PRED_MODE", 0)),
  ("RqtRootCbf",             17,  1, ("array", "INIT_QT_ROOT_CBF", 0)),
  ("SigCoeffFlagLuma",       18, 27, ("array", "INIT_SIG_FLAG", 0)),
  ("SigCoeffFlagChroma",     45, 15, ("array", "INIT_SIG_FLAG", 28)),
  ("LastSigCoeffXPrefix",    60, 18, ("array", "INIT_LAST", 0)),
  ("LastSigCoeffYPrefix",    78, 18, ("array", "INIT_LAST", 0)),
  ("CoeffAbsLevelGreater1",  96, 24, ("array", "INIT_ONE_FLAG", 0)),
  ("CoeffAbsLevelGreater2", 120,  6, ("array", "INIT_ABS_FLAG", 0)),
  ("SplitTransformFlag",    126,  3, ("array", "INIT_TRANS_SUBDIV_FLAG", 0)),
  ("CbfLuma",               129,  2, ("array", "INIT_QT_CBF", 0)),
  ("CbfCbCr",               131,  4, ("array", "INIT_QT_CBF", 5)),
  ("TransformSkipFlagLuma", 135,  1, ("array", "INIT_TRANSFORMSKIP_FLAG", 0)),
  ("TransformSkipFlagChroma", 136, 1, ("array", "INIT_TRANSFORMSKIP_FLAG", 1)),
  ("MergeFlag",             137,  1, ("array", "INIT_MERGE_FLAG_EXT", 0)),
  ("MergeIdx",              138,  1, ("array", "INIT_MERGE_IDX_EXT", 0)),
  ("InterPredIdc",          139,  5, ("array", "INIT_INTER_DIR", 0)),
  ("RefIdx",                144,  2, ("array", "INIT_REF_PIC", 0)),
  ("AbsMvdGreater0",        146,  1, ("array", "INIT_MVD", 0)),
  ("AbsMvdGreater1",        147,  1, ("array", "INIT_MVD", 1)),
  ("MvpFlag",               148,  1, ("array", "INIT_MVP_IDX", 0)),
  ("NoResidualDataFlag",    149,  1, ("zero",)),
  ("CuQpDeltaAbs",          150,  2, ("array", "INIT_DQP", 0)),
  ("CodedSubBlockFlag",     152,  4, ("array", "INIT_SIG_CG_FLAG", 0)),
]


def ensure_sources():
  for path, url in SOURCES:
    if not path.exists():
      print(f"fetching {url}", file=sys.stderr)
      subprocess.run(["wget", "-q", "-O", str(path), url], check=True)


def strip_comments(s):
  s = re.sub(r"/\*.*?\*/", "", s, flags=re.S)
  s = re.sub(r"//[^\n]*", "", s)
  return s


def expand_macros(src):
  macros = {}
  for m in re.finditer(r"^\s*#define\s+([A-Z_][A-Z0-9_]*)\s+([^\n]+)$", src, flags=re.M):
    name, body = m.group(1), m.group(2).strip().rstrip(",")
    if "," in body or body.replace(" ", "").isdigit() or body == "CNU":
      macros[name] = body
  expanded = src
  for _ in range(4):
    new = expanded
    for name, body in macros.items():
      new = re.sub(rf"\b{name}\b", body, new)
    if new == expanded:
      break
    expanded = new
  return expanded


def extract_array(src, name):
  pat = re.compile(
    rf"static\s+const\s+UChar\s+{name}\s*\[[^\]]*\]\s*\[[^\]]*\]\s*=\s*\{{(.*?)\}}\s*;",
    re.S,
  )
  m = pat.search(src)
  if not m:
    raise SystemExit(f"array not found: {name}")
  body = m.group(1)

  rows = []
  depth = 0
  buf = ""
  for ch in body:
    if ch == "{":
      depth += 1
      if depth == 1:
        buf = ""
      continue
    if ch == "}":
      depth -= 1
      if depth == 0:
        toks = [t.strip() for t in buf.split(",") if t.strip()]
        rows.append([int(t) if t != "CNU" else CNU for t in toks])
      continue
    if depth >= 1:
      buf += ch

  if len(rows) != 3:
    raise SystemExit(f"{name}: expected 3 rows, got {len(rows)}")
  return rows


def assert_spot_checks(arrays):
  assert arrays["INIT_SPLIT_FLAG"][2] == [139, 141, 157]
  assert arrays["INIT_SKIP_FLAG"][2] == [CNU, CNU, CNU]
  assert arrays["INIT_MERGE_FLAG_EXT"][0] == [154]
  assert arrays["INIT_MERGE_FLAG_EXT"][1] == [110]
  assert arrays["INIT_MERGE_FLAG_EXT"][2] == [CNU]
  assert arrays["INIT_QT_ROOT_CBF"][0] == [79]
  assert arrays["INIT_QT_ROOT_CBF"][2] == [CNU]
  assert arrays["INIT_SAO_MERGE_FLAG"] == [[153], [153], [153]]
  print("spot-checks ok", file=sys.stderr)


def assert_layout_is_contiguous():
  expected = 0
  for name, start, count, _ in LAYOUT:
    if start != expected:
      raise SystemExit(
        f"{name} starts at {start}, expected {expected}: slots overlap or leave a hole")
    expected = start + count
  print(f"layout ok, {expected} contexts", file=sys.stderr)


def build_table(arrays):
  hm_to_spec = [2, 1, 0]
  out = [[CNU] * CTX_COUNT for _ in range(3)]

  for name, start, count, spec in LAYOUT:
    for spec_init_type in range(3):
      hm_row = hm_to_spec[spec_init_type]
      if spec[0] == "zero":
        values = [CNU] * count
      else:
        _, arr_name, slice_off = spec
        arr = arrays[arr_name][hm_row]
        if slice_off + count > len(arr):
          raise SystemExit(
            f"{name} reads {arr_name}[{slice_off}:{slice_off + count}] of {len(arr)} entries")
        values = arr[slice_off:slice_off + count]
      for i, v in enumerate(values):
        out[spec_init_type][start + i] = v

  return out


def fmt_row(row):
  chunks = []
  for i in range(0, CTX_COUNT, 16):
    chunks.append(", ".join(f"{v:3d}" for v in row[i:i + 16]))
  return ",\n      ".join(chunks)


def extract_license(path):
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

  src = strip_comments(HM_SRC.read_text())
  src = expand_macros(src)

  array_names = sorted({spec[1] for _, _, _, spec in LAYOUT if spec[0] == "array"})
  arrays = {name: extract_array(src, name) for name in array_names}
  assert_spot_checks(arrays)
  assert_layout_is_contiguous()

  table = build_table(arrays)

  rows_text = []
  for i in range(3):
    rows_text.append(
      f"    [\n"
      f"      {fmt_row(table[i])}\n"
      f"    ]"
    )
  init_value_block = ",\n".join(rows_text)

  pieces = [
    extract_license(HM_SRC),
    "",
    f"// Source: {HM_URL}",
    "",
    "namespace H265;",
    "",
    "public static class CabacContextInitTables",
    "{",
    "  public const int CtxCount = 256;",
    "  public const int InitTypeCount = 3;",
    "",
    "  public static readonly byte[][] InitValue =",
    "  [",
    init_value_block,
    "  ];",
    "}",
    "",
  ]

  text = "\n".join(pieces)
  OUT_CINIT.write_text(text)
  print(f"wrote {OUT_CINIT.relative_to(REPO_ROOT)} ({len(text.splitlines())} lines)", file=sys.stderr)

  write_rom_tables()


def extract_flat_array(src, name):
  match = re.search(
    r"\b" + re.escape(name) + r"(?:\s*\[[^\]]*\])+\s*=\s*\{(.*?)\}\s*;", src, flags=re.S)
  if not match:
    raise SystemExit(f"table {name} not found in {ROM_SRC.name}")
  return [int(t) for t in re.findall(r"-?\d+", match.group(1))]


def fmt_short(values, per_line=16):
  lines = []
  for start in range(0, len(values), per_line):
    chunk = values[start:start + per_line]
    lines.append("    " + ", ".join(f"{v:3d}" for v in chunk) + ",")
  return "\n".join(lines)


def extract_matrix(src, macro, size):
  """Expands one of HM's DEFINE_*_MATRIX macros for the 8-bit coefficients.

  The result keeps HM's own [frequency][position] layout so the emitted table can be compared
  against the source directly. Each macro is invoked twice, once with the extended-precision
  coefficients and once with the 8-bit ones; the latter is the invocation whose arguments are
  small.
  """
  body = re.search(rf"#define\s+{macro}\(([^)]*)\)(.*?)\n\n", src, flags=re.S)
  if not body:
    raise SystemExit(f"{macro} not found in {ROM_SRC.name}")

  params = [p.strip() for p in body.group(1).split(",")]
  rows = re.findall(r"\{([^{}]*)\}", body.group(2))
  if len(rows) != size:
    raise SystemExit(f"{macro}: expected {size} rows, found {len(rows)}")

  values = None
  for call in re.finditer(rf"{macro}\s*\(([^)]*)\)", src):
    args = [a.strip() for a in call.group(1).split(",")]
    if len(args) == len(params) and all(a.isdigit() for a in args) and int(args[0]) < 256:
      values = {name: int(a) for name, a in zip(params, args)}
      break
  if values is None:
    raise SystemExit(f"no 8-bit {macro} invocation found")

  forward = []
  for row in rows:
    forward.append([
      0 if t == "0" else (-values[t[1:]] if t.startswith("-") else values[t])
      for t in (t.strip() for t in row.split(",")) if t])

  return [value for row in forward for value in row]


def write_rom_tables():
  src = strip_comments(ROM_SRC.read_text())

  members = []
  for csharp_name, hm_name in ROM_TABLES:
    values = extract_flat_array(src, hm_name)
    members.append(
      f"  /// <summary>HM {hm_name}.</summary>\n"
      f"  public static readonly byte[] {csharp_name} =\n"
      f"  [\n"
      f"{fmt_short(values)}\n"
      f"  ];")

  prediction = strip_comments(PRED_SRC.read_text())
  for csharp_name, hm_name, per_line in PREDICTION_TABLES:
    values = extract_flat_array(prediction, hm_name)
    members.append(
      f"  /// <summary>HM {hm_name}.</summary>\n"
      f"  public static readonly short[] {csharp_name} =\n"
      f"  [\n"
      f"{fmt_short(values, per_line=per_line)}\n"
      f"  ];")

  for name, hm_name, macro, size in TRANSFORM_MATRICES:
    members.append(
      f"  /// <summary>HM {hm_name} at 8-bit precision, [frequency * {size} + position].</summary>\n"
      f"  public static readonly short[] {name} =\n"
      f"  [\n"
      f"{fmt_short(extract_matrix(src, macro, size), per_line=size if size <= 16 else 16)}\n"
      f"  ];")

  pieces = [
    extract_license(ROM_SRC),
    "",
    f"// Source: {ROM_URL}",
    "",
    "namespace H265;",
    "",
    "public static class ResidualTables",
    "{",
    "\n\n".join(members),
    "}",
    "",
  ]

  text = "\n".join(pieces)
  OUT_ROM.write_text(text)
  print(f"wrote {OUT_ROM.relative_to(REPO_ROOT)} ({len(text.splitlines())} lines)", file=sys.stderr)


if __name__ == "__main__":
  main()
