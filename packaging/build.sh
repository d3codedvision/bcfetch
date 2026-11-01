#!/bin/bash
# Builds dist/bcfetch-linux-x64.tar.gz, dist/bcfetch_<version>_amd64.deb, dist/bcfetch-<version>-x86_64.AppImage and dist/SHA256SUMS.
# Needs the .NET SDK, dpkg-deb and curl (appimagetool and its runtime are pinned, downloaded once and checksum-verified). Usage: packaging/build.sh
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION=$(sed -n 's#.*<VersionPrefix>\(.*\)</VersionPrefix>.*#\1#p' Directory.Build.props)
rm -rf dist && mkdir -p dist/publish

# AppStream metadata (license, description, release notes) for software centres, with the CHANGELOG section of this version
META=io.github.d3codedvision.bcfetch.metainfo.xml
DESKTOP=io.github.d3codedvision.bcfetch.desktop
python3 packaging/make-metainfo.py "$VERSION" "$(git log -1 --format=%cs 2>/dev/null || date +%F)" packaging/$META.in CHANGELOG.md dist/$META
if command -v appstreamcli >/dev/null; then appstreamcli validate --no-net dist/$META; fi
if command -v desktop-file-validate >/dev/null; then desktop-file-validate packaging/$DESKTOP; fi

dotnet publish src/BcFetch.App -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None \
  -o dist/publish

# tar.gz (asset name is what UpdatesService expects)
cp packaging/$DESKTOP src/BcFetch.App/Assets/Cloud.png dist/publish/
tar -C dist/publish -czf dist/bcfetch-linux-x64.tar.gz bcfetch $DESKTOP Cloud.png
rm dist/publish/$DESKTOP dist/publish/Cloud.png

# .deb: /opt/bcfetch + symlink in /usr/bin
D=dist/deb
mkdir -p $D/DEBIAN $D/opt/bcfetch $D/usr/bin $D/usr/share/applications $D/usr/share/metainfo $D/usr/share/doc/bcfetch $D/usr/share/icons/hicolor/512x512/apps
cp dist/publish/bcfetch $D/opt/bcfetch/
ln -s /opt/bcfetch/bcfetch $D/usr/bin/bcfetch
cp packaging/$DESKTOP $D/usr/share/applications/
cp dist/$META $D/usr/share/metainfo/
cp packaging/copyright $D/usr/share/doc/bcfetch/copyright
cp src/BcFetch.App/Assets/Cloud.png $D/usr/share/icons/hicolor/512x512/apps/bcfetch.png
cat > $D/DEBIAN/control <<CTL
Package: bcfetch
Version: $VERSION
Section: sound
Priority: optional
Architecture: amd64
Maintainer: d3codedvision
Homepage: https://github.com/d3codedvision/bcfetch
Installed-Size: $(du -sk $D/opt $D/usr | awk '{s+=$1} END {print s}')
Depends: libfontconfig1, libx11-6, libice6, libsm6
Recommends: libnotify-bin
Description: Download albums from Bandcamp
 bcfetch downloads Bandcamp albums and artist discographies as MP3 files
 with tags and cover art. It has a graphical window and a command-line mode.
 .
 It is a Linux port of BandcampDownloader. Not affiliated with Bandcamp.
CTL
chmod -R go-w $D
dpkg-deb --root-owner-group --build $D dist/bcfetch_${VERSION}_amd64.deb >/dev/null

# AppImage: AppDir with the single-file binary, packed by a pinned, checksum-verified appimagetool
CACHE=${XDG_CACHE_HOME:-$HOME/.cache}/bcfetch-build
# fetch <url> <sha256> <file>: download once, verify before the file is ever executed or embedded
fetch() {
  [ -x "$3" ] && return
  mkdir -p "$CACHE"
  curl -fsSL -o "$3" "$1"
  echo "$2  $3" | sha256sum -c - >/dev/null || { rm -f "$3"; echo "checksum mismatch for $1" >&2; exit 1; }
  chmod +x "$3"
}
TOOL=$CACHE/appimagetool-1.9.1-x86_64.AppImage
RUNTIME=$CACHE/runtime-20251108-x86_64
fetch https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage \
  ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0 "$TOOL"
fetch https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-x86_64 \
  2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d "$RUNTIME"
A=dist/AppDir
mkdir -p $A/usr/bin
cp dist/publish/bcfetch $A/usr/bin/
cp packaging/$DESKTOP $A/
mkdir -p $A/usr/share/metainfo
cp dist/$META $A/usr/share/metainfo/
cp src/BcFetch.App/Assets/Cloud.png $A/bcfetch.png
ln -s bcfetch.png $A/.DirIcon
ln -s usr/bin/bcfetch $A/AppRun
ARCH=x86_64 "$TOOL" --appimage-extract-and-run --runtime-file "$RUNTIME" $A dist/bcfetch-$VERSION-x86_64.AppImage >/dev/null

(cd dist && sha256sum bcfetch-linux-x64.tar.gz bcfetch_*_amd64.deb bcfetch-*.AppImage > SHA256SUMS)
rm -rf dist/deb dist/publish dist/AppDir dist/$META
ls -lh dist
