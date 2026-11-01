// Native device-axis control. Canvas products adapt SkM44.cpp (2020 Google LLC);
// affine inverse coefficients adapt SkMatrixInvert.cpp (2021 Google LLC).
// Source revision f8b66b7597c4cc859d3ed190e9c6872241e6721c.
// See THIRD_PARTY_NOTICES.txt for the BSD license. Compile with -ffp-contract=off.
#include <array>
#include <bit>
#include <cmath>
#include <cstdint>
#include <cstdio>
using Basis = std::array<float, 4>; // SVG a,b,c,d; origins are independent.
struct Case { const char* id; Basis ctm; };
Basis canvas(const Basis& a, const Basis& b) {
    return {a[0]*b[0]+a[2]*b[1], a[1]*b[0]+a[3]*b[1],
            a[0]*b[2]+a[2]*b[3], a[1]*b[2]+a[3]*b[3]};
}
Basis inverse(const Basis& m) {
    double scale = 1/(double(m[0])*m[3]-double(m[2])*m[1]);
    return {float(m[3]*scale),float(-m[1]*scale),float(-m[2]*scale),float(m[0]*scale)};
}
void values(const Basis& m) {
    std::putchar('[');
    for (int i=0;i<4;i++) std::printf("%s%.17g",i?",":"",double(m[i]));
    std::putchar(']');
}
int main() {
    const Case cases[] = {
        {"overlap-downscale", {.8f,.24f,-.24f,.8f}},
        {"overlap-upscale", {1.74493f,.132486f,-.132486f,1.74493f}},
        {"fractional-source", {-1.7408498525619507f,-.13221058249473572f,.1321762204170227f,-1.7413023710250854f}},
        {"integer-source", {-1.7449300289154053f,-.1324860155582428f,.1324860155582428f,-1.7449300289154053f}}
    };
    std::printf("{\"schemaVersion\":1,\"cases\":[");
    int index=0;
    for (const Case& c : cases) {
        float sx=std::sqrt(c.ctm[0]*c.ctm[0]+c.ctm[1]*c.ctm[1]);
        float sy=std::sqrt(c.ctm[2]*c.ctm[2]+c.ctm[3]*c.ctm[3]);
        Basis toGlobal{c.ctm[0]*(1.f/sx),c.ctm[1]*(1.f/sx),c.ctm[2]*(1.f/sy),c.ctm[3]*(1.f/sy)};
        Basis fromGlobal=inverse(toGlobal), restore=canvas(fromGlobal,toGlobal);
        std::printf("%s{\"id\":\"%s\",\"deviceToGlobal\":",index++?",":"",c.id); values(toGlobal);
        std::printf(",\"globalToDevice\":"); values(fromGlobal);
        std::printf(",\"restoreBits\":[");
        for (int i=0;i<4;i++) std::printf("%s%u",i?",":"",std::bit_cast<std::uint32_t>(restore[i]));
        std::printf("],\"affineRestore\":%s}",restore[1]!=0.f||restore[2]!=0.f?"true":"false");
    }
    std::printf("]}\n");
}
