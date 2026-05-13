using System;
using Hexa.NET.ImGui;

namespace InstantTraceViewerUI
{
    internal static unsafe class ImGuiHost
    {
        public static bool SupportsViewports => OperatingSystem.IsWindows();

        public static ImGuiContextPtr Initialize()
        {
#if WINDOWS
            if (OperatingSystem.IsWindows())
            {
                Win32ImGuiHost.WindowInitialize();

                ImGuiContextPtr imguiContext = ImGui.CreateContext();
                ImGui.SetCurrentContext(imguiContext);
                Win32ImGuiHost.InitializeImGuiBackends(imguiContext);
                return imguiContext;
            }
#endif

            if (OperatingSystem.IsMacOS())
            {
                return MacImGuiHost.Initialize();
            }

            throw new PlatformNotSupportedException("Instant Trace Viewer supports Windows and macOS.");
        }

        public static float GetDpiScale()
        {
#if WINDOWS
            if (OperatingSystem.IsWindows())
            {
                return Win32ImGuiHost.GetDpiScale();
            }
#endif

            // Cocoa exposes window and view sizes in logical points, so applying monitor scale
            // to ImGui style sizes makes the UI too large on Retina displays.
            return 1.0f;
        }

        public static void WindowBeginNextFrame(out bool quit, out bool occluded)
        {
#if WINDOWS
            if (OperatingSystem.IsWindows())
            {
                Win32ImGuiHost.WindowBeginNextFrame(out quit, out occluded);
                return;
            }
#endif

            MacImGuiHost.WindowBeginNextFrame(out quit, out occluded);
        }

        public static void WindowEndNextFrame()
        {
#if WINDOWS
            if (OperatingSystem.IsWindows())
            {
                Win32ImGuiHost.WindowEndNextFrame();
                return;
            }
#endif

            MacImGuiHost.WindowEndNextFrame();
        }

        public static void Shutdown(ImGuiContextPtr imguiContext)
        {
#if WINDOWS
            if (OperatingSystem.IsWindows())
            {
                Win32ImGuiHost.ShutdownImGuiBackends();
                ImGui.DestroyContext(imguiContext);
                Win32ImGuiHost.WindowCleanup();
                return;
            }
#endif

            MacImGuiHost.WindowCleanup();
        }
    }
}
