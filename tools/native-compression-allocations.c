/* Linux/glibc research observer. Build as a private LD_PRELOAD library, never a
 * renderer dependency. Attributes requested bytes to the runtime compression
 * module, excluding allocator overhead, managed heaps and unrelated native work. */
#define _GNU_SOURCE
#include <dlfcn.h>
#include <fcntl.h>
#include <pthread.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

extern void *__libc_malloc(size_t);
extern void *__libc_calloc(size_t, size_t);
extern void *__libc_memalign(size_t, size_t);
extern void __libc_free(void *);

struct allocation { void *pointer; size_t bytes; unsigned long id; };
struct observation { unsigned long id; size_t bytes; int allocate; };
static struct allocation live[256];
static struct observation events[2048];
static pthread_mutex_t gate = PTHREAD_MUTEX_INITIALIZER;
static _Thread_local int observing __attribute__((tls_model("initial-exec")));
static size_t current_bytes, peak_bytes, total_bytes, event_count;
static unsigned long allocations, releases, overflow;
static int enabled;
static char module[4096];

static void observe_allocation(void *pointer, size_t bytes, void *caller)
{
    if (!enabled || observing || pointer == NULL) return;
    observing = 1;
    Dl_info info;
    if (dladdr(caller, &info) && info.dli_fname != NULL
        && strstr(info.dli_fname, "/libSystem.IO.Compression.Native.so") != NULL) {
        pthread_mutex_lock(&gate);
        size_t slot;
        for (slot = 0; slot < 256 && live[slot].pointer != NULL; slot++) { }
        if (slot == 256 || event_count == 2048) overflow++;
        else {
            if (module[0] == '\0') snprintf(module, sizeof(module), "%s", info.dli_fname);
            unsigned long id = ++allocations;
            live[slot] = (struct allocation){pointer, bytes, id};
            current_bytes += bytes;
            total_bytes += bytes;
            if (current_bytes > peak_bytes) peak_bytes = current_bytes;
            events[event_count++] = (struct observation){id, bytes, 1};
        }
        pthread_mutex_unlock(&gate);
    }
    observing = 0;
}

void *malloc(size_t bytes)
{
    void *pointer = __libc_malloc(bytes);
    observe_allocation(pointer, bytes, __builtin_return_address(0));
    return pointer;
}

void *calloc(size_t count, size_t size)
{
    void *pointer = __libc_calloc(count, size);
    if (size == 0 || count <= SIZE_MAX / size)
        observe_allocation(pointer, count * size, __builtin_return_address(0));
    return pointer;
}

void *aligned_alloc(size_t alignment, size_t bytes)
{
    void *pointer = __libc_memalign(alignment, bytes);
    observe_allocation(pointer, bytes, __builtin_return_address(0));
    return pointer;
}

void free(void *pointer)
{
    if (enabled && !observing && pointer != NULL) {
        observing = 1;
        pthread_mutex_lock(&gate);
        for (size_t slot = 0; slot < 256; slot++) {
            if (live[slot].pointer != pointer) continue;
            current_bytes -= live[slot].bytes;
            releases++;
            if (event_count == 2048) overflow++;
            else events[event_count++] = (struct observation){live[slot].id, live[slot].bytes, 0};
            live[slot].pointer = NULL;
            break;
        }
        pthread_mutex_unlock(&gate);
        observing = 0;
    }
    __libc_free(pointer);
}

__attribute__((constructor)) static void start_observing(void)
{
    enabled = getenv("SVG_NATIVE_ALLOCATIONS") != NULL;
}

__attribute__((destructor)) static void finish_observing(void)
{
    const char *path = getenv("SVG_NATIVE_ALLOCATIONS");
    if (!enabled || path == NULL) return;
    observing = 1;
    int file = open(path, O_WRONLY | O_CREAT | O_EXCL, 0600);
    if (file < 0) _exit(97);
    dprintf(file, "{\"allocations\":%lu,\"releases\":%lu,\"totalRequestedBytes\":%zu,\"peakLiveRequestedBytes\":%zu,\"liveRequestedBytesAtExit\":%zu,\"overflow\":%lu,\"module\":\"%s\",\"events\":[",
        allocations, releases, total_bytes, peak_bytes, current_bytes, overflow, module);
    for (size_t index = 0; index < event_count; index++)
        dprintf(file, "%s[%lu,%d,%zu]", index == 0 ? "" : ",", events[index].id, events[index].allocate, events[index].bytes);
    dprintf(file, "]}\n");
    close(file);
}
