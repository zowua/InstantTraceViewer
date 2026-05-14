using System;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.Metal;
using Hexa.NET.ImGui.Backends.OSX;

namespace InstantTraceViewerUI
{
    internal static unsafe class MacImGuiHost
    {
        private const string NativeLibraryName = "InstantTraceViewerNative";

        private static nint s_view;
        private static bool s_frameBegun;
        private static bool s_osxBackendInitialized;
        private static bool s_metalBackendInitialized;

        public static ImGuiContextPtr Initialize()
        {
            if (WindowInitialize(out s_view, out nint device) != 0)
            {
                NativeWindowCleanup();
                throw new InvalidOperationException("Failed to initialize macOS ImGui host.");
            }

            ImGuiContextPtr context = default;
            try
            {
                context = ImGui.CreateContext();
                if (context.Handle == null)
                {
                    throw new InvalidOperationException("Failed to create ImGui context.");
                }

                ImGui.SetCurrentContext(context);
                ImGuiImplOSX.SetCurrentContext(context);
                ImGuiImplMetal.SetCurrentContext(context);

                nint pool = PushAutoreleasePool();
                try
                {
                    if (!ImGuiImplOSX.Init((void*)s_view))
                    {
                        throw new InvalidOperationException("Failed to initialize Hexa.NET.ImGui OSX backend.");
                    }

                    s_osxBackendInitialized = true;

                    if (!ImGuiImplMetal.Init(new MTLDevicePtr((MTLDevice*)device)))
                    {
                        throw new InvalidOperationException("Failed to initialize Hexa.NET.ImGui Metal backend.");
                    }

                    s_metalBackendInitialized = true;
                }
                finally
                {
                    PopAutoreleasePool(pool);
                }

                return context;
            }
            catch
            {
                ShutdownImGuiBackends(context);
                NativeWindowCleanup();
                s_view = 0;
                throw;
            }
        }

        public static void WindowBeginNextFrame(out bool quit, out bool occluded)
        {
            if (NativeWindowBeginNextFrame(out int nativeQuit, out int nativeOccluded, out nint renderPassDescriptor) != 0)
            {
                throw new InvalidOperationException("Failed to begin macOS ImGui frame.");
            }

            quit = nativeQuit != 0;
            occluded = nativeOccluded != 0;
            if (quit || occluded)
            {
                return;
            }

            if (renderPassDescriptor == 0)
            {
                throw new InvalidOperationException("macOS frame did not provide a Metal render pass descriptor.");
            }

            nint pool = PushAutoreleasePool();
            try
            {
                ImGuiImplMetal.NewFrame(new MTLRenderPassDescriptorPtr((MTLRenderPassDescriptor*)renderPassDescriptor));
                ImGuiImplOSX.NewFrame((void*)s_view);
                ImGui.NewFrame();
                s_frameBegun = true;
            }
            catch
            {
                NativeWindowCancelFrame();
                throw;
            }
            finally
            {
                PopAutoreleasePool(pool);
            }
        }

        public static void WindowEndNextFrame()
        {
            if (!s_frameBegun)
            {
                throw new InvalidOperationException("No macOS ImGui frame is active.");
            }

            nint pool = PushAutoreleasePool();
            try
            {
                ImGui.Render();
                s_frameBegun = false;

                if (NativeWindowBeginRender(out nint commandBuffer, out nint renderEncoder) != 0)
                {
                    NativeWindowCancelFrame();
                    throw new InvalidOperationException("Failed to begin macOS Metal render pass.");
                }

                try
                {
                    if (commandBuffer != 0 && renderEncoder != 0)
                    {
                        ImGuiImplMetal.RenderDrawData(
                            ImGui.GetDrawData(),
                            new MTLCommandBufferPtr((MTLCommandBuffer*)commandBuffer),
                            new MTLRenderCommandEncoderPtr((MTLRenderCommandEncoder*)renderEncoder));
                    }
                }
                catch
                {
                    NativeWindowCancelFrame();
                    throw;
                }

                if (NativeWindowEndRender() != 0)
                {
                    throw new InvalidOperationException("Failed to end macOS Metal render pass.");
                }
            }
            finally
            {
                PopAutoreleasePool(pool);
            }
        }

        public static void WindowCancelFrame()
        {
            s_frameBegun = false;
            NativeWindowCancelFrame();
        }

        public static void Shutdown(ImGuiContextPtr imguiContext)
        {
            WindowCancelFrame();
            ShutdownImGuiBackends(imguiContext);

            if (NativeWindowCleanup() != 0)
            {
                throw new InvalidOperationException("Failed to clean up macOS ImGui host.");
            }

            s_view = 0;
        }

        private static void ShutdownImGuiBackends(ImGuiContextPtr imguiContext)
        {
            if (imguiContext.Handle != null)
            {
                ImGui.SetCurrentContext(imguiContext);
                ImGuiImplOSX.SetCurrentContext(imguiContext);
                ImGuiImplMetal.SetCurrentContext(imguiContext);
            }

            nint pool = PushAutoreleasePool();
            try
            {
                if (s_metalBackendInitialized)
                {
                    ImGuiImplMetal.Shutdown();
                    s_metalBackendInitialized = false;
                }

                if (s_osxBackendInitialized)
                {
                    ImGuiImplOSX.Shutdown();
                    s_osxBackendInitialized = false;
                }
            }
            finally
            {
                PopAutoreleasePool(pool);
            }

            if (imguiContext.Handle != null)
            {
                ImGui.DestroyContext(imguiContext);
            }
        }

        private static nint PushAutoreleasePool()
        {
            nint pool = NativeWindowPushAutoreleasePool();
            if (pool == 0)
            {
                throw new InvalidOperationException("Failed to create macOS autorelease pool.");
            }

            return pool;
        }

        private static void PopAutoreleasePool(nint pool)
        {
            NativeWindowPopAutoreleasePool(pool);
        }

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int WindowInitialize(out nint view, out nint device);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WindowBeginNextFrame")]
        private static extern int NativeWindowBeginNextFrame(out int quit, out int occluded, out nint renderPassDescriptor);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WindowBeginRender")]
        private static extern int NativeWindowBeginRender(out nint commandBuffer, out nint renderEncoder);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WindowEndRender")]
        private static extern int NativeWindowEndRender();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WindowCancelFrame")]
        private static extern int NativeWindowCancelFrame();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WindowPushAutoreleasePool")]
        private static extern nint NativeWindowPushAutoreleasePool();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WindowPopAutoreleasePool")]
        private static extern void NativeWindowPopAutoreleasePool(nint pool);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "WindowCleanup")]
        private static extern int NativeWindowCleanup();
    }
}
