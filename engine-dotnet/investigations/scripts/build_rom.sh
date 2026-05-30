#!/usr/bin/env bash
set -e
REPO=/mnt/n/dev/scratch/slop-ware/pokegold.worktrees/agents-fun-compiler-high-level-port
RGBDS_DIR=$HOME/rgbds-1.0.1

echo "=== checking build deps ==="
for c in gcc make bison flex pkg-config; do
  command -v "$c" >/dev/null 2>&1 || { echo "FATAL: missing $c (run the apt-get install first)"; exit 1; }
done
[ -f /usr/include/png.h ] || { echo "FATAL: missing libpng-dev (png.h)"; exit 1; }
echo "deps OK"

if [ ! -x "$RGBDS_DIR/rgbasm" ]; then
  echo "=== building rgbds v1.0.1 ==="
  rm -rf "$RGBDS_DIR"
  git clone --depth 1 -b v1.0.1 https://github.com/gbdev/rgbds "$RGBDS_DIR"
  make -C "$RGBDS_DIR" -j"$(nproc)" Q=
else
  echo "=== rgbds already built ==="
fi
"$RGBDS_DIR/rgbasm" --version

echo "=== building pokegold.gbc ==="
cd "$REPO"
make RGBDS="$RGBDS_DIR/" clean >/dev/null 2>&1 || true
make RGBDS="$RGBDS_DIR/" gold -j"$(nproc)"

echo "=== verifying hash ==="
sha1sum pokegold.gbc
echo "expected: d8b8a3600a465308c9953dfa04f0081c05bdcb94"
ls -l pokegold.gbc pokegold.sym 2>/dev/null || true
