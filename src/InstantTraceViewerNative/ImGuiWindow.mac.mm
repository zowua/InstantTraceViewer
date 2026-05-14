#import <Cocoa/Cocoa.h>
#import <Metal/Metal.h>
#import <QuartzCore/CAMetalLayer.h>
#import <QuartzCore/CADisplayLink.h>

extern "C" void* objc_autoreleasePoolPush(void);
extern "C" void objc_autoreleasePoolPop(void* pool);

static constexpr CGFloat DefaultWidth = 1200.0;
static constexpr CGFloat DefaultHeight = 800.0;

static NSWindow* g_window = nil;
static NSView* g_view = nil;
static CAMetalLayer* g_metalLayer = nil;
static id<MTLDevice> g_device = nil;
static id<MTLCommandQueue> g_commandQueue = nil;
static bool g_quitRequested = false;
static bool g_hasPresentedFrame = false;
static id<CAMetalDrawable> g_currentDrawable = nil;
static MTLRenderPassDescriptor* g_currentRenderPassDescriptor = nil;
static id<MTLCommandBuffer> g_currentCommandBuffer = nil;
static id<MTLRenderCommandEncoder> g_currentRenderEncoder = nil;
static CADisplayLink* g_displayLink = nil;
static uint64_t g_displayLinkFrameCounter = 0;
static uint64_t g_consumedDisplayLinkFrameCounter = 0;

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

@interface InstantTraceDisplayLinkTarget : NSObject
- (void)displayLinkDidFire:(CADisplayLink*)displayLink;
@end

@implementation InstantTraceDisplayLinkTarget
- (void)displayLinkDidFire:(CADisplayLink*)displayLink
{
    (void)displayLink;
    ++g_displayLinkFrameCounter;
}
@end

static InstantTraceDisplayLinkTarget* g_displayLinkTarget = nil;

extern "C" int WindowCleanup() noexcept;

static int FailInitialize() noexcept
{
    WindowCleanup();
    return 1;
}

static void WaitForNextFrame()
{
    if (g_displayLink == nil)
    {
        return;
    }

    uint64_t targetFrameCounter = g_consumedDisplayLinkFrameCounter + 1;
    NSRunLoop* runLoop = NSRunLoop.currentRunLoop;
    while (g_displayLinkFrameCounter < targetFrameCounter && !g_quitRequested)
    {
        [runLoop runMode:NSDefaultRunLoopMode beforeDate:[NSDate distantFuture]];
    }

    g_consumedDisplayLinkFrameCounter = g_displayLinkFrameCounter;
}

static void ClearCurrentFrame(bool endEncoding)
{
    if (endEncoding && g_currentRenderEncoder != nil)
    {
        [g_currentRenderEncoder endEncoding];
    }

    g_currentRenderEncoder = nil;
    g_currentCommandBuffer = nil;
    g_currentDrawable = nil;
    if (g_currentRenderPassDescriptor != nil)
    {
        g_currentRenderPassDescriptor.colorAttachments[0].texture = nil;
    }
}

extern "C" void* WindowPushAutoreleasePool() noexcept
{
    return objc_autoreleasePoolPush();
}

extern "C" void WindowPopAutoreleasePool(void* pool) noexcept
{
    if (pool != nullptr)
    {
        objc_autoreleasePoolPop(pool);
    }
}

