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
        auto show = symbol<void (*)()>(library, "VCR_SettingsShow");
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

        // Exercise an actual native Settings window's visibility lifecycle.
        // In particular, the red close control must hide, not release, this
        // independent NSWindow. Opening it again must retain the same object.
        show();
        require(visible() == 1 && window.isVisible,
                "native Settings show did not make the NSWindow visible");
        [window performClose:nil];
        require(visible() == 0 && !window.isVisible,
                "native Settings close should hide, not terminate or destroy");
        show();
        require(visible() == 1 && window.isVisible && settingsWindow() == window,
                "native Settings reopen failed to reuse existing window");

        // The content area must stay resizable without a fixed aspect ratio.
        // Stay within a small hosted Mac display's visible frame. The
        // default 940x720pt content plus system caption is already near its
        // screen height; demanding 1024x760 exceeds a 1024x768 display.
        [window setContentSize:NSMakeSize(960, 580)];
        const NSSize resized = window.contentView.bounds.size;
        std::fprintf(stderr, "AppKit resized content: %.1fx%.1f pt\n",
                     (double)resized.width, (double)resized.height);
        require(std::fabs(resized.width - 960.0) < 2.0 &&
                std::fabs(resized.height - 580.0) < 2.0,
                "native Settings window failed independent width/height resize");
        require(NSWidth(scroll.frame) > 678.0,
                "native Settings detail pane did not resize with its window");

        add(0, 45, 2, "한글 경로", "한글 값");
        char buffer[1024] = {};
        getText(45, buffer, sizeof(buffer));
        require(std::strcmp(buffer, "한글 값") == 0, "initial Korean UTF-8 lost");
        setText(45, "수정된 값 ✓");
        getText(45, buffer, sizeof(buffer));
        require(std::strcmp(buffer, "수정된 값 ✓") == 0,
                "updated Korean UTF-8 field lost");
        // A caller may provide a short buffer for a Korean path. Every
        // capacity must yield valid UTF-8, a terminating NUL and no writes
        // past its declared boundary. Reject partially copied code points.
        setText(45, "가나A✓");
        const char *fullText = "가나A✓";
        for (int capacity = 1; capacity <= 20; ++capacity) {
            char bounded[24];
            std::memset(bounded, 0x5a, sizeof(bounded));
            getText(45, bounded, capacity);
            const size_t bytes = strnlen(bounded, static_cast<size_t>(capacity));
            require(bytes < static_cast<size_t>(capacity),
                    "bounded text result missing NUL terminator");
            for (size_t index = static_cast<size_t>(capacity);
                 index < sizeof(bounded); ++index) {
                require(bounded[index] == 0x5a,
                        "native GetText overwrote bytes beyond capacity");
            }
            NSString *decoded = [[NSString alloc]
                initWithBytes:bounded length:bytes
                    encoding:NSUTF8StringEncoding];
            require(decoded != nil, "bounded Korean text split a UTF-8 codepoint");
            require(std::strncmp(bounded, fullText, bytes) == 0,
                    "bounded text is not a complete prefix");
            if (capacity >= static_cast<int>(std::strlen(fullText)) + 1)
                require(std::strcmp(bounded, fullText) == 0,
                        "full Korean text not returned at sufficient capacity");
        }
        char missing[3] = {'x', 'x', 'x'};
        getText(9999, missing, sizeof(missing));
        require(missing[0] == 0, "missing native Settings text must be empty");

        // Both real General labels exist in the U1 workspace:
        // 90 = action feedback, 94 = configuration path / recovery status.
        // Older tests measured only synthetic 94 or only legacy 90.
        add(0, 89, 0, "General status heading", "");
        add(0, 90, 0, "Action feedback ready.", "");
        add(0, 94, 0, "Configuration status unavailable.", "");
        select(0);
        NSTextField *headingText = nil;
        NSTextField *feedbackText = nil;
        NSTextField *configurationText = nil;
        for (NSView *view in scroll.documentView.subviews) {
            if (![view isKindOfClass:[NSTextField class]]) continue;
            if (view.tag == 89) headingText = (NSTextField *)view;
            if (view.tag == 90) feedbackText = (NSTextField *)view;
            if (view.tag == 94) configurationText = (NSTextField *)view;
        }
        require(headingText != nil && feedbackText != nil &&
                configurationText != nil, "both actual General status rows missing");
        for (NSTextField *field in @[feedbackText, configurationText]) {
            require([(NSTextFieldCell *)field.cell wraps] &&
                    !field.usesSingleLineMode && field.isSelectable,
                    "General status must support multiline wrapping and selection");
        }
        const CGFloat shortFeedbackHeight = NSHeight(feedbackText.frame);
        const CGFloat shortConfigHeight = NSHeight(configurationText.frame);
        const CGFloat shortDocumentHeight = NSHeight(scroll.documentView.bounds);
        NSString *warning = [@"복구 설정 경로: " stringByPaddingToLength:3600
            withString:@"/아주긴폴더/백업경로/경고메시지/ " startingAtIndex:0];

        // Legacy/U1 action feedback 90 can also be a long diagnostic.
        setText(90, warning.UTF8String);
        require(NSHeight(feedbackText.frame) > shortFeedbackHeight + 100,
                "long action feedback 90 is clipped");
        require(NSHeight(scroll.documentView.bounds) > shortDocumentHeight + 100,
                "long action feedback 90 did not expand scroll document");
        require(NSMaxY(feedbackText.frame) + 8 <= NSMinY(headingText.frame),
                "long action feedback 90 overlaps heading");
        require([feedbackText.stringValue isEqualToString:warning] &&
                [feedbackText.toolTip isEqualToString:warning],
                "action feedback 90 lost full warning text");
        setText(90, "Action feedback ready.");

        // U1 control 94 is the actual GetWorkspaceConfigurationStatus() value.
        // Test it independently of action feedback 90.
        setText(94, warning.UTF8String);
        const CGFloat expandedConfigHeight = NSHeight(configurationText.frame);
        const CGFloat expandedDocumentHeight = NSHeight(scroll.documentView.bounds);
        std::fprintf(stderr,
            "AppKit U1 config 94: short=%.1f long=%.1f page_short=%.1f page_long=%.1f\n",
            (double)shortConfigHeight, (double)expandedConfigHeight,
            (double)shortDocumentHeight, (double)expandedDocumentHeight);
        require(expandedConfigHeight > shortConfigHeight + 100,
                "long U1 configuration status 94 is clipped");
        require(expandedDocumentHeight > shortDocumentHeight + 100,
                "long U1 configuration status 94 did not grow scroll document");
        require(NSMaxY(configurationText.frame) + 8 <= NSMinY(feedbackText.frame),
                "U1 configuration status 94 overlaps action feedback 90");
        require([configurationText.stringValue isEqualToString:warning] &&
                [configurationText.toolTip isEqualToString:warning],
                "U1 configuration status 94 lost complete recovery text");

        // Paths with no spaces must also grow the real U1 status row.
        NSString *unbrokenPath = [@"/사용자/설정/"
            stringByPaddingToLength:3000 withString:@"한글경로" startingAtIndex:0];
        setText(94, unbrokenPath.UTF8String);
        require(NSHeight(configurationText.frame) > shortConfigHeight + 100,
                "unbroken Korean U1 configuration path is clipped");
        require(NSHeight(scroll.documentView.bounds) > shortDocumentHeight + 100,
                "unbroken U1 configuration path did not grow scroll document");
        require(NSMaxY(configurationText.frame) + 8 <= NSMinY(feedbackText.frame),
                "unbroken U1 configuration path overlaps action feedback");
        require([configurationText.stringValue isEqualToString:unbrokenPath] &&
                [configurationText.toolTip isEqualToString:unbrokenPath],
                "unbroken U1 configuration path lost original text");

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
