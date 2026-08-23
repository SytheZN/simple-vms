#!/usr/bin/env python3
"""Generate small all-intra AVC fixtures for the thumbnail decoder tests.

Run from the project root:

    ./scripts/generators/build-h264-fixtures.py

Each fixture enables one coding tool on top of a baseline that has them all off, so a
decoder failure names the tool the fixture introduces. Alongside every stream it writes a
reference decode of the same frame at the decoder's output scale, as raw 8-bit planes.

Requires ffmpeg with libx264. The streams are synthetic, so unlike a camera capture they
can be committed.
"""

import subprocess
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parent.parent
# Its own directory: several fixture names are shared with the HEVC set, and the reference
# planes carry no codec in their name.
OUT_DIR = REPO_ROOT / "tests/Tests.Unit/Thumbnail/fixtures/h264"

SCALE = 8

BASELINE = {
  "keyint": "1",
  # Sliced threading splits the picture into several slice NALs with their own CABAC state,
  # which the decoder does not implement and a camera does not use.
  "threads": "1",
  "slices": "0",
  "cabac": "1",
  "8x8dct": "0",
  "cqm": "flat",
  "aq-mode": "0",
  "qp": "30",
  # x264 takes two off whatever offset it is asked for while psy-RD is on, so 2 is what lands a
  # literal zero in the PPS and leaves the baseline with the tool genuinely off.
  "chroma-qp-offset": "2",
}

# Name -> (source, size, profile, settings overriding the baseline). A setting of None is
# removed, which matters for qp: adaptive quantisation only engages once rate control is not
# fixed-QP, and it is what makes the encoder signal mb_qp_delta at all.
FIXTURES = [
  ("plain", "testsrc2", (256, 128), "main", {}),
  # Deblocking is the only stage of a conformant decode the thumbnail decoder skips, and it runs
  # after reconstruction rather than feeding it. Turning it off leaves nothing between this stream
  # and an exact match, so the tests can hold this one to a far tighter tolerance than the rest.
  ("nofilter", "testsrc2", (256, 128), "main", {"no-deblock": "1"}),
  ("cavlc", "testsrc2", (256, 128), "baseline", {"cabac": "0"}),
  # Covers Intra_8x8 prediction as well as the 8x8 transform: x264 mixes partition sizes within a
  # picture and offers no way to pin one, so both arrive together or not at all.
  ("dct8x8", "testsrc2", (256, 128), "high", {"8x8dct": "1"}),
  ("scalingmatrix", "testsrc2", (256, 128), "high", {"cqm": "jvt"}),
  ("mbqpdelta", "testsrc2", (256, 128), "main",
   {"qp": None, "crf": "28", "aq-mode": "1"}),
  ("chromaqp", "testsrc2", (256, 128), "main", {"chroma-qp-offset": "6"}),
  # 200x136 codes as 208x144, so the decoder has to crop and the rightmost and bottom
  # macroblocks fall outside the displayed picture.
  ("boundary", "testsrc2", (200, 136), "main", {}),
  # The tool set and geometry a camera keyframe actually arrives with: 1080 codes as 1088, and
  # content dense enough to drive coefficient levels into the escape-coded range.
  ("fullhd", "testsrc2", (1920, 1080), "high",
   {"qp": None, "crf": "30", "aq-mode": "1", "8x8dct": "1"}),
  ("everything", "testsrc2", (200, 136), "high",
   {"qp": None, "crf": "28", "aq-mode": "1", "8x8dct": "1", "cqm": "jvt",
    "chroma-qp-offset": "6"}),
]


def run(args):
  subprocess.run(args, check=True, capture_output=True)


def emit(name, source, size, profile, overrides):
  params = {k: v for k, v in (BASELINE | overrides).items() if v is not None}
  width, height = size
  stream = OUT_DIR / f"{name}.h264"

  run([
    "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
    "-f", "lavfi", "-i", f"{source}=size={width}x{height}:rate=1",
    "-frames:v", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p",
    "-profile:v", profile,
    "-x264-params", ":".join(f"{k}={v}" for k, v in params.items()),
    "-f", "h264", str(stream),
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

  print(f"{name:20} {profile:8} {width}x{height} {stream.stat().st_size:6d} bytes",
        file=sys.stderr)


def main():
  OUT_DIR.mkdir(parents=True, exist_ok=True)
  for name, source, size, profile, overrides in FIXTURES:
    emit(name, source, size, profile, overrides)


if __name__ == "__main__":
  main()
