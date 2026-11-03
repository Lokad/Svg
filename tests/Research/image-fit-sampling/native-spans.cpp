// Native span control adapted from SkScan_Antihair.cpp, SkBitmapProcState.cpp
// and SkBitmapProcShader.cpp
// at f8b66b7597c4cc859d3ed190e9c6872241e6721c.
// Copyright 2011 The Android Open Source Project; 2011 Google Inc.
// See THIRD_PARTY_NOTICES.txt for the BSD license. No Lokad.Svg code is used.
#include <algorithm>
#include <array>
#include <cstdint>
#include <cstdio>
#include <vector>

struct Span { int left, right; };
struct Blitter {
    int queryRow;
    std::vector<Span> spans;
    void vertical(int x, int y, int height, unsigned alpha) {
        if (alpha && queryRow >= y && queryRow < y + height) spans.push_back({x, x + 1});
    }
    void rectangle(int x, int y, int width, int height) {
        if (queryRow >= y && queryRow < y + height) spans.push_back({x, x + width});
    }
    void horizontal(int x, int y, int count, unsigned alpha) {
        // call_hline_blitter's stack run buffer is 100 pixels. Each invocation
        // reaches a separate shadeSpan even when consecutive alphas are equal.
        do {
            int n = std::min(count, 100);
            if (alpha && queryRow == y) spans.push_back({x, x + n});
            x += n;
            count -= n;
        } while (count > 0);
    }
};

static void scanline(int l, int y, int r, unsigned alpha, Blitter& b) {
    if ((l >> 8) == ((r - 1) >> 8)) { b.vertical(l >> 8, y, 1, alpha * (r - l) >> 8); return; }
    int left = l >> 8;
    if (l & 255) { b.vertical(left, y, 1, alpha * (256 - (l & 255)) >> 8); ++left; }
    int right = r >> 8;
    if (right > left) b.horizontal(left, y, right - left, alpha);
    if (r & 255) b.vertical(right, y, 1, alpha * (r & 255) >> 8);
}

static void fill(int l, int t, int r, int bottom, Blitter& b) {
    if (l >= r || t >= bottom) return;
    int top = t >> 8;
    if (top == ((bottom - 1) >> 8)) { scanline(l, top, r, bottom - t - 1, b); return; }
    if (t & 255) { scanline(l, top, r, 256 - (t & 255), b); ++top; }
    int bot = bottom >> 8;
    if (bot > top) {
        int left = l >> 8;
        if (left == ((r - 1) >> 8)) b.vertical(left, top, bot - top, r - l - 1);
        else {
            if (l & 255) { b.vertical(left, top, bot - top, 256 - (l & 255)); ++left; }
            int right = r >> 8;
            if (right > left) b.rectangle(left, top, right - left, bot - top);
            if (r & 255) b.vertical(right, top, bot - top, r & 255);
        }
    }
    if (bottom & 255) scanline(l, bot, r, bottom & 255, b);
}

int main() {
    struct Case { const char* name; std::array<int,4> edges, clip; std::vector<int> rows, columns; };
    const std::vector<Case> cases{
        {"fractional", {2624,5184,64192,6272}, {0,0,400,50}, {20,21,24}, {10,11,110,111,186,210,211,249,250}},
        {"single-full-row", {2624,5120,64192,5376}, {0,0,400,50}, {20}, {10,11,110,111,210,211,250}},
        {"near-full-top", {2624,5121,64192,6144}, {0,0,400,50}, {20,21}, {10,11,110,111,210,211,250}},
        {"one-column", {2624,5184,2752,6272}, {0,0,400,50}, {20,21,24}, {10}},
        {"clipped", {-15552,-896,89792,20608}, {30,10,301,81}, {10,80}, {30,129,130,229,230,300}},
        {"single-clipped-row", {2624,0,64192,25600}, {0,20,400,21}, {20}, {10,11,110,111,210,211,250}},
        {"clipped-bitmap", {2624,5184,102592,6272}, {254,0,510,50}, {20,21,24}, {254,353,354,399,400}}
    };
    std::printf("{\"revision\":\"f8b66b7597c4cc859d3ed190e9c6872241e6721c\",\"partialRowMaximum\":100,\"filteredMatrixMaximum\":%d,\"unfilteredMatrixMaximum\":%d,\"rows\":[\n", (512-4)/4, (512-4)/2);
    bool first = true;
    for (const auto& c : cases) for (int y : c.rows) {
        Blitter b{y,{}};
        fill(std::max(c.edges[0],c.clip[0]*256), std::max(c.edges[1],c.clip[1]*256),
             std::min(c.edges[2],c.clip[2]*256), std::min(c.edges[3],c.clip[3]*256), b);
        for (int x : c.columns) for (const auto& span : b.spans) if (x >= span.left && x < span.right) {
            std::printf("%s{\"case\":\"%s\",\"edges\":[%d,%d,%d,%d],\"clip\":[%d,%d,%d,%d],\"x\":%d,\"y\":%d,\"span\":[%d,%d]}", first?"":",\n", c.name, c.edges[0],c.edges[1],c.edges[2],c.edges[3],c.clip[0],c.clip[1],c.clip[2],c.clip[3],x,y,span.left,span.right);
            first = false;
        }
    }
    std::printf("\n]}\n");
}
