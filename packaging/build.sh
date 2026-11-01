#!/bin/bash
# Builds dist/bcfetch-linux-x64.tar.gz, dist/bcfetch_<version>_amd64.deb and dist/SHA256SUMS.
# Needs the .NET SDK and dpkg-deb. Usage: packaging/build.sh
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION=$(sed -n 's#.*<VersionPrefix>\(.*\)</VersionPrefix>.*#\1#p' Directory.Build.props)
rm -rf dist && mkdir -p dist/publish

dotnet publish src/BcFetch.App -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None \
  -o dist/publish

# tar.gz (asset name is what UpdatesService expects)
cp packaging/bcfetch.desktop src/BcFetch.App/Assets/Cloud.png dist/publish/
tar -C dist/publish -czf dist/bcfetch-linux-x64.tar.gz bcfetch bcfetch.desktop Cloud.png
rm dist/publish/bcfetch.desktop dist/publish/Cloud.png

# .deb: /opt/bcfetch + symlink in /usr/bin
D=dist/deb
mkdir -p $D/DEBIAN $D/opt/bcfetch $D/usr/bin $D/usr/share/applications $D/usr/share/icons/hicolor/512x512/apps
cp dist/publish/bcfetch $D/opt/bcfetch/
ln -s /opt/bcfetch/bcfetch $D/usr/bin/bcfetch
cp packaging/bcfetch.desktop $D/usr/share/applications/
cp src/BcFetch.App/Assets/Cloud.png $D/usr/share/icons/hicolor/512x512/apps/bcfetch.png
cat > $D/DEBIAN/control <<CTL
Package: bcfetch
Version: $VERSION
Architecture: amd64
Maintainer: d3codedvision
Depends: libfontconfig1, libx11-6, libice6, libsm6
Description: Download albums from Bandcamp
 Avalonia GUI port of BandcampDownloader.
CTL
chmod -R go-w $D
dpkg-deb --root-owner-group --build $D dist/bcfetch_${VERSION}_amd64.deb >/dev/null

(cd dist && sha256sum bcfetch-linux-x64.tar.gz bcfetch_*_amd64.deb > SHA256SUMS)
rm -rf dist/deb dist/publish
ls -lh dist
