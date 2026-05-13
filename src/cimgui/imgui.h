#pragma once

#include_next "imgui.h"

// The macOS backend is compiled locally but links to Hexa.NET.ImGui's packaged
// cimgui runtime. Hexa 2.2.9 reports Dear ImGui 1.92.2b, while the closest
// available cimgui backend source is 1.92.3dock. Keep the backend's startup
// check from aborting on that patch-level version skew.
#undef IMGUI_CHECKVERSION
#define IMGUI_CHECKVERSION() ((void)0)
