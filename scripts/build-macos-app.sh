#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"

configuration="Release"
runtime_identifier=""
app_path="${repo_root}/artifacts/app/InstantTraceViewer.app"
sign_identity="-"

usage() {
  cat <<'USAGE'
Build a self-contained macOS .app bundle.

Usage:
  scripts/build-macos-app.sh [options]

Options:
  -c, --configuration <Debug|Release>   Build configuration. Default: Release
  -r, --runtime <osx-arm64|osx-x64>     macOS runtime identifier. Default: host architecture
  -o, --output <path.app>               Output app bundle path. Default: artifacts/app/InstantTraceViewer.app
      --sign-identity <identity>        codesign identity. Default: - (ad-hoc)
      --no-sign                         Do not codesign the app bundle
  -h, --help                            Show this help
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -c|--configuration)
      configuration="${2:?missing value for $1}"
      shift 2
      ;;
    -r|--runtime)
      runtime_identifier="${2:?missing value for $1}"
      shift 2
      ;;
    -o|--output)
      app_path="${2:?missing value for $1}"
      shift 2
      ;;
    --sign-identity)
      sign_identity="${2:?missing value for $1}"
      shift 2
      ;;
    --no-sign)
      sign_identity=""
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ -z "${runtime_identifier}" ]]; then
  case "$(uname -m)" in
    arm64) runtime_identifier="osx-arm64" ;;
    x86_64) runtime_identifier="osx-x64" ;;
    *) echo "Unsupported macOS host architecture: $(uname -m)" >&2; exit 1 ;;
  esac
fi

case "${runtime_identifier}" in
  osx-arm64|osx-x64) ;;
  *) echo "Unsupported macOS runtime identifier: ${runtime_identifier}" >&2; exit 1 ;;
esac

project_path="${repo_root}/src/InstantTraceViewerUI/InstantTraceViewerUI.csproj"
target_framework="net8.0"
publish_dir="${repo_root}/artifacts/publish/${runtime_identifier}"
contents_dir="${app_path}/Contents"
macos_dir="${contents_dir}/MacOS"
resources_dir="${contents_dir}/Resources"
native_build_dir="${repo_root}/src/InstantTraceViewerUI/bin/${configuration}/${target_framework}/${runtime_identifier}"

rm -rf "${publish_dir}" "${app_path}"
mkdir -p "${publish_dir}" "${macos_dir}" "${resources_dir}"

dotnet publish "${project_path}" \
  -c "${configuration}" \
  -r "${runtime_identifier}" \
  --self-contained true \
  -o "${publish_dir}"

rsync -a "${publish_dir}/" "${macos_dir}/"

if [[ -f "${native_build_dir}/libInstantTraceViewerNative.dylib" ]]; then
  cp "${native_build_dir}/libInstantTraceViewerNative.dylib" "${macos_dir}/libInstantTraceViewerNative.dylib"
fi

if [[ ! -f "${macos_dir}/libInstantTraceViewerNative.dylib" ]]; then
  echo "Missing libInstantTraceViewerNative.dylib in ${macos_dir}" >&2
  exit 1
fi

cat > "${contents_dir}/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>CFBundleName</key>
    <string>Instant Trace Viewer</string>
    <key>CFBundleDisplayName</key>
    <string>Instant Trace Viewer</string>
    <key>CFBundleIdentifier</key>
    <string>com.instanttraceviewer.app</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>InstantTraceViewerUI</string>
    <key>CFBundleSupportedPlatforms</key>
    <array>
      <string>MacOSX</string>
    </array>
    <key>NSHighResolutionCapable</key>
    <true/>
  </dict>
</plist>
PLIST

chmod +x "${macos_dir}/InstantTraceViewerUI"

if [[ -n "${sign_identity}" ]]; then
  codesign --force --deep --sign "${sign_identity}" "${app_path}"
fi

echo "Built ${app_path}"
