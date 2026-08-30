#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 || ( "$1" != "osx-x64" && "$1" != "osx-arm64" ) ]]; then
  echo "usage: $0 <osx-x64|osx-arm64>" >&2
  exit 2
fi

runtime_identifier=$1
script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repository_root=$(cd -- "$script_directory/../.." && pwd)
dotnet_command=${TRANSDUCK_DOTNET:-dotnet}
version=$(sed -n 's:.*<VersionPrefix>\([^<]*\)</VersionPrefix>.*:\1:p' "$repository_root/Directory.Build.props")

if [[ ! $version =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]]; then
  echo "invalid_version" >&2
  exit 1
fi

case "$runtime_identifier" in
  osx-x64) architecture=x64 ;;
  osx-arm64) architecture=arm64 ;;
esac

artifacts_directory="$script_directory/artifacts"
zip_path="$artifacts_directory/TransDuck-macOS-$architecture.zip"
staging_root=$(mktemp -d /tmp/transduck-macos-package.XXXXXX)
trap 'rm -rf -- "$staging_root"' EXIT

publish_directory="$staging_root/publish"
app_directory="$staging_root/TransDuck.app"
contents_directory="$app_directory/Contents"
macos_directory="$contents_directory/MacOS"
resources_directory="$contents_directory/Resources"

"$dotnet_command" publish "$repository_root/macos/src/TransDuck.App/TransDuck.App.csproj" \
  --configuration Release \
  --runtime "$runtime_identifier" \
  --self-contained true \
  --output "$publish_directory" \
  --artifacts-path "$staging_root/build-artifacts" \
  -p:PublishTrimmed=false \
  -p:DebugType=None \
  -p:DebugSymbols=false

mkdir -p "$macos_directory" "$resources_directory/licenses" "$artifacts_directory"
cp -R -p "$publish_directory/." "$macos_directory/"
chmod 755 "$macos_directory/TransDuck"
sed "s/@VERSION@/$version/g" "$script_directory/Info.plist.in" > "$contents_directory/Info.plist"
cp "$repository_root/assets/brand-source-icon/TransDuck.icns" "$resources_directory/TransDuck.icns"
cp "$repository_root/LICENSE" "$resources_directory/LICENSE"
cp "$repository_root/macos/THIRD-PARTY-NOTICES.md" "$resources_directory/THIRD-PARTY-NOTICES.md"
cp -R -p "$repository_root/macos/third_party/licenses/." "$resources_directory/licenses/"

if [[ $(uname -s) == Darwin ]]; then
  /usr/bin/plutil -lint "$contents_directory/Info.plist"
  host_architecture=$(uname -m)
  if [[ ( "$runtime_identifier" == "osx-x64" && "$host_architecture" == "x86_64" ) ||
        ( "$runtime_identifier" == "osx-arm64" && "$host_architecture" == "arm64" ) ]]; then
    "$macos_directory/TransDuck" --smoke-test
  fi
fi

"$dotnet_command" run \
  --project "$script_directory/TransDuck.Packaging/TransDuck.Packaging.csproj" \
  --configuration Release \
  -- pack "$app_directory" "$zip_path"

echo "$zip_path"
