// Native arithmetic control: recorded SVG matrix preparation before canvas capture.
// Canvas product order adapts Skia src/core/SkM44.cpp at
// f8b66b7597c4cc859d3ed190e9c6872241e6721c. Copyright 2020 Google LLC.
// Shader concatenation/inversion follows SkMatrix.cpp at that revision.
// Copyright 2006 The Android Open Source Project. See THIRD_PARTY_NOTICES.txt.
#include <array>
#include <bit>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <limits>
using Matrix = std::array<double, 6>;
using Native = std::array<float, 6>;
struct Case { const char* id; Matrix root, source, layer, target; Native capture; };
Native narrow(const Matrix& m) { return {float(m[0]), float(m[1]), float(m[2]), float(m[3]), float(m[4]), float(m[5])}; }
Native canvas(const Native& a, const Native& b) {
    return {a[0]*b[0] + a[2]*b[1], a[1]*b[0] + a[3]*b[1],
            a[0]*b[2] + a[2]*b[3], a[1]*b[2] + a[3]*b[3],
            a[0]*b[4] + (a[2]*b[5] + a[4]), a[1]*b[4] + (a[3]*b[5] + a[5])};
}
Native shader(const Native& a, const Native& b) {
    if (a[1] == 0 && a[2] == 0 && b[1] == 0 && b[2] == 0)
        return {a[0]*b[0], 0, 0, a[3]*b[3], a[0]*b[4]+a[4], a[3]*b[5]+a[5]};
    return {float(double(a[0])*b[0]+double(a[2])*b[1]), float(double(a[1])*b[0]+double(a[3])*b[1]),
            float(double(a[0])*b[2]+double(a[2])*b[3]), float(double(a[1])*b[2]+double(a[3])*b[3]),
            float(double(a[0])*b[4]+double(a[2])*b[5])+a[4], float(double(a[1])*b[4]+double(a[3])*b[5])+a[5]};
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
    const Case base[] = {{"fractional", {0.99766170413555011, 0, 0, 0.9979210370131919, 0, 0}, {0.80000001192092896, 0.24000000953674316, -0.15000000596046448, 0.90000003576278687, 55, 40}, {0.83328944444656372, 0, 0, 0.91051113605499268, 0, 0}, {0.80000001192092896, 0.24000000953674316, -0.15000000596046448, 0.90000003576278687, 59.712500058114529, 44.485000178217888}, {0.99433630704879761, -0.28972864151000977, 0.16567964851856232, 0.9655110239982605, -61.174026489257812, -22.642333984375}},
{"compensated", {0.99766170413555011, 0, 0, 0.9979210370131919, 0, 0}, {0.80187503102235524, 0.24050000192721654, -0.15035157205071314, 0.90187500722706204, 55.128907561302185, 40.083332061767578}, {0.83522456884384155, 0, 0, 0.91241437196731567, 0, 0}, {0.80187503102235524, 0.24050000192721654, -0.15035157205071314, 0.90187500722706204, 59.852452653782677, 44.577675847782437}, {0.99431490898132324, -0.28965535759925842, 0.16571915149688721, 0.96551781892776489, -61.316085815429688, -22.689668655395508}}};
    std::printf("{\"schemaVersion\":1,\"sourceCases\":[");
    for (int i=0;i<6;i++) {
        Case c = base[i == 1 ? 1 : 0];
        if (i >= 2) c.target[i-2] = std::nextafter(float(c.target[i-2]), std::numeric_limits<float>::infinity());
        std::printf("%s{\"id\":%d,\"root\":",i?",":"",i); numbers(c.root);
        std::printf(",\"source\":"); numbers(c.source); std::printf(",\"layer\":"); numbers(c.layer);
        std::printf(",\"target\":"); numbers(c.target); std::printf(",\"capture\":"); numbers(c.capture);
        std::printf(",\"expectedBits\":"); bits(canvas(c.capture,prepare(c.root,c.target))); std::putchar('}');
    }
    const Matrix initial{2.125, .0625, -.1875, .8125, 4096.125, -2048.5};
    const Native output{.7f, 0, 0, 1.3f, -13.25f, 9.5f};
    const Native capture{.9f, .2f, -.3f, 1.1f, 80.25f, 20.5f};
    std::printf("],\"shaderCases\":[");
    for (int depth : {64,65}) {
        const Matrix firstLocal{1,0,0,1,.125,.125};
        Native prepared = shader(output, prepare(initial,firstLocal));
        const Native local{1,0,0,1,.125f,.125f};
        for (int stage=1;stage<depth;stage++) prepared = shader(prepared,local);
        std::printf("%s{\"depth\":%d,\"initial\":",depth==64?"":",",depth); numbers(initial);
        std::printf(",\"output\":"); numbers(output); std::printf(",\"capture\":"); numbers(capture);
        std::printf(",\"origins\":[");
        for (int index=0;index<2;index++) {
            int x=index?17:0, y=index?23:0; Native shifted=capture; shifted[4]-=x; shifted[5]-=y;
            std::printf("%s{\"x\":%d,\"y\":%d,\"expectedBits\":",index?",":"",x,y); bits(inverse(canvas(shifted,prepared))); std::putchar('}');
        }
        std::printf("]}");
    }
    std::printf("]}\n");
}
