#import <AppKit/AppKit.h>
#include <cstring>
#include <algorithm>
#include <deque>
#include <map>
#include <string>
#include <vector>

namespace {
    NSWindow *settingsWindow = nil;
    NSView *settingsRoot = nil;
    NSScrollView *details = nil;
    NSView *sidebar = nil;
    NSMutableDictionary<NSNumber *, NSView *> *pages = nil;
    NSMutableDictionary<NSNumber *, NSMutableArray<NSArray<NSView *> *> *> *rowGroups = nil;
    NSMutableArray<NSButton *> *navigation = nil;
    NSColor *constColor(CGFloat r, CGFloat g, CGFloat b) {
        return [NSColor colorWithCalibratedRed:r green:g blue:b alpha:1];
    }
    NSColor *surfaceColor() { return constColor(0.106, 0.122, 0.145); }
    NSColor *sidebarColor() { return constColor(0.141, 0.165, 0.192); }
    NSColor *buttonColor() { return constColor(0.165, 0.188, 0.231); }
    NSColor *activeColor() { return constColor(0.105, 0.350, 0.665); }
    NSColor *foregroundColor() { return constColor(0.886, 0.910, 0.945); }
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
        for (int i = 0; i < (int)navigation.count; ++i) {
            NSButton *button = navigation[(NSUInteger)i];
            button.layer.backgroundColor =
                (i == category ? activeColor() : sidebarColor()).CGColor;
            button.contentTintColor = foregroundColor();
        }
        if (details) {
            [details setDocumentView:pages[@(category)]];
            // NSScrollView has a distinct scroll position for the selected page.
            [details.contentView scrollToPoint:
                NSMakePoint(0, NSHeight(pages[@(category)].bounds) -
                              NSHeight(details.contentView.bounds))];
            [details reflectScrolledClipView:details.contentView];
        }
    }

    void layoutRows(int category) {
        if (category < 0 || category >= 4 || !pages) return;
        NSView *page = pages[@(category)];
        CGFloat height = std::max((CGFloat)650, (CGFloat)categoryRows[category] * 52 + 36);
        page.frame = NSMakeRect(0, 0, 660, height);
        NSArray<NSArray<NSView *> *> *rows = rowGroups[@(category)];
        for (NSUInteger index = 0; index < rows.count; ++index) {
            CGFloat y = height - 64 - (CGFloat)index * 52;
            for (NSView *view in rows[index]) {
                NSRect frame = view.frame;
                frame.origin.y = y;
                view.frame = frame;
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
        // Keep the managed selection in sync with the native sidebar.
        actions.push_back(button);
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
    // The detail document is 660pt wide and horizontal scrolling is disabled.
    // Do not allow a narrower content area to clip its text fields/buttons.
    [settingsWindow setContentMinSize:NSMakeSize(940, 480)];
    [settingsWindow setDelegate:delegate];
    [settingsWindow center];
    settingsWindow.level = NSNormalWindowLevel;
    settingsWindow.appearance =
        [NSAppearance appearanceNamed:NSAppearanceNameDarkAqua];
    settingsRoot = settingsWindow.contentView;
    [settingsRoot setWantsLayer:YES];
    settingsRoot.layer.backgroundColor = surfaceColor().CGColor;
    fields = [NSMutableDictionary dictionary];
    categoryViews = [NSMutableDictionary dictionary];
    pages = [NSMutableDictionary dictionary];
    rowGroups = [NSMutableDictionary dictionary];
    navigation = [NSMutableArray array];

    sidebar = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, 220, 720)];
    sidebar.autoresizingMask = NSViewHeightSizable;
    sidebar.wantsLayer = YES;
    sidebar.layer.backgroundColor = sidebarColor().CGColor;
    [settingsRoot addSubview:sidebar];

    details = [[NSScrollView alloc] initWithFrame:NSMakeRect(242, 20, 678, 680)];
    details.autoresizingMask = NSViewWidthSizable | NSViewHeightSizable;
    details.hasVerticalScroller = YES;
    details.hasHorizontalScroller = NO;
    details.drawsBackground = NO;
    [settingsRoot addSubview:details];

    NSTextField *heading = [NSTextField labelWithString:@"환경설정"];
    heading.frame = NSMakeRect(22, 654, 180, 32);
    heading.font = [NSFont boldSystemFontOfSize:19];
    heading.textColor = foregroundColor();
    heading.autoresizingMask = NSViewMinYMargin;
    [sidebar addSubview:heading];

    NSArray<NSString *> *labels = @[@"일반", @"캐릭터 / 모델", @"트래킹", @"출력"];
    for (int category = 0; category < 4; ++category) {
        NSButton *button = [[NSButton alloc]
            initWithFrame:NSMakeRect(18, 593 - category * 50, 184, 40)];
        button.title = labels[category];
        button.tag = 100 + category;
        button.target = delegate;
        button.action = @selector(clicked:);
        button.bordered = NO;
        button.wantsLayer = YES;
        button.layer.cornerRadius = 5;
        button.layer.backgroundColor = sidebarColor().CGColor;
        button.contentTintColor = foregroundColor();
        button.font = [NSFont systemFontOfSize:14 weight:NSFontWeightMedium];
        button.autoresizingMask = NSViewMinYMargin;
        [sidebar addSubview:button];
        [navigation addObject:button];
        categoryViews[@(category)] = [NSMutableArray array];
        rowGroups[@(category)] = [NSMutableArray array];
        NSView *page = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, 660, 650)];
        pages[@(category)] = page;
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
    ++categoryRows[category];
    NSView *page = pages[@(category)];
    NSMutableArray<NSView *> *views = categoryViews[@(category)];
    NSMutableArray<NSView *> *row = [NSMutableArray array];
    if (kind == 2) {
        NSTextField *caption = [NSTextField labelWithString:toString(label)];
        caption.frame = NSMakeRect(20, 0, 208, 30);
        caption.textColor = foregroundColor();
        caption.autoresizingMask = NSViewMinYMargin;
        [page addSubview:caption];
        [views addObject:caption];
        [row addObject:caption];
        NSTextField *input = [[NSTextField alloc]
            initWithFrame:NSMakeRect(244, 0, 380, 30)];
        input.stringValue = toString(value);
        input.textColor = foregroundColor();
        input.backgroundColor = buttonColor();
        input.drawsBackground = YES;
        input.autoresizingMask = NSViewWidthSizable | NSViewMinYMargin;
        [page addSubview:input];
        [views addObject:input];
        [row addObject:input];
        fields[@(ident)] = input;
    } else if (kind == 1) {
        NSButton *button = [[NSButton alloc]
            initWithFrame:NSMakeRect(20, 0, 604, 36)];
        button.title = toString(label);
        button.tag = ident;
        button.target = delegate;
        button.action = @selector(clicked:);
        button.bordered = NO;
        button.wantsLayer = YES;
        button.layer.backgroundColor = buttonColor().CGColor;
        button.layer.cornerRadius = 5;
        button.contentTintColor = foregroundColor();
        button.autoresizingMask = NSViewWidthSizable | NSViewMinYMargin;
        [page addSubview:button];
        [views addObject:button];
        [row addObject:button];
    } else {
        NSTextField *text = [NSTextField labelWithString:toString(label)];
        text.frame = NSMakeRect(20, 0, 604, 30);
        text.font = [NSFont systemFontOfSize:16 weight:NSFontWeightSemibold];
        text.textColor = foregroundColor();
        text.autoresizingMask = NSViewWidthSizable | NSViewMinYMargin;
        [page addSubview:text];
        [views addObject:text];
        [row addObject:text];
        if (ident != 0) fields[@(ident)] = text;
    }
    [rowGroups[@(category)] addObject:row];
    layoutRows(category);
    if (category == selectedCategory)
        [details setDocumentView:page];
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
    [rowGroups[@(category)] removeAllObjects];
    layoutRows(category);
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
    sidebar = nil;
    details = nil;
    pages = nil;
    rowGroups = nil;
    navigation = nil;
    fields = nil;
    categoryViews = nil;
    delegate = nil;
    actions.clear();
    for (int &row : categoryRows) row = 0;
    selectedCategory = 0;
}
}
