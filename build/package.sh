#!/usr/bin/env bash
# Builds the release zip (JellyIptv.dll + meta.json + thumb.png) and updates manifest.json.
# Usage: build/package.sh "changelog text"
set -euo pipefail
cd "$(dirname "$0")/.."

REPO=KsmKayk/jellyiptv
VERSION=$(sed -n 's:.*<AssemblyVersion>\(.*\)</AssemblyVersion>.*:\1:p' src/JellyIptv/JellyIptv.csproj)
ABI=10.10.7.0
CHANGELOG=${1:-"Versão $VERSION"}
OUT=artifacts/release
ZIP=jellyiptv_$VERSION.zip
TIMESTAMP=$(date -u +%Y-%m-%dT%H:%M:%SZ)

rm -rf "$OUT" && mkdir -p "$OUT/pkg"
dotnet publish src/JellyIptv/JellyIptv.csproj -c Release -o artifacts/publish
cp artifacts/publish/JellyIptv.dll "$OUT/pkg/"
cp assets/banner.png "$OUT/pkg/thumb.png"
cat > "$OUT/pkg/meta.json" <<JSON
{
  "category": "Live TV",
  "description": "Assista e baixe filmes, séries e TV ao vivo de uma conta Xtream Codes.",
  "guid": "f16dea81-517f-4f93-9da7-cb0dd194a373",
  "name": "JellyIPTV",
  "overview": "Filmes, séries e TV ao vivo de uma conta Xtream Codes.",
  "owner": "KsmKayk",
  "targetAbi": "$ABI",
  "timestamp": "$TIMESTAMP",
  "version": "$VERSION",
  "status": "Active",
  "autoUpdate": false,
  "imagePath": "thumb.png",
  "assemblies": []
}
JSON
(cd "$OUT/pkg" && python -c "import zipfile,sys,os; z=zipfile.ZipFile(sys.argv[1],'w',zipfile.ZIP_DEFLATED); [z.write(f) for f in sorted(os.listdir('.')) if f!=sys.argv[1]]" "../$ZIP")
MD5=$(md5sum "$OUT/$ZIP" | cut -d' ' -f1)

python - "$VERSION" "$ABI" "$MD5" "$TIMESTAMP" "$CHANGELOG" "https://github.com/$REPO/releases/download/v$VERSION/$ZIP" <<'PY'
import json, sys
version, abi, md5, ts, changelog, url = sys.argv[1:]
path = "manifest.json"
data = json.load(open(path, encoding="utf-8"))
versions = [v for v in data[0]["versions"] if v["version"] != version]
versions.insert(0, {"version": version, "changelog": changelog, "targetAbi": abi,
                    "sourceUrl": url, "checksum": md5, "timestamp": ts})
data[0]["versions"] = versions
with open(path, "w", encoding="utf-8", newline="\n") as f:
    json.dump(data, f, ensure_ascii=False, indent=2)
    f.write("\n")
PY
echo "$OUT/$ZIP  md5=$MD5"
