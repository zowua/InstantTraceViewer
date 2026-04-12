#!/usr/bin/env bash
set -euo pipefail

project_dir="${1:?project directory is required}"
target_dir="${2:?target directory is required}"
repo_root="$(cd "${project_dir}/../.." && pwd)"
native_out="${project_dir}/native/osx"
sdk_root="$(xcrun --sdk macosx --show-sdk-path)"

mkdir -p "${native_out}" "${target_dir}"

clang++ \
  -std=c++20 \
  -dynamiclib \
  -isysroot "${sdk_root}" \
  -I"${repo_root}/ThirdParty/cimgui" \
  -I"${repo_root}/ThirdParty/cimgui/imgui" \
  -I"${repo_root}/src/cimgui" \
  -DIMGUI_USER_CONFIG='"imconfig_custom.h"' \
  -DIMGUI_DISABLE_OBSOLETE_FUNCTIONS=1 \
  -Wl,-install_name,@rpath/cimgui.dylib \
  "${repo_root}/ThirdParty/cimgui/cimgui.cpp" \
  "${repo_root}/ThirdParty/cimgui/imgui/imgui.cpp" \
  "${repo_root}/ThirdParty/cimgui/imgui/imgui_draw.cpp" \
  "${repo_root}/ThirdParty/cimgui/imgui/imgui_tables.cpp" \
  "${repo_root}/ThirdParty/cimgui/imgui/imgui_widgets.cpp" \
  "${repo_root}/ThirdParty/cimgui/imgui/imgui_demo.cpp" \
  -o "${native_out}/cimgui.dylib"

cp "${native_out}/cimgui.dylib" "${native_out}/libcimgui.dylib"

clang++ \
  -std=c++20 \
  -fobjc-arc \
  -dynamiclib \
  -isysroot "${sdk_root}" \
  -I"${repo_root}/ThirdParty/cimgui" \
  -I"${repo_root}/ThirdParty/cimgui/imgui" \
  -I"${repo_root}/src/cimgui" \
  -DIMGUI_USER_CONFIG='"imconfig_custom.h"' \
  -DIMGUI_DISABLE_OBSOLETE_FUNCTIONS=1 \
  "${repo_root}/src/InstantTraceViewerNative/ImGuiWindow.mac.mm" \
  "${repo_root}/ThirdParty/cimgui/imgui/backends/imgui_impl_osx.mm" \
  "${repo_root}/ThirdParty/cimgui/imgui/backends/imgui_impl_metal.mm" \
  -L"${native_out}" \
  -lcimgui \
  -framework Cocoa \
  -framework Foundation \
  -framework QuartzCore \
  -framework Metal \
  -framework MetalKit \
  -framework GameController \
  -framework Carbon \
  -Wl,-rpath,@loader_path \
  -o "${native_out}/libInstantTraceViewerNative.dylib"

cp "${native_out}/cimgui.dylib" "${target_dir}/cimgui.dylib"
cp "${native_out}/libcimgui.dylib" "${target_dir}/libcimgui.dylib"
cp "${native_out}/libInstantTraceViewerNative.dylib" "${target_dir}/libInstantTraceViewerNative.dylib"
