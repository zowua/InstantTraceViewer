#import <Cocoa/Cocoa.h>
#import <Metal/Metal.h>
#import <QuartzCore/CAMetalLayer.h>

#include <chrono>
#include <thread>

#include "imgui.h"
#include "backends/imgui_impl_metal.h"
#include "backends/imgui_impl_osx.h"

static constexpr CGFloat DefaultWidth = 1200.0;
static constexpr CGFloat DefaultHeight = 800.0;
static constexpr auto TargetFrameDuration = std::chrono::milliseconds(16);

static NSWindow* g_window = nil;
static NSView* g_view = nil;
static CAMetalLayer* g_metalLayer = nil;
static id<MTLDevice> g_device = nil;
static id<MTLCommandQueue> g_commandQueue = nil;
static bool g_quitRequested = false;
static bool g_hasPresentedFrame = false;
static id<CAMetalDrawable> g_currentDrawable = nil;
static MTLRenderPassDescriptor* g_currentRenderPassDescriptor = nil;
static std::chrono::steady_clock::time_point g_nextFrameTime;

@interface InstantTraceWindowDelegate : NSObject <NSWindowDelegate>
@end

@implementation InstantTraceWindowDelegate
- (void)windowWillClose:(NSNotification*)notification
{
    g_quitRequested = true;
}
@end

static InstantTraceWindowDelegate* g_windowDelegate = nil;

static void PumpPendingEventsForMode(NSString* mode)
{
    NSEvent* event = nil;
    do
    {
        event = [NSApp nextEventMatchingMask:NSEventMaskAny
                                   untilDate:[NSDate distantPast]
                                      inMode:mode
                                     dequeue:YES];
        if (event != nil)
        {
            [NSApp sendEvent:event];
        }
    } while (event != nil);
}

static void PumpPendingEvents()
{
    PumpPendingEventsForMode(NSDefaultRunLoopMode);
    PumpPendingEventsForMode(NSEventTrackingRunLoopMode);
    PumpPendingEventsForMode(NSModalPanelRunLoopMode);
    [NSApp updateWindows];
}

static void WaitForNextFrame()
{
    auto now = std::chrono::steady_clock::now();
    if (g_nextFrameTime > now)
    {
        std::this_thread::sleep_until(g_nextFrameTime);
    }

    g_nextFrameTime = std::chrono::steady_clock::now() + TargetFrameDuration;
}

extern "C" int WindowInitialize(ImGuiContext** imguiContext) noexcept
{
    @autoreleasepool
    {
        if (g_window != nil)
        {
            return 1;
        }

        [NSApplication sharedApplication];
        [NSApp setActivationPolicy:NSApplicationActivationPolicyRegular];
        [NSApp finishLaunching];

        g_device = MTLCreateSystemDefaultDevice();
        if (g_device == nil)
        {
            return 1;
        }

        g_commandQueue = [g_device newCommandQueue];
        if (g_commandQueue == nil)
        {
            return 1;
        }

        NSRect frame = NSMakeRect(100.0, 100.0, DefaultWidth, DefaultHeight);
        NSUInteger styleMask =
            NSWindowStyleMaskTitled |
            NSWindowStyleMaskClosable |
            NSWindowStyleMaskMiniaturizable |
            NSWindowStyleMaskResizable;

        g_window = [[NSWindow alloc] initWithContentRect:frame
                                               styleMask:styleMask
                                                 backing:NSBackingStoreBuffered
                                                   defer:NO];
        if (g_window == nil)
        {
            return 1;
        }

        g_window.title = @"Instant Trace Viewer";
        g_window.acceptsMouseMovedEvents = YES;
        g_windowDelegate = [[InstantTraceWindowDelegate alloc] init];
        g_window.delegate = g_windowDelegate;

        g_view = [[NSView alloc] initWithFrame:frame];
        if (g_view == nil)
        {
            return 1;
        }

        g_view.autoresizingMask = NSViewWidthSizable | NSViewHeightSizable;
        g_view.wantsLayer = YES;

        g_metalLayer = [CAMetalLayer layer];
        if (g_metalLayer == nil)
        {
            return 1;
        }

        g_metalLayer.device = g_device;
        g_metalLayer.pixelFormat = MTLPixelFormatBGRA8Unorm;
        g_metalLayer.framebufferOnly = YES;
        g_metalLayer.opaque = YES;
        g_metalLayer.frame = g_view.bounds;
        g_view.layer = g_metalLayer;

        g_currentRenderPassDescriptor = [MTLRenderPassDescriptor new];
        if (g_currentRenderPassDescriptor == nil)
        {
            return 1;
        }

        g_window.contentView = g_view;
        [g_window makeFirstResponder:g_view];
        [g_window makeKeyAndOrderFront:nil];
        [NSApp activateIgnoringOtherApps:YES];

        IMGUI_CHECKVERSION();
        *imguiContext = ImGui::CreateContext();
        ImGuiIO& io = ImGui::GetIO();
        io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;
        io.ConfigFlags |= ImGuiConfigFlags_NavEnableGamepad;
        io.ConfigFlags |= ImGuiConfigFlags_DockingEnable;

        ImGui_ImplOSX_Init(g_view);
        ImGui_ImplMetal_Init(g_device);

        g_nextFrameTime = std::chrono::steady_clock::now();
        return 0;
    }
}

