using System;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;

namespace InstantTraceViewerUI
{
    internal static unsafe class MacImGuiHost
    {
        public static ImGuiContextPtr Initialize()
        {
            if (WindowInitialize(out nint rawContext) != 0)
            {
                throw new InvalidOperationException("Failed to initialize macOS ImGui host.");
            }

            ImGuiContextPtr imguiContext = new((ImGuiContext*)rawContext);
            ImGui.SetCurrentContext(imguiContext);
            return imguiContext;
        }

        public static void WindowBeginNextFrame(out bool quit, out bool occluded)
        {
            if (NativeWindowBeginNextFrame(out int nativeQuit, out int nativeOccluded) != 0)
            {
                throw new InvalidOperationException("Failed to begin macOS ImGui frame.");
            }

            quit = nativeQuit != 0;
            occluded = nativeOccluded != 0;
        }

        public static void WindowEndNextFrame()
        {
            if (NativeWindowEndNextFrame() != 0)
            {
                throw new InvalidOperationException("Failed to end macOS ImGui frame.");
            }
        }

        public static void WindowCleanup()
        {
            if (NativeWindowCleanup() != 0)
            {
                throw new InvalidOperationException("Failed to clean up macOS ImGui host.");
            }
        }

        [DllImport("InstantTraceViewerNative", CallingConvention = CallingConvention.Winapi)]
        private static extern int WindowInitialize(out nint imguiContext);

        [DllImport("InstantTraceViewerNative", CallingConvention = CallingConvention.Winapi, EntryPoint = "WindowBeginNextFrame")]
        private static extern int NativeWindowBeginNextFrame(out int quit, out int occluded);

        [DllImport("InstantTraceViewerNative", CallingConvention = CallingConvention.Winapi, EntryPoint = "WindowEndNextFrame")]
        private static extern int NativeWindowEndNextFrame();

        [DllImport("InstantTraceViewerNative", CallingConvention = CallingConvention.Winapi, EntryPoint = "WindowCleanup")]
        private static extern int NativeWindowCleanup();
    }
}
