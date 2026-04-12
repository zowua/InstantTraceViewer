using ImGuiNET;
using System.Runtime.InteropServices;

namespace InstantTraceViewerUI
{
    internal static class NativeInterop
    {
        private const string NativeLibraryName = "InstantTraceViewerNative";

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Winapi)]
        public static extern int WindowInitialize(out nint imguiContext);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Winapi)]
        public static extern int WindowBeginNextFrame(out int quit, out int occluded);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Winapi)]
        public static extern int WindowEndNextFrame();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Winapi)]
        public static extern int WindowCleanup();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Winapi)]
        public static extern void RebuildFontAtlas();
    }

    internal static class ImGuiInternal
    {
        [DllImport("cimgui", EntryPoint = "igTableSetColumnSortDirection", CallingConvention = CallingConvention.Cdecl)]
        public static extern void TableSetColumnSortDirection(int column_n, ImGuiSortDirection sort_direction, bool append_to_sort_specs);
    }
}
