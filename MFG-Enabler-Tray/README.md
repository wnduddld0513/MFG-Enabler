# Native tray companion

`MFG-Enabler.Tray.exe` is a Win32 C companion with the same icon as the main application. It uses `ReadDirectoryChangesW` on NVIDIA App's `NvBackend` directory, filters `ApplicationStorage.json`, and debounces changes for 1.2 seconds. It checks once at startup, handles file replacement and notification overflow, and queues a follow-up scan if the worker is still running. A missing directory is retried every 30 seconds; existing directories use an event wait rather than continuous polling.

The companion launches `MFG-Enabler.exe --background-scan`. The worker enters before XAML initialization, shares a mutex with the UI, and exits after scanning/applying. The open interface handles its own library notifications. Tray **Open** launches or restores the interface; **Exit** stops the tray. `--quit` requests a clean exit, including cancellation of pending directory I/O. Automatic application and Windows startup are configured in the main application's global MFG options.

Build from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File MFG-Enabler-Tray/build.ps1
```

The script verifies and extracts the bundled Zig 0.15.2 archive and compiles with `-Wall -Wextra -Werror`. Output is `MFG-Enabler-Tray/bin/MFG-Enabler.Tray.exe`; the main build scripts copy it into packages.

Run `tests/Tray/run.ps1` for isolated startup, debounce, queued-worker, rename and shutdown tests. The test build uses its own window class/mutex and a temporary NVIDIA storage path, so it does not control the user's normal tray. One measured test run used about 10 MiB working set; this varies by system.
