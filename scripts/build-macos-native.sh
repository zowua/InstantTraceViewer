#!/usr/bin/env bash
set -euo pipefail

project_dir="${1:?project directory is required}"
target_dir="${2:?target directory is required}"
runtime_identifier="${3:-}"
hexa_imgui_version="${4:-2.2.9}"
repo_root="$(cd "${project_dir}/../.." && pwd)"
sdk_root="$(xcrun --sdk macosx --show-sdk-path)"
nuget_root="${NUGET_PACKAGES:-${HOME}/.nuget/packages}"

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
imgui_native_dir="${nuget_root}/hexa.net.imgui/${hexa_imgui_version}/runtimes/${runtime_identifier}/native"
# Hexa.NET.ImGui 2.2.9 ships Dear ImGui 1.92.2b; build matching backends so IMGUI_CHECKVERSION remains meaningful.
imgui_ref="v1.92.2b-docking"
imgui_source_repo="${repo_root}/ThirdParty/cimgui/imgui"
imgui_source_dir="$(mktemp -d "${TMPDIR:-/tmp}/instant-trace-viewer-imgui.XXXXXX")"
trap 'rm -rf "${imgui_source_dir}"' EXIT

mkdir -p "${native_out}" "${target_dir}"

if [[ ! -f "${imgui_native_dir}/cimgui.dylib" ]]; then
  echo "Missing Hexa.NET.ImGui native cimgui at ${imgui_native_dir}/cimgui.dylib" >&2
  exit 1
fi

if [[ ! -d "${imgui_source_repo}/.git" && ! -f "${imgui_source_repo}/.git" ]]; then
  echo "Missing Dear ImGui submodule at ${imgui_source_repo}. Run git submodule update --init --recursive." >&2
  exit 1
fi

git -C "${imgui_source_repo}" archive "${imgui_ref}" \
  imgui.h \
  imconfig.h \
  backends/imgui_impl_osx.h \
  backends/imgui_impl_osx.mm \
  backends/imgui_impl_metal.h \
  backends/imgui_impl_metal.mm \
  | tar -x -C "${imgui_source_dir}"

clang++ \
  -std=c++20 \
  -fobjc-arc \
  -dynamiclib \
  -arch "${clang_arch}" \
  -isysroot "${sdk_root}" \
  -I"${imgui_source_dir}" \
  -I"${imgui_source_dir}/backends" \
  -I"${repo_root}/src/cimgui" \
  -DIMGUI_USER_CONFIG='"imconfig_custom.h"' \
  -DIMGUI_DISABLE_OBSOLETE_FUNCTIONS=1 \
  "${repo_root}/src/InstantTraceViewerNative/ImGuiWindow.mac.mm" \
  "${imgui_source_dir}/backends/imgui_impl_osx.mm" \
  "${imgui_source_dir}/backends/imgui_impl_metal.mm" \
  "${imgui_native_dir}/cimgui.dylib" \
  -framework Cocoa \
  -framework Foundation \
  -framework QuartzCore \
  -framework Metal \
  -framework MetalKit \
  -framework GameController \
  -framework Carbon \
  -Wl,-rpath,@loader_path \
  -o "${native_out}/libInstantTraceViewerNative.dylib"

cp "${imgui_native_dir}/cimgui.dylib" "${target_dir}/cimgui.dylib"
cp "${imgui_native_dir}/cimgui.dylib" "${target_dir}/libcimgui.dylib"
cp "${native_out}/libInstantTraceViewerNative.dylib" "${target_dir}/libInstantTraceViewerNative.dylib"
