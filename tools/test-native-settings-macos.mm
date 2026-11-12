// Actual AppKit bridge integration test. Executes the compiled native .dylib
// on a macOS runner; does not imply Unity rendering or human UI acceptance.
#import <AppKit/AppKit.h>
#include <dlfcn.h>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace {
void require(bool condition, const char *message) {
    if (!condition) {
        std::fprintf(stderr, "P1 AppKit bridge FAIL: %s\n", message);
        std::exit(1);
    }
}
template <typename T> T symbol(void *library, const char *name) {
    void *pointer = dlsym(library, name);
    require(pointer != nullptr, name);
    return reinterpret_cast<T>(pointer);
}
NSWindow *settingsWindow() {
    for (NSWindow *window in [NSApp windows]) {
        if ([window.title hasPrefix:@"VirtualCharacterRender -"]) return window;
    }
    return nil;
}
NSScrollView *detailScroll(NSWindow *window) {
    for (NSView *view in window.contentView.subviews) {
        if ([view isKindOfClass:[NSScrollView class]])
            return (NSScrollView *)view;
    }
    return nil;
}
}

int main(int argc, const char *argv[]) {
    @autoreleasepool {
        require(argc == 2, "usage: test-native-settings-macos <dylib>");
        [NSApplication sharedApplication];
        void *library = dlopen(argv[1], RTLD_NOW | RTLD_LOCAL);
        if (!library) {
            std::fprintf(stderr, "dlopen failed: %s\n", dlerror());
            return 1;
        }
        auto create = symbol<void (*)()>(library, "VCR_SettingsCreate");
        auto destroy = symbol<void (*)()>(library, "VCR_SettingsDestroy");
        auto add = symbol<void (*)(int, int, int, const char *, const char *)>(
            library, "VCR_SettingsAddControl");
        auto clear = symbol<void (*)(int)>(library, "VCR_SettingsClearCategory");
        auto select = symbol<void (*)(int)>(library, "VCR_SettingsSelectCategory");
        auto setText = symbol<void (*)(int, const char *)>(library, "VCR_SettingsSetText");
        auto getText = symbol<void (*)(int, char *, int)>(library, "VCR_SettingsGetText");
        auto visible = symbol<int (*)()>(library, "VCR_SettingsIsVisible");
        auto poll = symbol<int (*)()>(library, "VCR_SettingsPollAction");

        create();
        NSWindow *window = settingsWindow();
        require(window != nil, "real NSWindow was not created");
        require((window.styleMask & NSWindowStyleMaskTitled) != 0,
                "macOS Settings must keep system titlebar");
        require((window.styleMask & NSWindowStyleMaskResizable) != 0,
                "macOS Settings must remain independently resizable");
        require(![window isReleasedWhenClosed],
                "native NSWindow must disable automatic close-release under ARC");
        require(visible() == 0, "new Settings should initially be hidden");
        NSScrollView *scroll = detailScroll(window);
        require(scroll != nil, "native Settings detail ScrollView missing");

        add(0, 45, 2, "한글 경로", "한글 값");
        char buffer[1024] = {};
        getText(45, buffer, sizeof(buffer));
        require(std::strcmp(buffer, "한글 값") == 0, "initial Korean UTF-8 lost");
        setText(45, "수정된 값 ✓");
        getText(45, buffer, sizeof(buffer));
        require(std::strcmp(buffer, "수정된 값 ✓") == 0,
                "updated Korean UTF-8 field lost");

        // Check dynamic page rows and cleanup. Without rebuilding page geometry,
        // the test would miss a stale counter that survives window recreation.
        clear(0);
        for (int i = 0; i < 17; ++i)
            add(0, 500 + i, 1, "Test action", "");
        select(0);
        require(scroll.documentView != nil, "filled page not attached");
        const CGFloat expanded = NSHeight(scroll.documentView.bounds);
        require(expanded > 650.0, "page did not expand after 17 controls");
        select(2);
        add(2, 800, 0, "추적", "");
        require(scroll.documentView != nil, "tracking page not attached");
        select(0);
        require(std::fabs(NSHeight(scroll.documentView.bounds) - expanded) < 1.0,
                "category navigation lost page layout");
        require(poll() == 0, "programmatic category selection queued a click");
        // Exercise an actual AppKit NSButton target/action path. The native
        // sidebar must dispatch the same navigation ID to managed Unity.
        NSButton *outputNavigation = nil;
        for (NSView *pane in window.contentView.subviews) {
            for (NSView *control in pane.subviews) {
                if ([control isKindOfClass:[NSButton class]] &&
                    [(NSButton *)control tag] == 103) {
                    outputNavigation = (NSButton *)control;
                    break;
                }
            }
        }
        require(outputNavigation != nil, "native Output navigation missing");
        [outputNavigation performClick:nil];
        require(poll() == 103 && poll() == 0,
                "native sidebar click failed to queue exactly one action");

        NSWindow *oldWindow = window;
        destroy();
        create();
        // AppKit's NSApp.windows may still include a closed autoreleased
        // window until the next event/autorelease drain. Do not inspect
        // the old window when verifying the newly created native bridge.
        window = nil;
        for (NSWindow *candidate in [NSApp windows]) {
            if (candidate != oldWindow &&
                [candidate.title hasPrefix:@"VirtualCharacterRender -"]) {
                window = candidate;
                break;
            }
        }
        require(window != nil, "Settings failed to recreate a distinct NSWindow");
        scroll = detailScroll(window);
        require(scroll != nil, "recreated Settings lost ScrollView");
        add(0, 900, 0, "재생성됨", "");
        select(0);
        require(scroll.documentView != nil, "recreated page was not attached");
        const CGFloat recreated = NSHeight(scroll.documentView.bounds);
        std::fprintf(stderr, "AppKit page height: filled=%.1f recreated=%.1f\n",
                     (double)expanded, (double)recreated);
        // AppKit may grow a 650pt document to fill a taller scroll viewport,
        // so absolute equality to 650pt is not a valid lifecycle assertion.
        // A single-row fresh page must be substantially shorter than the
        // 17-row page from the previous window instance.
        require(recreated >= 650.0 && recreated < expanded - 100.0,
                "stale row count survived Settings Destroy/Create");
        destroy();
        // Objective-C classes registered by a dynamically loaded dylib must
        // not be unloaded while the AppKit process is alive. Unity also keeps
        // native plugin modules loaded for the lifetime of the Player.
        std::puts("P1 macOS AppKit bridge native integration: PASS (no Unity GUI acceptance)");
    }
    return 0;
}
