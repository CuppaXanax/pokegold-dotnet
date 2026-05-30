#!/usr/bin/env bash
echo "--- tools ---"
for c in gcc make git bison flex pkg-config curl xz cmake; do
  if command -v "$c" >/dev/null 2>&1; then echo "have $c -> $(command -v "$c")"; else echo "MISSING $c"; fi
done
echo "--- libpng dev ---"
ls /usr/include/png.h 2>/dev/null && echo "libpng-dev present" || echo "no libpng-dev header"
ls /usr/lib/x86_64-linux-gnu/libpng* 2>/dev/null || echo "no libpng runtime"
echo "--- os ---"
. /etc/os-release; echo "$PRETTY_NAME"
