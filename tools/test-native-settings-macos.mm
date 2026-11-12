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
        std::fprintf(stderr, "AppKit checkpoint: before NSApplication\n");
        [NSApplication sharedApplication];
        std::fprintf(stderr, "AppKit checkpoint: after NSApplication\n");
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

        std::fprintf(stderr, "AppKit checkpoint: before Create\n");
        create();
        std::fprintf(stderr, "AppKit checkpoint: after Create\n");
        NSWindow *window = settingsWindow();
        require(window != nil, "real NSWindow was not created");
        require((window.styleMask & NSWindowStyleMaskTitled) != 0,
                "macOS Settings must keep system titlebar");
        require((window.styleMask & NSWindowStyleMaskResizable) != 0,
                "macOS Settings must remain independently resizable");
        require(visible() == 0, "new Settings should initially be hidden");
        NSScrollView *scroll = detailScroll(window);
        require(scroll != nil, "native Settings detail ScrollView missing");

        std::fprintf(stderr, "AppKit checkpoint: before first Add\n");
        add(0, 45, 2, "한글 경로", "한글 값");
        std::fprintf(stderr, "AppKit checkpoint: after first Add\n");
        char buffer[1024] = {};
        getText(45, buffer, sizeof(buffer));
        require(std::strcmp(buffer, "한글 값") == 0, "initial Korean UTF-8 lost");
        setText(45, "수정된 값 ✓");
        getText(45, buffer, sizeof(buffer));
        require(std::strcmp(buffer, "수정된 값 ✓") == 0,
                "updated Korean UTF-8 field lost");

        // Check dynamic page rows and cleanup. Without rebuilding page geometry,
        // the test would miss a stale counter that survives window recreation.
        std::fprintf(stderr, "AppKit checkpoint: before Clear\n");
        clear(0);
        std::fprintf(stderr, "AppKit checkpoint: after Clear\n");
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

        std::fprintf(stderr, "AppKit checkpoint: before Destroy/recreate\n");
        destroy();
        create();
        std::fprintf(stderr, "AppKit checkpoint: after Destroy/recreate\n");
        window = settingsWindow();
        require(window != nil, "Settings failed to recreate after Destroy");
        scroll = detailScroll(window);
        require(scroll != nil, "recreated Settings lost ScrollView");
        add(0, 900, 0, "재생성됨", "");
        select(0);
        require(scroll.documentView != nil, "recreated page was not attached");
        require(std::fabs(NSHeight(scroll.documentView.bounds) - 650.0) < 1.0,
                "stale row count survived Settings Destroy/Create");
        destroy();
        dlclose(library);
        std::puts("P1 macOS AppKit bridge native integration: PASS (no Unity GUI acceptance)");
    }
    return 0;
}
