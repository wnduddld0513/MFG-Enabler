# NVIDIA App integration — 1.1

Inspected locally on 2026-09-10 against NVIDIA App product version 11.0.9.251.

## Observations

- The supplied NVIDIA App folder contains `NvBackend/Plugins/LocalUser/NvBackend64.dll`, the ontology component, and `ApplicationOntology/data/fingerprint.db`.
- `fingerprint.db` is XML with `FingerprintDB/Fingerprint/Version`, not SQLite. Profiles contain `CMSID`, `DriverProfile`, and optional `Disable_FG_Override` fields. Missing disable fields are represented as false in the installed-application model. This is a deny-list setting, not positive FG capability evidence.
- The current per-user backend stores detected installation records in `ApplicationStorage.json`, under `Applications[]`, with `LocalId` and a nested `Application` object.
- Relevant observed fields are `DisplayName`, `InstallDirectory`, `DetectedFiles`, `ImageFiles`, `DriverProfile`, `CmsId`, `ShortName`, `Version`, `IsFingerprintDetected`, `IsCreativeApplication`, and `Disable_FG_Override`.
- The native backend contains the `nvngx_dlssg.dll` detector name. Our independent reader verifies that named x64 PE in a detected install tree; it does not copy native scanner code.
- The frontend distinguishes scan/NGX support information from GPU and deny-list constraints. `ngxdlssoverridestate.json` also exists, with `stateMaskFG` and `gameScanMask`; this integration deliberately does not guess the undocumented bit meanings or conflate these with frontend `supportState` enums.

## Implemented contract

Read the installed inventory, optionally match one exact ontology version, resolve only recorded executable paths, reject FG-denied/unknown profiles, and check for the FG component. Accepting a candidate is not proof of in-game FG, D3D12 use, MFG multiplier availability, or compatibility with this mod. NVIDIA's GPU restriction is not used to reject RTX 30.

No store manifests, game registry keys, general EXE crawling, database modification, command strings from metadata, NVIDIA binary loading, private RPC calls, or copied fingerprint database are used. The installed NVIDIA App must perform the initial scan. Refresh in MFG Enabler only reloads its saved output.

The cache and ontology files are read with shared access. Broken inventory data fails closed. A broken optional ontology file is reported, and only explicit inventory fields can then qualify a game. An exact profile match requires CMS ID, short name and version together. Paths outside the install root, reparse points, non-x64 executables and invalid FG binaries are not accepted.

Persisted MFG Enabler library records never retain activation eligibility. Every launch and pre-install check uses current NVIDIA data. Previously applied games absent from the new candidates remain recovery-only; old unmodified store-discovered entries are removed.

## Verification boundary

The machine's NVIDIA cache currently contains one application (Steam). It is excluded by its FG-denied profile, so the live read-only check produces zero FG candidates. Positive game cases use synthetic fixtures, not a claim that a real installed game was detected or tested. Real-game FG performance has not been tested.

Neither the supplied NVIDIA program tree nor the user's cached application inventory is included in the source or portable ZIP.