extern "C" int WindowBeginNextFrame(int* quit, int* occluded) noexcept
{
    @autoreleasepool
    {
        *quit = 0;
        *occluded = 0;

        PumpPendingEvents();

        if (g_quitRequested)
        {
            *quit = 1;
            return 0;
        }

        if (g_window == nil || g_view == nil || g_metalLayer == nil)
        {
            return 1;
        }

        WaitForNextFrame();

        ImGuiIO& io = ImGui::GetIO();
        io.DisplaySize = ImVec2((float)g_view.bounds.size.width, (float)g_view.bounds.size.height);

        CGFloat framebufferScale = g_window.screen != nil ? g_window.screen.backingScaleFactor : NSScreen.mainScreen.backingScaleFactor;
        io.DisplayFramebufferScale = ImVec2((float)framebufferScale, (float)framebufferScale);

        if (g_view.bounds.size.width <= 0.0 || g_view.bounds.size.height <= 0.0 || g_window.isMiniaturized)
        {
            *occluded = 1;
            return 0;
        }

        if (!g_hasPresentedFrame && (g_window.occlusionState & NSWindowOcclusionStateVisible) == 0)
        {
            // Allow the first frame through so the window can appear, then avoid blocking on drawable acquisition while occluded.
        }
        else if ((g_window.occlusionState & NSWindowOcclusionStateVisible) == 0)
        {
            *occluded = 1;
            return 0;
        }

        g_metalLayer.contentsScale = framebufferScale;
        g_metalLayer.frame = g_view.bounds;
        g_metalLayer.drawableSize = CGSizeMake(g_view.bounds.size.width * framebufferScale, g_view.bounds.size.height * framebufferScale);

        g_currentDrawable = [g_metalLayer nextDrawable];
        if (g_currentDrawable == nil)
        {
            *occluded = 1;
            return 0;
        }

        g_currentRenderPassDescriptor.colorAttachments[0].texture = g_currentDrawable.texture;
        g_currentRenderPassDescriptor.colorAttachments[0].clearColor = MTLClearColorMake(0.45, 0.55, 0.60, 1.0);
        g_currentRenderPassDescriptor.colorAttachments[0].loadAction = MTLLoadActionClear;
        g_currentRenderPassDescriptor.colorAttachments[0].storeAction = MTLStoreActionStore;

        ImGui_ImplMetal_NewFrame(g_currentRenderPassDescriptor);
        ImGui_ImplOSX_NewFrame(g_view);
        ImGui::NewFrame();

        return 0;
    }
}

extern "C" int WindowEndNextFrame() noexcept
{
    @autoreleasepool
    {
        if (g_currentDrawable == nil || g_currentRenderPassDescriptor == nil)
        {
            return 0;
        }

        ImGui::Render();

        id<MTLCommandBuffer> commandBuffer = [g_commandQueue commandBuffer];
        if (commandBuffer == nil)
        {
            return 1;
        }

        id<MTLRenderCommandEncoder> renderEncoder = [commandBuffer renderCommandEncoderWithDescriptor:g_currentRenderPassDescriptor];
        if (renderEncoder == nil)
        {
            return 1;
        }

        ImGui_ImplMetal_RenderDrawData(ImGui::GetDrawData(), commandBuffer, renderEncoder);
        [renderEncoder endEncoding];
        [commandBuffer presentDrawable:g_currentDrawable];
        [commandBuffer commit];

        g_hasPresentedFrame = true;
        g_currentDrawable = nil;
        g_currentRenderPassDescriptor.colorAttachments[0].texture = nil;
        return 0;
    }
}

extern "C" int WindowCleanup() noexcept
{
    @autoreleasepool
    {
        ImGui_ImplMetal_Shutdown();
        ImGui_ImplOSX_Shutdown();
        ImGui::DestroyContext();

        [g_window orderOut:nil];
        g_window.delegate = nil;

        g_currentDrawable = nil;
        g_currentRenderPassDescriptor = nil;
        g_windowDelegate = nil;
        g_metalLayer = nil;
        g_view = nil;
        g_window = nil;
        g_commandQueue = nil;
        g_device = nil;
        g_quitRequested = false;
        g_hasPresentedFrame = false;
        g_nextFrameTime = std::chrono::steady_clock::time_point{};
        return 0;
    }
}

extern "C" void RebuildFontAtlas() noexcept
{
    @autoreleasepool
    {
        if (g_device != nil)
        {
            ImGui_ImplMetal_DestroyFontsTexture();
            ImGui_ImplMetal_CreateFontsTexture(g_device);
        }
    }
}
