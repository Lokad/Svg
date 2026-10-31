// Adapts Skia's Src strength reduction, lerp and RGBA8 store stages.
// Copyright 2016 Google Inc.; 2018, 2022 Google LLC. See THIRD_PARTY_NOTICES.txt.
// Pinned source/provenance and compiler options: opaque-gradient-coverage.json.
#include <algorithm>
#include <array>
#include <bit>
#include <cmath>
#include <cstdint>
#include <cstdio>

struct Case {
    const char* name;
    std::array<float, 4> source;
    std::array<int, 4> destination;
    int coverage;
    bool opaque;
    int kind; // Mask=0, Direct=1, Pair=2; pairs use the native A8 mask route.
};

int main() {
    const float red = std::bit_cast<float>(std::uint32_t{1064961028});
    const float halfByte = .5f / 255.f;
    const std::array<float, 4> tail{red, 1, 1, 1};
    const std::array<float, 4> small{halfByte, halfByte, halfByte, 1};
    const std::array<int, 4> backdrop{174, 182, 188, 255};
    const std::array<int, 4> white{255, 255, 255, 255};
    const Case cases[] = {
        {"opaque_antih_threshold", tail, backdrop, 192, true, 1},
        {"nonopaque_program_opaque_sample", tail, backdrop, 192, false, 1},
        {"full_direct_source_store", small, white, 255, true, 1},
        {"full_a8_mask_lerp", small, white, 255, true, 0},
        {"full_fringe_pair_lerp", small, white, 255, true, 2},
        {"zero_coverage", tail, backdrop, 0, true, 1},
    };
    std::puts("id,redBits,greenBits,blueBits,alphaBits,dstRed,dstGreen,dstBlue,dstAlpha,coverage,opaque,kind,outRed,outGreen,outBlue,outAlpha");
    for (const Case& c : cases) {
        std::printf("%s", c.name);
        for (float value : c.source) std::printf(",%u", std::bit_cast<std::uint32_t>(value));
        for (int value : c.destination) std::printf(",%d", value);
        std::printf(",%d,%d,%d", c.coverage, c.opaque ? 1 : 0, c.kind);
        const float coverage = c.coverage * (1.f / 255.f);
        for (int lane = 0; lane < 4; lane++) {
            const float source = std::clamp(c.source[lane], 0.f, 1.f);
            const float destination = c.destination[lane] * (1.f / 255.f);
            float result;
            if (c.opaque) {
                result = c.coverage == 255 && c.kind == 1 ? source
                    : std::fma(source - destination, coverage, destination);
            } else {
                result = std::fma(destination, 1.f - c.source[3] * coverage, source * coverage);
            }
            std::printf(",%d", std::clamp(static_cast<int>(std::nearbyintf(result * 255.f)), 0, 255));
        }
        std::putchar('\n');
    }
}
