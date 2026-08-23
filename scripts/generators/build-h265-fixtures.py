#!/usr/bin/env python3
"""Generate small all-intra HEVC fixtures for the thumbnail decoder tests.

Run from the project root:

    ./scripts/generators/build-h265-fixtures.py

Each fixture enables one coding tool on top of a baseline that has them all off, so a
decoder failure names the tool the fixture introduces. Alongside every stream it writes a
reference decode of the same frame at the decoder's output scale, as raw 8-bit planes.

Requires ffmpeg with libx265. The streams are synthetic, so unlike a camera capture they
can be committed.
"""

import subprocess
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parent.parent
# Its own directory: several fixture names are shared with the AVC set, and the reference
# planes carry no codec in their name.
OUT_DIR = REPO_ROOT / "tests/Tests.Unit/Thumbnail/fixtures/h265"

SCALE = 8

BASELINE = {
  "keyint": "1",
  "info": "0",
  # Wavefronts are on by default and split the slice into per-row substreams with their own CABAC
  # state, which the decoder does not implement and a camera does not use.
  "wpp": "0",
  "rect": "0",
  "amp": "0",
  "aq-mode": "0",
  "sao": "0",
  "tskip": "0",
  "signhide": "0",
  "strong-intra-smoothing": "0",
  "tu-intra-depth": "1",
  "qp": "30",
}

# Name -> (source, size, settings overriding the baseline). A setting of None is removed, which
# matters for qp: adaptive quantisation only engages once rate control is not fixed-QP, and it is
# what makes the encoder signal cu_qp_delta at all.
FIXTURES = [
  ("plain", "testsrc2", (256, 128), {}),
  # Deblocking is the only stage of a conformant decode the thumbnail decoder skips, and it runs
  # after reconstruction rather than feeding it. Turning it off leaves nothing between this stream
  # and an exact match, so the tests can hold this one to a far tighter tolerance than the rest.
  ("nofilter", "testsrc2", (256, 128), {"no-deblock": "1"}),
  ("sao", "testsrc2", (256, 128), {"sao": "1"}),
  ("cuqpdelta", "testsrc2", (256, 128), {"qp": None, "crf": "28", "aq-mode": "1"}),
  ("transformskip", "testsrc2", (256, 128), {"tskip": "1"}),
  ("signdatahiding", "testsrc2", (256, 128), {"signhide": "1"}),
  ("transformdepth", "testsrc2", (256, 128), {"tu-intra-depth": "3"}),
  # 200x136 leaves a partial CTB at both edges, so the quadtree has to infer splits there.
  ("boundary", "testsrc2", (200, 136), {}),
  # A smooth source lets the encoder keep whole 64x64 and 32x32 coding units.
  ("largeunits", "smptebars", (256, 192), {}),
  # The tool set and geometry a camera keyframe actually arrives with: a partial bottom CTB row,
  # and content dense enough to drive coefficient levels into the escape-coded range.
  ("fullhd", "testsrc2", (1920, 1080),
   {"qp": None, "crf": "30", "aq-mode": "1", "sao": "1", "tskip": "1"}),
  ("everything", "testsrc2", (200, 136),
   {"qp": None, "crf": "28", "aq-mode": "1", "sao": "1", "tskip": "1", "signhide": "1",
    "tu-intra-depth": "3"}),
]


def run(args):
  subprocess.run(args, check=True, capture_output=True)


def emit(name, source, size, overrides):
  params = {k: v for k, v in (BASELINE | overrides).items() if v is not None}
  width, height = size
  stream = OUT_DIR / f"{name}.h265"

  run([
    "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
    "-f", "lavfi", "-i", f"{source}=size={width}x{height}:rate=1",
    "-frames:v", "1", "-c:v", "libx265",
    "-x265-params", ":".join(f"{k}={v}" for k, v in params.items()),
    "-f", "hevc", str(stream),
  ])

  # Area resampling averages each source block, which is what the tests downscale the decoded
  # picture with. A different kernel would put its own error into the comparison.
  #
  # The chroma planes start at half size, so the same target lands all three at one size, which is
  # also the ratio the thumbnail encoder pairs luma and chroma at.
  reduced = f"scale={max(1, width // SCALE)}:{max(1, height // SCALE)}"
  for plane, suffix in (("y", "y"), ("u", "cb"), ("v", "cr")):
    run([
      "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
      "-i", str(stream), "-sws_flags", "area",
      "-vf", f"extractplanes={plane},{reduced}",
      "-f", "rawvideo", str(OUT_DIR / f"{name}.{suffix}"),
    ])

  print(f"{name:20} {width}x{height} {stream.stat().st_size:6d} bytes", file=sys.stderr)


def main():
  OUT_DIR.mkdir(parents=True, exist_ok=True)
  for name, source, size, overrides in FIXTURES:
    emit(name, source, size, overrides)


if __name__ == "__main__":
  main()
