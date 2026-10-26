/* A main-queue bridge for the managed bootstrap. No game code is patched. */
#include <dispatch/dispatch.h>
#include <stdint.h>
#include <sys/sysctl.h>

typedef void (*digfloor_callback)(void);

static void run_callback(void *context) {
    ((digfloor_callback)context)();
}

void digfloor_schedule(digfloor_callback callback, double delay) {
    dispatch_after_f(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(delay * NSEC_PER_SEC)),
                     dispatch_get_main_queue(), (void *)callback, run_callback);
}

int digfloor_is_native(void) {
#if defined(__arm64__)
    int translated = 0;
    size_t length = sizeof(translated);
    if (sysctlbyname("sysctl.proc_translated", &translated, &length, NULL, 0) == 0)
        return translated == 0;
    return 1; /* sysctl is absent on native processes on some macOS versions. */
#else
    return 0;
#endif
}
