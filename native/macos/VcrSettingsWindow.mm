#import <AppKit/AppKit.h>
#include <cstring>
#include <deque>
#include <map>
#include <string>
#include <vector>

namespace {
    NSWindow *settingsWindow = nil;
    NSView *settingsRoot = nil;
    NSMutableDictionary<NSNumber *, NSTextField *> *fields = nil;
    NSMutableDictionary<NSNumber *, NSMutableArray<NSView *> *> *categoryViews = nil;
    int categoryRows[4] = {0, 0, 0, 0};
    int selectedCategory = 0;
    std::deque<int> actions;

    NSString *toString(const char *utf8) {
        return utf8 ? [NSString stringWithUTF8String:utf8] ?: @"" : @"";
    }

    void selectCategory(int category) {
        if (category < 0 || category >= 4) return;
        selectedCategory = category;
        for (int i = 0; i < 4; ++i) {
            NSArray<NSView *> *views = categoryViews[@(i)];
            for (NSView *view in views) {
                view.hidden = i != category;
            }
        }
    }
}

@interface VcrSettingsWindowDelegate : NSObject<NSWindowDelegate>
- (void)clicked:(id)sender;
@end

@implementation VcrSettingsWindowDelegate
- (void)clicked:(id)sender {
    const int button = (int)[sender tag];
    if (button >= 100 && button < 104) {
        selectCategory(button - 100);
    } else {
        actions.push_back(button);
    }
}
- (BOOL)windowShouldClose:(NSWindow *)window {
    [window orderOut:nil];
    return NO;
}
@end

namespace {
    VcrSettingsWindowDelegate *delegate = nil;
}

