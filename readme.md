# Instant Trace Viewer

[![Windows Build](https://github.com/brycehutchings/InstantTraceViewer/actions/workflows/build-windows.yml/badge.svg)](https://github.com/brycehutchings/InstantTraceViewer/actions/workflows/build-windows.yml)

Instant Trace Viewer is a developer tool for collecting and analyzing traces and logs.
It features a straightforward interface for viewing both real-time traces and trace files,
with filtering and graphical visualizations to help you quickly understand your program's behavior during development.

![image](https://github.com/user-attachments/assets/a996841e-afad-496b-85f7-6b23bb5b5e22)

Real-time trace sources supported:
* Event Tracing for Windows (ETW)
* Android Logcat

Supported file formats:
* ETL (Event Tracing for Windows)
* Perfetto
* CSV and TSV

## Installation

You can install **Instant Trace Viewer** from the Microsoft Store or using `winget` from the command line.

### Microsoft Store

[![Install from Microsoft Store](https://get.microsoft.com/images/en-us%20dark.svg)](https://apps.microsoft.com/detail/9NWPWZGRVL2C)

Click the button above to install **Instant Trace Viewer**.

### Winget (Command Line)

If you prefer using the command line, install **Instant Trace Viewer** with Windows Package Manager (`winget`):

```sh
winget install 9NWPWZGRVL2C
```

## Cloning the Repository

To clone the repository, use the following command in your terminal:

```bash
git clone https://github.com/brycehutchings/InstantTraceViewer
```

This repository no longer requires git submodules; the Dear ImGui runtime and
backends are restored from NuGet packages during the normal .NET restore step.

## Developer Builds

These are the latest builds produced by this project's GitHub Actions pipeline.

* Download [InstantTraceViewer-x64.zip](https://nightly.link/brycehutchings/InstantTraceViewer/workflows/build-windows/main/InstantTraceViewer-x64.zip)
* Download [InstantTraceViewer-ARM64.zip](https://nightly.link/brycehutchings/InstantTraceViewer/workflows/build-windows/main/InstantTraceViewer-ARM64.zip)

## macOS Builds

macOS builds require the .NET 8 SDK and Xcode command line tools.
The Dear ImGui runtime and OSX/Metal backends are supplied by the Hexa.NET.ImGui NuGet packages.

```bash
xcode-select --install
```

To run the app directly from source:

```bash
dotnet run --project src/InstantTraceViewerUI/InstantTraceViewerUI.csproj
```

To open a Perfetto trace at launch:

```bash
dotnet run --project src/InstantTraceViewerUI/InstantTraceViewerUI.csproj -- path/to/trace.pftrace
```

To build a self-contained `.app` bundle:

```bash
scripts/build-macos-app.sh
```

By default, the script detects the host architecture:

* Apple Silicon (`arm64`) builds `osx-arm64`
* Intel (`x86_64`) builds `osx-x64`

The app bundle is written to:

```bash
artifacts/app/InstantTraceViewer.app
```

To build a specific architecture or output path:

```bash
scripts/build-macos-app.sh -r osx-arm64
scripts/build-macos-app.sh -r osx-x64
scripts/build-macos-app.sh -o /tmp/InstantTraceViewer.app
```

The script performs an ad-hoc codesign by default, which is enough for local development:

```bash
codesign --verify --deep --strict artifacts/app/InstantTraceViewer.app
open artifacts/app/InstantTraceViewer.app
```

For distribution outside your own machine, sign with a Developer ID certificate and notarize the app:

```bash
scripts/build-macos-app.sh --sign-identity "Developer ID Application: Your Name (TEAMID)"
```
