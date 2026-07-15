#!/bin/bash
# Builds a Velopack release for one runtime: publishes a self-contained agent,
# then runs `vpk pack` to produce the installer + full/delta packages + the
# update-feed files the running agent reads from GitHub Releases.
#
# Usage: scripts/pack.sh <version> <rid>
#   rid is one of: win-x64 | osx-x64 | osx-arm64 | linux-x64
#
# `vpk` (the Velopack CLI, pinned to 1.2.0 to match the NuGet package) must be on
# PATH and must run on the matching OS: Windows packs Setup.exe, macOS packs the
# .app/.pkg, Linux packs the .AppImage. Install it with:
#   dotnet tool install -g vpk --version 1.2.0
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

VERSION="${1:?usage: scripts/pack.sh <version> <rid>}"
RID="${2:?usage: scripts/pack.sh <version> <rid>}"

PROJECT="agent/HshAgent/HshAgent.csproj"
PUBLISH_DIR="dist/publish/$RID"
RELEASE_DIR="dist/releases"

case "$RID" in
  win-*) MAIN_EXE="HshAgent.exe" ;;
  *)     MAIN_EXE="HshAgent" ;;
esac

echo "==> Publishing $RID (self-contained) to $PUBLISH_DIR"
# Not single-file: Velopack needs the loose publish folder to compute deltas.
dotnet publish "$PROJECT" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:Version="$VERSION" \
  -o "$PUBLISH_DIR"

# Optional branding/theming applied per-RID, only when the asset exists so plain
# dev builds still pack with no extra files. macOS uses an .icns + plain-text
# installer panes (welcome/readme/conclusion — they render in the system text
# colour, so they stay readable in light and dark mode); Windows uses an .ico;
# Linux uses a .png. Regenerate the icons with scripts/generate-icons.sh.
BRAND_ARGS=()
add_brand() { [ -e "$2" ] && BRAND_ARGS+=("$1" "$2"); }   # add_brand <flag> <path>

# --noPortable drops Velopack's portable (no-installer) zip so we ship installable
# artifacts only. It applies to Windows/macOS; Linux packs an AppImage (inherently
# portable, no separate portable build) and vpk rejects the flag there.
PACK_OPTS=()
case "$RID" in
  linux-*) ;;
  *)       PACK_OPTS+=(--noPortable) ;;
esac

case "$RID" in
  win-*)
    add_brand --icon installer-assets/windows/hsh.ico
    ;;
  osx-*)
    add_brand --icon            installer-assets/macos/hsh.icns
    add_brand --instWelcome     installer-assets/macos/welcome.txt
    add_brand --instReadme      installer-assets/macos/readme.txt
    add_brand --instConclusion  installer-assets/macos/conclusion.txt
    # LSUIElement=true: without it, `open`-based launches (including
    # UpdateMac's own post-update relaunch) give the headless agent a Dock
    # icon that immediately shows "Not Responding". --plist and --bundleId
    # are mutually exclusive, so the bundle id lives in Info.plist itself.
    add_brand --plist           installer-assets/macos/Info.plist
    ;;
  linux-*)
    add_brand --icon installer-assets/linux/hsh.png
    ;;
esac

echo "==> Packing Velopack release for $RID into $RELEASE_DIR"
# --channel "$RID" gives each OS *and arch* its own channel (win-x64 / osx-arm64 /
# osx-x64 / linux-x64) so they coexist in one GitHub Release — Velopack's default
# channel is per-OS only ("osx"), which can't host arm64 + x64 side by side. The
# agent requests the channel matching its own architecture (see UpdateService.cs).
# Code-signing flags (--azureTrustedSignFile on Windows; --signAppIdentity /
# --notaryProfile on macOS) are intentionally omitted until certificates exist.
vpk pack \
  --packId HshAgent \
  --packTitle "HSH Agent" \
  --packAuthors "HSH" \
  --packVersion "$VERSION" \
  --packDir "$PUBLISH_DIR" \
  --mainExe "$MAIN_EXE" \
  --runtime "$RID" \
  --channel "$RID" \
  --outputDir "$RELEASE_DIR" \
  "${PACK_OPTS[@]}" \
  "${BRAND_ARGS[@]}"

# vpk hardcodes the .pkg to offer an "all users" install (/Applications,
# root-owned), which breaks self-update: UpdateMac can't rename a root-owned
# bundle, and its elevation fallback blocks on GUI dialogs a background
# LaunchAgent process can never bring to the front on macOS 14+ — they sit
# invisible and auto-cancel after 5 minutes. Rewrite the package to
# home-domain-only (~/Applications, user-owned, matching the per-user install
# on Windows) so updates never need elevation, and replace vpk's stock
# postinstall: it was written for a root install (`sudo -u "$USER" …`), which
# fails when the script itself already runs unprivileged.
case "$RID" in
  osx-*)
    PKG="$RELEASE_DIR/HshAgent-$RID-Setup.pkg"
    echo "==> Rewriting $PKG as a per-user (home-domain) installer"
    EXPAND_DIR="$(mktemp -d)/expanded"
    pkgutil --expand "$PKG" "$EXPAND_DIR"

    sed -i '' 's/enable_localSystem="true"/enable_localSystem="false"/' "$EXPAND_DIR/Distribution"
    grep -q 'enable_currentUserHome="true".*enable_localSystem="false"' "$EXPAND_DIR/Distribution" \
      || { echo "error: expected <domains> line not found — did vpk's pkg layout change?" >&2; exit 1; }

    COMPONENT=("$EXPAND_DIR"/*.pkg)
    [ -f "${COMPONENT[0]}/Scripts/postinstall" ] \
      || { echo "error: component postinstall not found — did vpk's pkg layout change?" >&2; exit 1; }
    cat > "${COMPONENT[0]}/Scripts/postinstall" <<'POSTINSTALL'
#!/bin/sh
# Home-domain install: runs unprivileged as the installing user; $2 is
# ~/Applications. Clears stale Velopack temp/cache state, then launches the
# app once so its first-run hook registers the login LaunchAgent.
rm -rf /tmp/velopack/HshAgent
rm -rf "$HOME/Library/Caches/velopack/HshAgent"
env VELOPACK_FIRSTRUN=1 open "$2/HSH Agent.app/"
exit 0
POSTINSTALL
    chmod +x "${COMPONENT[0]}/Scripts/postinstall"

    rm -f "$PKG"
    pkgutil --flatten "$EXPAND_DIR" "$PKG"
    ;;
esac

echo "==> Done. Release artifacts (installer + *.nupkg + releases.*.json) in $RELEASE_DIR/"