extern "C" {
// Close the visible application surface before Unity's synchronous quit
// callbacks dispose inference/capture resources. Never force process exit.
__attribute__((visibility("default"))) void VCR_SettingsHideAppWindowsForQuit(void) {
    if (![NSThread isMainThread]) return;
    for (NSWindow *window in [NSApp windows]) {
        if ([window isVisible])
            [window orderOut:nil];
    }
}

__attribute__((visibility("default"))) void VCR_SettingsCreate(void) {
    if (settingsWindow) return;
    delegate = [[VcrSettingsWindowDelegate alloc] init];
    settingsWindow = [[NSWindow alloc]
        initWithContentRect:NSMakeRect(0, 0, 940, 720)
                  styleMask:(NSWindowStyleMaskTitled |
                             NSWindowStyleMaskClosable |
                             NSWindowStyleMaskMiniaturizable |
                             NSWindowStyleMaskResizable)
                    backing:NSBackingStoreBuffered
                      defer:NO];
    [settingsWindow setTitle:@"VirtualCharacterRender - 환경설정"];
    [settingsWindow setDelegate:delegate];
    [settingsWindow center];
    settingsRoot = settingsWindow.contentView;
    [settingsRoot setWantsLayer:YES];
    settingsRoot.layer.backgroundColor = [NSColor windowBackgroundColor].CGColor;
    fields = [NSMutableDictionary dictionary];
    categoryViews = [NSMutableDictionary dictionary];
    NSArray<NSString *> *labels = @[@"일반", @"캐릭터 / 모델", @"트래킹", @"출력"];
    for (int category = 0; category < 4; ++category) {
        NSButton *button = [[NSButton alloc]
            initWithFrame:NSMakeRect(20 + category * 220, 670, 202, 33)];
        button.title = labels[category];
        button.tag = 100 + category;
        button.target = delegate;
        button.action = @selector(clicked:);
        button.bezelStyle = NSBezelStyleRounded;
        button.autoresizingMask = NSViewMinYMargin;
        [settingsRoot addSubview:button];
        categoryViews[@(category)] = [NSMutableArray array];
    }
    selectCategory(0);
}

__attribute__((visibility("default"))) int VCR_SettingsIsVisible(void) {
    return settingsWindow && [settingsWindow isVisible] ? 1 : 0;
}

__attribute__((visibility("default"))) void VCR_SettingsShow(void) {
    if (!settingsWindow) VCR_SettingsCreate();
    [settingsWindow makeKeyAndOrderFront:nil];
    [NSApp activateIgnoringOtherApps:YES];
}

__attribute__((visibility("default"))) void VCR_SettingsSelectCategory(int category) {
    selectCategory(category);
}

__attribute__((visibility("default"))) void VCR_SettingsAddControl(
    int category, int ident, int kind, const char *label, const char *value) {
    if (!settingsWindow) VCR_SettingsCreate();
    if (category < 0 || category >= 4) return;
    const CGFloat y = 620 - categoryRows[category]++ * 48;
    NSMutableArray<NSView *> *views = categoryViews[@(category)];
    if (kind == 2) {
        NSTextField *caption = [NSTextField labelWithString:toString(label)];
        caption.frame = NSMakeRect(220, y + 4, 230, 30);
        caption.autoresizingMask = NSViewMinYMargin;
        [settingsRoot addSubview:caption];
        [views addObject:caption];
        NSTextField *input = [[NSTextField alloc]
            initWithFrame:NSMakeRect(460, y + 4, 388, 29)];
        input.stringValue = toString(value);
        input.autoresizingMask = NSViewMinYMargin;
        [settingsRoot addSubview:input];
        [views addObject:input];
        fields[@(ident)] = input;
    } else if (kind == 1) {
        NSButton *button = [[NSButton alloc]
            initWithFrame:NSMakeRect(220, y, 628, 36)];
        button.title = toString(label);
        button.tag = ident;
        button.target = delegate;
        button.action = @selector(clicked:);
        button.bezelStyle = NSBezelStyleRounded;
        button.autoresizingMask = NSViewMinYMargin;
        [settingsRoot addSubview:button];
        [views addObject:button];
    } else {
        NSTextField *text = [NSTextField labelWithString:toString(label)];
        text.frame = NSMakeRect(220, y + 4, 628, 30);
        text.autoresizingMask = NSViewMinYMargin;
        [settingsRoot addSubview:text];
        [views addObject:text];
    }
    for (NSView *view in views) view.hidden = category != selectedCategory;
}

__attribute__((visibility("default"))) void VCR_SettingsClearCategory(int category) {
    if (category < 0 || category >= 4) return;
    NSArray<NSView *> *views = categoryViews[@(category)];
    for (NSView *view in views) {
        if ([view isKindOfClass:NSTextField.class]) {
            for (NSNumber *key in [fields allKeys]) {
                if (fields[key] == view) [fields removeObjectForKey:key];
            }
        }
        [view removeFromSuperview];
    }
    [categoryViews[@(category)] removeAllObjects];
    categoryRows[category] = 0;
}

__attribute__((visibility("default"))) void VCR_SettingsSetText(
    int ident, const char *value) {
    NSTextField *field = fields[@(ident)];
    if (field) field.stringValue = toString(value);
}

__attribute__((visibility("default"))) void VCR_SettingsGetText(
    int ident, char *buffer, int capacity) {
    if (!buffer || capacity <= 0) return;
    const char *value = [fields[@(ident)].stringValue UTF8String];
    if (!value) value = "";
    std::strncpy(buffer, value, (size_t)(capacity - 1));
    buffer[capacity - 1] = 0;
}

__attribute__((visibility("default"))) int VCR_SettingsPollAction(void) {
    if (actions.empty()) return 0;
    const int next = actions.front();
    actions.pop_front();
    return next;
}

__attribute__((visibility("default"))) void VCR_SettingsDestroy(void) {
    if (settingsWindow) {
        [settingsWindow setDelegate:nil];
        [settingsWindow orderOut:nil];
        [settingsWindow close];
    }
    settingsWindow = nil;
    settingsRoot = nil;
    fields = nil;
    categoryViews = nil;
    delegate = nil;
    actions.clear();
    for (int &row : categoryRows) row = 0;
    selectedCategory = 0;
}
}