extern "C" int WindowInitialize(void** view, void** device) noexcept
{
    @autoreleasepool
    {
        *view = nullptr;
        *device = nullptr;

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
            return FailInitialize();
        }

        g_commandQueue = [g_device newCommandQueue];
        if (g_commandQueue == nil)
        {
            return FailInitialize();
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
            return FailInitialize();
        }

        g_window.title = @"Instant Trace Viewer";
        g_window.acceptsMouseMovedEvents = YES;
        g_windowDelegate = [[InstantTraceWindowDelegate alloc] init];
        g_window.delegate = g_windowDelegate;

        g_view = [[NSView alloc] initWithFrame:frame];
        if (g_view == nil)
        {
            return FailInitialize();
        }

        g_view.autoresizingMask = NSViewWidthSizable | NSViewHeightSizable;
        g_view.wantsLayer = YES;

        g_metalLayer = [CAMetalLayer layer];
        if (g_metalLayer == nil)
        {
            return FailInitialize();
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
            return FailInitialize();
        }

        g_window.contentView = g_view;
        [g_window makeFirstResponder:g_view];
        [g_window makeKeyAndOrderFront:nil];
        [NSApp activateIgnoringOtherApps:YES];

        if (@available(macOS 14.0, *))
        {
            g_displayLinkTarget = [[InstantTraceDisplayLinkTarget alloc] init];
            g_displayLink = [g_view displayLinkWithTarget:g_displayLinkTarget selector:@selector(displayLinkDidFire:)];
            if (g_displayLink == nil)
            {
                return FailInitialize();
            }

            [g_displayLink addToRunLoop:NSRunLoop.currentRunLoop forMode:NSDefaultRunLoopMode];
            [g_displayLink addToRunLoop:NSRunLoop.currentRunLoop forMode:NSEventTrackingRunLoopMode];
            [g_displayLink addToRunLoop:NSRunLoop.currentRunLoop forMode:NSModalPanelRunLoopMode];
        }
        else
        {
            g_displayLink = nil;
        }

        *view = (__bridge void*)g_view;
        *device = (__bridge void*)g_device;
        return 0;
    }
}

extern "C" int WindowBeginNextFrame(int* quit, int* occluded, void** renderPassDescriptor) noexcept
{
    @autoreleasepool
    {
        *quit = 0;
        *occluded = 0;
        *renderPassDescriptor = nullptr;

        PumpPendingEvents();

        if (g_quitRequested)
        {
            *quit = 1;
            return 0;
        }

        if (g_window == nil || g_view == nil || g_metalLayer == nil || g_currentRenderPassDescriptor == nil)
        {
            return 1;
        }

        CGFloat framebufferScale = g_window.screen != nil ? g_window.screen.backingScaleFactor : NSScreen.mainScreen.backingScaleFactor;
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

        if (g_hasPresentedFrame)
        {
            WaitForNextFrame();
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

        *renderPassDescriptor = (__bridge void*)g_currentRenderPassDescriptor;
        return 0;
    }
}

extern "C" int WindowBeginRender(void** commandBuffer, void** renderEncoder) noexcept
{
    @autoreleasepool
    {
        *commandBuffer = nullptr;
        *renderEncoder = nullptr;

        if (g_currentDrawable == nil || g_currentRenderPassDescriptor == nil)
        {
            return 0;
        }

        g_currentCommandBuffer = [g_commandQueue commandBuffer];
        if (g_currentCommandBuffer == nil)
        {
            ClearCurrentFrame(false);
            return 1;
        }

        g_currentRenderEncoder = [g_currentCommandBuffer renderCommandEncoderWithDescriptor:g_currentRenderPassDescriptor];
        if (g_currentRenderEncoder == nil)
        {
            ClearCurrentFrame(false);
            return 1;
        }

        *commandBuffer = (__bridge void*)g_currentCommandBuffer;
        *renderEncoder = (__bridge void*)g_currentRenderEncoder;
        return 0;
    }
}

extern "C" int WindowEndRender() noexcept
{
    @autoreleasepool
    {
        if (g_currentRenderEncoder != nil)
        {
            [g_currentRenderEncoder endEncoding];
        }

        if (g_currentCommandBuffer != nil && g_currentDrawable != nil)
        {
            [g_currentCommandBuffer presentDrawable:g_currentDrawable];
            [g_currentCommandBuffer commit];
            g_hasPresentedFrame = true;
        }

        ClearCurrentFrame(false);
        return 0;
    }
}

extern "C" int WindowCancelFrame() noexcept
{
    @autoreleasepool
    {
        ClearCurrentFrame(true);
        return 0;
    }
}

extern "C" int WindowCleanup() noexcept
{
    @autoreleasepool
    {
        if (g_window != nil)
        {
            [g_window orderOut:nil];
            g_window.delegate = nil;
        }

        if (g_displayLink != nil)
        {
            [g_displayLink invalidate];
        }

        ClearCurrentFrame(false);
        g_currentRenderPassDescriptor = nil;
        g_displayLink = nil;
        g_displayLinkTarget = nil;
        g_displayLinkFrameCounter = 0;
        g_consumedDisplayLinkFrameCounter = 0;
        g_windowDelegate = nil;
        g_metalLayer = nil;
        g_view = nil;
        g_window = nil;
        g_commandQueue = nil;
        g_device = nil;
        g_quitRequested = false;
        g_hasPresentedFrame = false;
        return 0;
    }
}
