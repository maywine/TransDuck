#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 || ( "$2" != "osx-x64" && "$2" != "osx-arm64" ) ]]; then
  echo "usage: $0 <zip-path> <osx-x64|osx-arm64>" >&2
  exit 2
fi

script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repository_root=$(cd -- "$script_directory/../.." && pwd)
dotnet_command=${TRANSDUCK_DOTNET:-dotnet}
zip_path=$(realpath "$1")
runtime_identifier=$2
version=$(sed -n 's:.*<VersionPrefix>\([^<]*\)</VersionPrefix>.*:\1:p' "$repository_root/Directory.Build.props")

"$dotnet_command" run \
  --project "$script_directory/TransDuck.Packaging/TransDuck.Packaging.csproj" \
  --configuration Release \
  -- verify "$zip_path" "$runtime_identifier" "$version"

if [[ $(uname -s) == Darwin ]]; then
  extraction_root=$(mktemp -d /tmp/transduck-macos-verify.XXXXXX)
  trap 'rm -rf -- "$extraction_root"' EXIT
  /usr/bin/ditto -x -k "$zip_path" "$extraction_root"
  app_directory="$extraction_root/TransDuck.app"
  if [[ ! -x "$app_directory/Contents/MacOS/TransDuck" ]]; then
    echo "extracted_app_is_not_executable" >&2
    exit 1
  fi
  /usr/bin/codesign --verify --deep --strict --all-architectures "$app_directory"
  host_architecture=$(uname -m)
  if [[ ( "$runtime_identifier" == "osx-x64" && "$host_architecture" == "x86_64" ) ||
        ( "$runtime_identifier" == "osx-arm64" && "$host_architecture" == "arm64" ) ]]; then
    "$app_directory/Contents/MacOS/TransDuck" --smoke-test
  fi
  echo "native_package_verified: $zip_path"
else
  echo "native_package_verification_requires_macOS" >&2
fi
