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
