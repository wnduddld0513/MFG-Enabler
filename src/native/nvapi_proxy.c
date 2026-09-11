/* MFG Enabler: a narrow NVAPI identity proxy, inspired by FakeNvAPI.
 * SPDX-License-Identifier: GPL-3.0-only
 * Copyright (c) 2026 MFG Enabler contributors.
 * No driver/Reflex/NGX capability emulation. See docs/SPOOF5080.md.
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <string.h>

typedef int (__cdecl *NameFn)(void *, char *);
typedef int (__cdecl *PciFn)(void *, uint32_t *, uint32_t *, uint32_t *, uint32_t *);
typedef struct { uint32_t version, architecture, implementation, revision; } ArchInfo;
typedef int (__cdecl *ArchFn)(void *, ArchInfo *);
typedef void *(__cdecl *QueryFn)(uint32_t);
#define ID_NAME 0xceee8e9fu
#define ID_PCI  0x2ddfb66eu
#define ID_ARCH 0xd8265d24u

static INIT_ONCE once = INIT_ONCE_STATIC_INIT;
static QueryFn real_query;

static BOOL CALLBACK initialize(PINIT_ONCE unused, PVOID argument, PVOID *context) {
    (void)unused; (void)argument; (void)context;
#ifdef MFG_NVAPI_TEST
    extern void * __cdecl fixture_query(uint32_t);
    real_query = fixture_query;
#else
    wchar_t path[MAX_PATH];
    UINT count = GetSystemDirectoryW(path, MAX_PATH);
    if (!count || count + 13 >= MAX_PATH) return TRUE;
    memcpy(path + count, L"\\nvapi64.dll", sizeof(L"\\nvapi64.dll"));
    /* Absolute system path only: never load another mod or a search-path DLL. */
    HMODULE module = LoadLibraryExW(path, NULL, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (module) real_query = (QueryFn)(void *)GetProcAddress(module, "nvapi_QueryInterface");
    /* Keep this module referenced for the lifetime of the process. */
#endif
    return TRUE;
}

static void *original(uint32_t id) {
    InitOnceExecuteOnce(&once, initialize, NULL, NULL);
    return real_query ? real_query(id) : NULL;
}

static int is_rtx30(void *gpu) {
    NameFn get_name = (NameFn)original(ID_NAME);
    char name[64] = {0};
    return get_name && get_name(gpu, name) == 0 &&
           strncmp(name, "NVIDIA GeForce RTX 30", sizeof("NVIDIA GeForce RTX 30")-1) == 0;
}

static int __cdecl spoof_name(void *gpu, char *name) {
    NameFn fn = (NameFn)original(ID_NAME);
    if (!fn) return -3;
    int status = fn(gpu, name);
    if (status == 0 && name && strncmp(name, "NVIDIA GeForce RTX 30", sizeof("NVIDIA GeForce RTX 30")-1) == 0)
        memcpy(name, "NVIDIA GeForce RTX 5080", sizeof("NVIDIA GeForce RTX 5080"));
    return status;
}

static int __cdecl spoof_pci(void *gpu, uint32_t *device, uint32_t *subsystem, uint32_t *revision, uint32_t *external) {
    PciFn fn = (PciFn)original(ID_PCI);
    if (!fn) return -3;
    int status = fn(gpu, device, subsystem, revision, external);
    if (status == 0 && is_rtx30(gpu)) {
        if (device) *device = 0x2c0210deu;
        if (external) *external = 0x2c02u;
        /* Preserve the real board/subsystem and revision information. */
    }
    return status;
}

static int __cdecl spoof_arch(void *gpu, ArchInfo *info) {
    ArchFn fn = (ArchFn)original(ID_ARCH);
    if (!fn) return -3;
    int status = fn(gpu, info);
    if (status == 0 && info && (info->version == 0x10010u || info->version == 0x20010u) && is_rtx30(gpu)) {
        info->architecture = 0x1b0u; /* Blackwell GB200 family */
        info->implementation = 3u;  /* GB203 */
        info->revision = 0xffffffffu;
    }
    return status;
}

__declspec(dllexport) void * __cdecl nvapi_QueryInterface(uint32_t id) {
    void *fn = original(id);
    if (!fn) return NULL; /* Never manufacture unsupported driver interfaces. */
    switch (id) {
        case ID_NAME: return (void *)spoof_name;
        case ID_PCI: return (void *)spoof_pci;
        case ID_ARCH: return (void *)spoof_arch;
        default: return fn;
    }
}

#ifndef MFG_NVAPI_TEST
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved) {
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(instance);
    return TRUE;
}
#endif
