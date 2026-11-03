#include <dlfcn.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
int main(void) {
    void *library = dlopen("libm.so.6", RTLD_NOW);
    if (library == NULL) return 1;
    float (*native_atan2)(float, float) = (float (*)(float, float))dlsym(library, "atan2f");
    if (native_atan2 == NULL) return 2;
    for (int y = 1; y <= 16; ++y) for (int x = 1; x <= 16; ++x) {
        float value = native_atan2((float)y, (float)x);
        uint32_t bits;
        memcpy(&bits, &value, sizeof(bits));
        printf("%d,%d,%08x\n", y, x, bits);
    }
    dlclose(library);
    return 0;
}
