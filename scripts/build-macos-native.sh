#!/usr/bin/env bash
set -euo pipefail

project_dir="${1:?project directory is required}"
target_dir="${2:?target directory is required}"
runtime_identifier="${3:-}"
repo_root="$(cd "${project_dir}/../.." && pwd)"
sdk_root="$(xcrun --sdk macosx --show-sdk-path)"

if [[ -z "${runtime_identifier}" ]]; then
  case "$(uname -m)" in
    arm64) runtime_identifier="osx-arm64" ;;
    x86_64) runtime_identifier="osx-x64" ;;
    *) echo "Unsupported macOS host architecture: $(uname -m)" >&2; exit 1 ;;
  esac
fi

case "${runtime_identifier}" in
  osx-arm64) clang_arch="arm64" ;;
  osx-x64) clang_arch="x86_64" ;;
  *) echo "Unsupported macOS runtime identifier: ${runtime_identifier}" >&2; exit 1 ;;
esac

native_out="${project_dir}/native/${runtime_identifier}"

mkdir -p "${native_out}" "${target_dir}"

clang++ \
  -std=c++20 \
  -fobjc-arc \
  -dynamiclib \
  -arch "${clang_arch}" \
  -isysroot "${sdk_root}" \
  "${repo_root}/src/InstantTraceViewerNative/ImGuiWindow.mac.mm" \
  -framework Cocoa \
  -framework Foundation \
  -framework QuartzCore \
  -framework Metal \
  -Wl,-rpath,@loader_path \
  -o "${native_out}/libInstantTraceViewerNative.dylib"

cp "${native_out}/libInstantTraceViewerNative.dylib" "${target_dir}/libInstantTraceViewerNative.dylib"
