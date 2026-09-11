# RTX 5080 NVAPI identity option

This is a narrow, independently implemented proxy inspired by [FakeNvAPI](https://github.com/optiscaler/fakenvapi), inspected at commit `c39271ab990fab3b80ebac93a593b963cdac0f0d`. It is not a repackaged FakeNvAPI DLL. Upstream was merged into OptiScaler and its archived implementation defaults to RTX 4090 with broad NVAPI emulation; this tool needs real NVIDIA driver calls for RTX 30.

The source `src/native/nvapi_proxy.c` exports `nvapi_QueryInterface` and opens the original library only from the Windows system directory. For real GPUs whose original name begins with `NVIDIA GeForce RTX 30`, it wraps three identity interfaces:

- `NvAPI_GPU_GetFullName`: NVIDIA GeForce RTX 5080.
- `NvAPI_GPU_GetPCIIdentifiers`: vendor `10DE`, device `2C02`; original board/subsystem values are preserved.
- `NvAPI_GPU_GetArchInfo`: GB200 architecture family `0x1B0`, GB203 implementation `3`, unknown chip revision, for the known 16-byte V1/V2 layouts.

All other QueryInterface IDs return the original driver entrypoint. Driver errors, original GPU handles, initialization, driver versions, actual memory/compute capabilities and unsupported-interface nulls are retained. No fabricated Reflex/NGX capability responses, registry edits, certificate changes, DXGI hooks or system driver replacement are performed.

The game must actually load the local `nvapi64.dll`. Absolute-system-path loading or GPU checks through DXGI and other interfaces bypass this option. Identifying as 5080 does not grant 5080 performance, add a missing DLSS implementation, or guarantee native FG/MFG menu availability. Use MFG's separate option for the SM86 runtime.

Installation and restore are recorded separately under the renderer directory's `.mfg-enabler/nvapi`. Existing local `nvapi64.dll` bytes are backed up and restored; while enabled, that prior library (including another NVAPI mod) is replaced. Turning off MFG alone does not remove this independent option. Full restore handles both.

References: [NVIDIA NVAPI API documentation](https://docs.nvidia.com/nvapi/group__gpu.html), [official interface identifiers](https://github.com/NVIDIA/nvapi/blob/main/nvapi_interface.h), [NVIDIA device list identifying RTX 5080 as 2C02](https://download.nvidia.com/XFree86/Linux-x86_64/570.133.07/README/supportedchips.html), [FakeNvAPI source](https://github.com/optiscaler/fakenvapi/blob/c39271ab990fab3b80ebac93a593b963cdac0f0d/src/fakenvapi.cpp).

The manager and this independent native source are licensed under GNU GPL version 3 (GPL-3.0-only). No upstream binaries or NVIDIA SDK headers are distributed as part of the build.
