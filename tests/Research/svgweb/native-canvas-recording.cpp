// Native arithmetic control: recorded SVG matrix preparation before canvas capture.
// Canvas product order adapts Skia src/core/SkM44.cpp at
// f8b66b7597c4cc859d3ed190e9c6872241e6721c. Copyright 2020 Google LLC.
// Shader concatenation/inversion follows SkMatrix.cpp at that revision.
// Copyright 2006 The Android Open Source Project. See THIRD_PARTY_NOTICES.txt.
#include <array>
#include <bit>
#include <cstdint>
#include <cstdio>
using Matrix = std::array<double, 6>;
using Native = std::array<float, 6>;
Native canvas(const Native& a, const Native& b) {
    return {a[0]*b[0] + a[2]*b[1], a[1]*b[0] + a[3]*b[1],
            a[0]*b[2] + a[2]*b[3], a[1]*b[2] + a[3]*b[3],
            a[0]*b[4] + (a[2]*b[5] + a[4]), a[1]*b[4] + (a[3]*b[5] + a[5])};
}
Native inverse(const Native& m) {
    double s = 1 / (double(m[0])*m[3]-double(m[2])*m[1]);
    return {float(m[3]*s), float(-m[1]*s), float(-m[2]*s), float(m[0]*s),
            float((double(m[2])*m[5]-double(m[3])*m[4])*s), float((double(m[1])*m[4]-double(m[0])*m[5])*s)};
}
Native prepare(const Matrix& root, const Matrix& target) {
    return {float(root[0]*target[0]+root[2]*target[1]), float(root[1]*target[0]+root[3]*target[1]),
            float(root[0]*target[2]+root[2]*target[3]), float(root[1]*target[2]+root[3]*target[3]),
            float(root[0]*target[4]+root[2]*target[5]+root[4]), float(root[1]*target[4]+root[3]*target[5]+root[5])};
}
template<class T> void numbers(const std::array<T,6>& m) {
    std::putchar('['); for (int i=0;i<6;i++) std::printf("%s%.17g",i?",":"",double(m[i])); std::putchar(']');
}
void bits(const Native& m) {
    std::putchar('['); for (int i=0;i<6;i++) std::printf("%s%u",i?",":"",std::bit_cast<std::uint32_t>(m[i])); std::putchar(']');
}
int main() {
    const Matrix initial{2.125, .0625, -.1875, .8125, 4096.125, -2048.5};
    const Native output{.7f, 0, 0, 1.3f, -13.25f, 9.5f};
    const Native capture{.9f, .2f, -.3f, 1.1f, 80.25f, 20.5f};
    std::printf("{\"schemaVersion\":1,\"canvasCases\":[");
    for (int depth : {64,65}) {
        const Matrix firstLocal{1,0,0,1,.125,.125};
        Native prepared = canvas(output, prepare(initial,firstLocal));
        const Native local{1,0,0,1,.125f,.125f};
        for (int stage=1;stage<depth;stage++) prepared = canvas(prepared,local);
        std::printf("%s{\"depth\":%d,\"initial\":",depth==64?"":",",depth); numbers(initial);
        std::printf(",\"output\":"); numbers(output); std::printf(",\"capture\":"); numbers(capture);
        std::printf(",\"origins\":[");
        for (int index=0;index<2;index++) {
            int x=index?17:0, y=index?23:0; Native shifted=capture; shifted[4]-=x; shifted[5]-=y;
            std::printf("%s{\"x\":%d,\"y\":%d,\"expectedBits\":",index?",":"",x,y); bits(inverse(canvas(shifted,prepared))); std::putchar('}');
        }
        std::printf("]}");
    }
    const Native root{1,0,0,1,-254,-254};
    const Matrix target{.08660254166892228,.05000000074505805,-.05000000074505805,.08660254166892228,30.69873026822478,-18.569219658355983};
    const Native source{1,0,0,1,262,257};
    std::printf("],\"rootCaptureCase\":{\"output\":"); numbers(root);
    std::printf(",\"target\":"); numbers(target);
    std::printf(",\"capture\":"); numbers(source);
    std::printf(",\"expectedBits\":"); bits(canvas(source,canvas(root,prepare(Matrix{1,0,0,1,0,0},target))));
    std::printf("}}\n");
}
