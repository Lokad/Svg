// Scalar adaptation of SkRasterPipeline_opts.h bilerp_clamp_large and accumulate.
// Copyright 2018 Google LLC. BSD license: see THIRD_PARTY_NOTICES.txt.
// Compile with -mavx2 -mfma -ffp-contract=off: match the native x64 FMA stage,
// while only the wrapped stage explicitly fuses products.
#include <array>
#include <bit>
#include <cmath>
#include <cstdint>
#include <cstdio>

int main() {
    constexpr std::array<std::array<unsigned, 4>, 4> pixels{{
        {{47, 35, 33, 236}}, {{47, 35, 33, 237}},
        {{48, 36, 34, 242}}, {{49, 36, 34, 243}}
    }};
    float x = 1.215576171875f, y = 1.5536289215087890625f;
    float fx = (x + .5f) - std::floor(x + .5f);
    float fy = (y + .5f) - std::floor(y + .5f);
    std::array<float, 4> weights{{(1-fx)*(1-fy), fx*(1-fy), (1-fx)*fy, fx*fy}};
    for (bool fused : {false, true}) {
        std::printf("%s", fused ? "wrapped" : "clamped");
        for (unsigned lane = 0; lane < 4; ++lane) {
            float value = 0;
            for (unsigned tap = 0; tap < 4; ++tap) {
                float channel = pixels[tap][lane] * (1.f / 255.f);
                if (fused) value = std::fma(weights[tap], channel, value);
                else value += weights[tap] * channel;
            }
            std::printf(",%08x", std::bit_cast<std::uint32_t>(value));
        }
        std::printf("\n");
    }
}
