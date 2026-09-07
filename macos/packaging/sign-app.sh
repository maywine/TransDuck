#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 || $(uname -s) != Darwin ]]; then
  echo "usage (on macOS): $0 <TransDuck.app>" >&2
  exit 2
fi

app_directory=$(realpath "$1")
macos_directory="$app_directory/Contents/MacOS"
runtime_directory="$app_directory/Contents/Resources/Runtime"
if [[ $(basename "$app_directory") != TransDuck.app || ! -f "$macos_directory/TransDuck" ]]; then
  echo "invalid_app_bundle" >&2
  exit 1
fi

mkdir -p "$runtime_directory"
shopt -s nullglob dotglob
for payload in "$macos_directory"/*; do
  [[ -L "$payload" ]] && continue
  name=$(basename "$payload")
  if [[ -f "$payload" && $(/usr/bin/file -b "$payload") == *Mach-O* ]]; then
    chmod 755 "$payload"
    if [[ $name != TransDuck ]]; then
      /usr/bin/codesign --force --sign - --timestamp=none "$payload"
      # The .NET host resolves the managed entry point's real directory.
      # Keep native dependencies available there without duplicating code.
      if [[ ! -e "$runtime_directory/$name" && ! -L "$runtime_directory/$name" ]]; then
        ln -s "../../MacOS/$name" "$runtime_directory/$name"
      fi
    fi
  else
    # Non-Mach-O code belongs in Resources. Signing loose .NET DLLs in
    # MacOS would put their signatures in fragile extended attributes.
    if [[ -e "$runtime_directory/$name" || -L "$runtime_directory/$name" ]]; then
      echo "runtime_payload_already_exists: $name" >&2
      exit 1
    fi
    mv "$payload" "$runtime_directory/$name"
    ln -s "../Resources/Runtime/$name" "$payload"
  fi
done

# Ad-hoc signing seals the complete bundle; it does not establish a Developer
# ID identity or notarize the app. Do not change bundle contents after this.
/usr/bin/codesign --force --sign - --timestamp=none "$app_directory"
/usr/bin/codesign --verify --deep --strict --all-architectures "$app_directory"
