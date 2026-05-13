#!/usr/bin/env bash
set -euo pipefail

project_dir="${1:?project directory is required}"
target_dir="${2:?target directory is required}"
repo_root="$(cd "${project_dir}/../.." && pwd)"
native_out="${project_dir}/native/osx"
sdk_root="$(xcrun --sdk macosx --show-sdk-path)"
arch="$(uname -m)"
nuget_root="${NUGET_PACKAGES:-${HOME}/.nuget/packages}"
imgui_native_dir="${nuget_root}/hexa.net.imgui/2.2.9/runtimes/osx-${arch}/native"

mkdir -p "${native_out}" "${target_dir}"

if [[ ! -f "${imgui_native_dir}/cimgui.dylib" ]]; then
  echo "Missing Hexa.NET.ImGui native cimgui at ${imgui_native_dir}/cimgui.dylib" >&2
  exit 1
fi

clang++ \
  -std=c++20 \
  -fobjc-arc \
  -dynamiclib \
  -isysroot "${sdk_root}" \
  -I"${repo_root}/src/cimgui" \
  -I"${repo_root}/ThirdParty/cimgui" \
  -I"${repo_root}/ThirdParty/cimgui/imgui" \
  -DIMGUI_USER_CONFIG='"imconfig_custom.h"' \
  -DIMGUI_DISABLE_OBSOLETE_FUNCTIONS=1 \
  "${repo_root}/src/InstantTraceViewerNative/ImGuiWindow.mac.mm" \
  "${repo_root}/ThirdParty/cimgui/imgui/backends/imgui_impl_osx.mm" \
  "${repo_root}/ThirdParty/cimgui/imgui/backends/imgui_impl_metal.mm" \
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
