#!/bin/bash
# Generates the placeholder HSH app/installer icons from a single rendered mark,
# committing the outputs so the build (and CI) never need any image tooling:
#
#   installer-assets/macos/hsh.icns     — macOS .app bundle icon (--icon)
#   installer-assets/windows/hsh.ico    — Windows Setup.exe / .app icon (--icon)
#   installer-assets/linux/hsh.png      — Linux AppImage icon (--icon)
#   web/public/favicon.ico              — browser tab icon
#
# The mark itself is drawn in pure Python (no ImageMagick/Pillow needed); only
# the .icns step uses macOS's sips + iconutil. Re-run this any time to refresh
# the icons, or replace the generated files with a real logo later.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

OUT_MAC="installer-assets/macos"
OUT_WIN="installer-assets/windows"
OUT_LINUX="installer-assets/linux"
mkdir -p "$OUT_MAC" "$OUT_WIN" "$OUT_LINUX" web/public

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

echo "==> Rendering placeholder mark + ICO/PNG via Python"
python3 - "$WORK" "$OUT_WIN/hsh.ico" "$OUT_LINUX/hsh.png" web/public/favicon.ico <<'PY'
import sys, struct, zlib

work, ico_path, linux_png, favicon = sys.argv[1:5]

def render(size):
    """A rounded blue square with a white 'H' — RGBA bytes for one image."""
    n = size
    radius = int(n * 0.18)
    # vertical gradient endpoints
    top = (0x3b, 0x6e, 0xf5)
    bot = (0x2a, 0x4f, 0xb0)
    # 'H' geometry
    bar_w = int(n * 0.13)
    left = int(n * 0.30); right = n - left
    cross_t = int(n * 0.43); cross_b = n - cross_t
    inset = int(n * 0.22)  # vertical extent of the H legs

    def in_round(x, y):
        # rounded-corner mask: clamp to the inner rect (inset by radius); a point
        # is inside iff it's within `radius` of that clamped point.
        cx = min(max(x, radius), n - 1 - radius)
        cy = min(max(y, radius), n - 1 - radius)
        return (x - cx) ** 2 + (y - cy) ** 2 <= radius ** 2

    rows = bytearray()
    for y in range(n):
        rows.append(0)  # filter type 0
        t = y / max(1, n - 1)
        bg = (int(top[0] + (bot[0] - top[0]) * t),
              int(top[1] + (bot[1] - top[1]) * t),
              int(top[2] + (bot[2] - top[2]) * t))
        for x in range(n):
            if not in_round(x, y):
                rows += b'\x00\x00\x00\x00'
                continue
            is_h = (
                (inset <= y <= n - inset) and (
                    (left <= x <= left + bar_w) or
                    (right - bar_w <= x <= right) or
                    (cross_t <= y <= cross_b and left <= x <= right)
                )
            )
            if is_h:
                rows += b'\xff\xff\xff\xff'
            else:
                rows += bytes((bg[0], bg[1], bg[2], 255))
    return bytes(rows)

def png_bytes(size):
    raw = render(size)
    def chunk(tag, data):
        return (struct.pack('>I', len(data)) + tag + data +
                struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff))
    sig = b'\x89PNG\r\n\x1a\n'
    ihdr = struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0)
    idat = zlib.compress(raw, 9)
    return sig + chunk(b'IHDR', ihdr) + chunk(b'IDAT', idat) + chunk(b'IEND', b'')

def write_png(path, size):
    with open(path, 'wb') as f:
        f.write(png_bytes(size))

def write_ico(path, sizes):
    imgs = [(s, png_bytes(s)) for s in sizes]
    n = len(imgs)
    out = struct.pack('<HHH', 0, 1, n)
    offset = 6 + 16 * n
    entries = b''
    body = b''
    for s, data in imgs:
        w = 0 if s >= 256 else s
        entries += struct.pack('<BBBBHHII', w, w, 0, 0, 1, 32, len(data), offset)
        body += data
        offset += len(data)
    with open(path, 'wb') as f:
        f.write(out + entries + body)

# base mark for the .icns step (consumed by sips), Linux icon, ICOs
write_png(f"{work}/hsh_1024.png", 1024)
write_png(linux_png, 512)
write_ico(ico_path, [16, 32, 48, 64, 128, 256])
write_ico(favicon, [16, 32, 48])
print("   wrote", ico_path, linux_png, favicon)
PY

if command -v sips >/dev/null && command -v iconutil >/dev/null; then
  echo "==> Building hsh.icns via sips + iconutil"
  ICONSET="$WORK/hsh.iconset"
  mkdir -p "$ICONSET"
  for s in 16 32 64 128 256 512; do
    sips -z "$s" "$s" "$WORK/hsh_1024.png" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
    d=$((s * 2))
    sips -z "$d" "$d" "$WORK/hsh_1024.png" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
  done
  cp "$WORK/hsh_1024.png" "$ICONSET/icon_512x512@2x.png"
  iconutil -c icns "$ICONSET" -o "$OUT_MAC/hsh.icns"
  echo "   wrote $OUT_MAC/hsh.icns"
else
  echo "!! sips/iconutil not found (non-macOS) — skipping hsh.icns."
  echo "   Run this script on macOS, or keep the committed hsh.icns."
fi

echo "==> Done."
