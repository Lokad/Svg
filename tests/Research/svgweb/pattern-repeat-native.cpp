// Adapts SkRasterPipeline_opts.h exclusive_repeat, save_xy and gather clamping.
// Copyright 2016 Google Inc.; 2018 Google LLC. See THIRD_PARTY_NOTICES.txt.
// Pinned source/provenance and compiler options: pattern-repeat-addressing.json.
#include <algorithm>
#include <bit>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <initializer_list>

int main() {
    std::puts("size,translationBits,centerBits,first,second,fractionBits");
    for (int size : {13, 60, 253}) {
        const float limit = static_cast<float>(size);
        for (float requested : {std::nextafter(limit - .5f, 0.f),
                                limit - .5f,
                                std::nextafter(limit + .5f, 0.f),
                                std::nextafter(-limit - .5f, 0.f),
                                std::nextafter(.5f, 0.f)}) {
            // A translated identity shader maps the first destination pixel center.
            const float translation = .5f - requested;
            const float center = .5f - translation;
            const float shifted = center + .5f;
            const float fraction = shifted - std::floor(shifted);
            auto repeat = [limit](float value) {
                const float repeated = value - std::floor(value * (1.f / limit)) * limit;
                const float lower = std::nextafter(0.f, 1.f);
                const float upper = std::nextafter(limit, 0.f);
                return static_cast<int>(std::clamp(repeated, lower, upper));
            };
            std::printf("%d,%u,%u,%d,%d,%u\n", size,
                        std::bit_cast<std::uint32_t>(translation),
                        std::bit_cast<std::uint32_t>(center),
                        repeat(center - .5f), repeat(center + .5f),
                        std::bit_cast<std::uint32_t>(fraction));
        }
    }
}
