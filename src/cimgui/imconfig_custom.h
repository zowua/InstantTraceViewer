#pragma once

#if defined(_WIN32)
#define IMGUI_API __declspec(dllexport)
#else
#define IMGUI_API __attribute__((visibility("default")))
#endif

// Hexa.NET.ImGui's Windows cimgui runtime exposes FreeType. The macOS path uses
// the packaged runtime as-is and rasterizes with stb_truetype.
#if defined(_WIN32)
#define IMGUI_ENABLE_FREETYPE
#endif

// Keep stb_truetype enabled even when FreeType is available; cimgui expects it.
#define IMGUI_ENABLE_STB_TRUETYPE
