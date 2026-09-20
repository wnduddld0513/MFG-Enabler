#define UNICODE
#define _UNICODE

#include <windows.h>
#include <shellapi.h>
#include <stdio.h>
#include <wchar.h>

#define WM_TRAY_CALLBACK (WM_APP + 1)
#define WM_STORAGE_CHANGED (WM_APP + 2)
#define WM_SCAN_DONE (WM_APP + 3)
#define ID_TRAY_OPEN 1001
#define ID_TRAY_EXIT 1002
#define ID_SCAN_TIMER 1003
#define IDI_MFG_ENABLER 101
#define SCAN_DEBOUNCE_MS 1200

#ifdef MFG_TRAY_TEST
static const wchar_t *TRAY_CLASS = L"MFGEnablerTrayTestWindow";
static const wchar_t *TRAY_MUTEX = L"Local\\MFG-Enabler-Tray-Test";
#else
static const wchar_t *TRAY_CLASS = L"MFGEnablerTrayWindow";
static const wchar_t *TRAY_MUTEX = L"Local\\MFG-Enabler-Tray-1";
#endif
static const wchar_t *WATCH_FILE = L"ApplicationStorage.json";

static HWND g_window;
static HANDLE g_stop_event;
static HANDLE g_watch_thread;
static HICON g_icon;
static UINT g_taskbar_created;
static HANDLE g_worker, g_worker_wait;
static BOOL g_scan_pending, g_open_pending;

static VOID CALLBACK scan_completed(PVOID context, BOOLEAN timed_out)
{
    (void)context; (void)timed_out;
    PostMessageW(g_window, WM_SCAN_DONE, 0, 0);
}

static BOOL has_switch(const wchar_t *wanted)
{
    int argc = 0;
    LPWSTR *argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (argv == NULL) return FALSE;
    BOOL found = FALSE;
    for (int i = 1; i < argc; ++i) {
        if (_wcsicmp(argv[i], wanted) == 0) {
            found = TRUE;
            break;
        }
    }
    LocalFree(argv);
    return found;
}

static BOOL sibling_path(const wchar_t *name, wchar_t *output, DWORD capacity)
{
    DWORD length = GetModuleFileNameW(NULL, output, capacity);
    if (length == 0 || length >= capacity) return FALSE;
    wchar_t *slash = wcsrchr(output, L'\\');
    if (slash == NULL) return FALSE;
    size_t prefix = (size_t)(slash - output + 1);
    size_t name_length = wcslen(name);
    if (prefix + name_length + 1 > capacity) return FALSE;
    wcscpy(output + prefix, name);
    return TRUE;
}

static void launch_main(BOOL background)
{
    if (g_worker != NULL) {
        if (background) g_scan_pending = TRUE; else g_open_pending = TRUE;
        return;
    }
    wchar_t exe[MAX_PATH];
    wchar_t command[2 * MAX_PATH + 64];
    if (!sibling_path(L"MFG-Enabler.exe", exe, MAX_PATH)) return;

    int written = background
        ? swprintf(command, sizeof(command) / sizeof(command[0]), L"\"%ls\" --background-scan", exe)
        : swprintf(command, sizeof(command) / sizeof(command[0]), L"\"%ls\"", exe);
    if (written <= 0) return;

    STARTUPINFOW startup;
    PROCESS_INFORMATION process;
    ZeroMemory(&startup, sizeof(startup));
    ZeroMemory(&process, sizeof(process));
    startup.cb = sizeof(startup);
    if (CreateProcessW(exe, command, NULL, NULL, FALSE, 0, NULL, NULL, &startup, &process)) {
        CloseHandle(process.hThread);
        if (background) {
            g_worker = process.hProcess;
            if (!RegisterWaitForSingleObject(&g_worker_wait, g_worker, scan_completed, NULL, INFINITE, WT_EXECUTEONLYONCE)) {
                CloseHandle(g_worker); g_worker = NULL;
            }
        } else CloseHandle(process.hProcess);
    }
}

static HICON load_tray_icon(void)
{
    HICON embedded = LoadIconW(GetModuleHandleW(NULL), MAKEINTRESOURCEW(IDI_MFG_ENABLER));
    if (embedded != NULL) return embedded;

    wchar_t path[MAX_PATH];
    if (sibling_path(L"Assets\\MFG-Enabler.ico", path, MAX_PATH)) {
        HICON icon = (HICON)LoadImageW(NULL, path, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
        if (icon != NULL) return icon;
    }
    return LoadIconW(NULL, IDI_APPLICATION);
}

static void add_tray_icon(void)
{
    NOTIFYICONDATAW data;
    ZeroMemory(&data, sizeof(data));
    data.cbSize = sizeof(data);
    data.hWnd = g_window;
    data.uID = 1;
    data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    data.uCallbackMessage = WM_TRAY_CALLBACK;
    data.hIcon = g_icon;
    lstrcpynW(data.szTip, L"MFG Enabler", (int)(sizeof(data.szTip) / sizeof(data.szTip[0])));
    Shell_NotifyIconW(NIM_ADD, &data);
}

static void remove_tray_icon(void)
{
    NOTIFYICONDATAW data;
    ZeroMemory(&data, sizeof(data));
    data.cbSize = sizeof(data);
    data.hWnd = g_window;
    data.uID = 1;
    Shell_NotifyIconW(NIM_DELETE, &data);
}

static void show_tray_menu(void)
{
    HMENU menu = CreatePopupMenu();
    if (menu == NULL) return;
    AppendMenuW(menu, MF_STRING, ID_TRAY_OPEN, L"Open MFG Enabler");
    AppendMenuW(menu, MF_SEPARATOR, 0, NULL);
    AppendMenuW(menu, MF_STRING, ID_TRAY_EXIT, L"Exit");
    POINT point;
    GetCursorPos(&point);
    SetForegroundWindow(g_window);
    TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_BOTTOMALIGN | TPM_LEFTALIGN,
                   point.x, point.y, 0, g_window, NULL);
    PostMessageW(g_window, WM_NULL, 0, 0);
    DestroyMenu(menu);
}

static BOOL is_storage_notification(const BYTE *buffer, DWORD bytes)
{
    DWORD offset = 0;
    while (offset < bytes) {
        const FILE_NOTIFY_INFORMATION *info = (const FILE_NOTIFY_INFORMATION *)(buffer + offset);
        size_t chars = info->FileNameLength / sizeof(wchar_t);
        if (chars == wcslen(WATCH_FILE) && _wcsnicmp(info->FileName, WATCH_FILE, chars) == 0)
            return TRUE;
        if (info->NextEntryOffset == 0) break;
        offset += info->NextEntryOffset;
    }
    return FALSE;
}

static BOOL build_watch_path(wchar_t *path, DWORD capacity)
{
    DWORD length = GetEnvironmentVariableW(L"LOCALAPPDATA", path, capacity);
    if (length == 0 || length >= capacity) return FALSE;
    const wchar_t *suffix = L"\\NVIDIA Corporation\\NVIDIA App\\NvBackend";
    size_t suffix_length = wcslen(suffix);
    if ((size_t)length + suffix_length + 1 > capacity) return FALSE;
    wcscat(path, suffix);
    return TRUE;
}

static DWORD WINAPI watch_storage(LPVOID unused)
{
    (void)unused;
    wchar_t directory[MAX_PATH];
    if (!build_watch_path(directory, MAX_PATH)) return 0;

    for (;;) {
        if (WaitForSingleObject(g_stop_event, 0) == WAIT_OBJECT_0) break;

        HANDLE handle = CreateFileW(directory, FILE_LIST_DIRECTORY,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            NULL, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OVERLAPPED, NULL);
        if (handle == INVALID_HANDLE_VALUE) {
            if (WaitForSingleObject(g_stop_event, 30000) == WAIT_OBJECT_0) break;
            continue;
        }
        PostMessageW(g_window, WM_STORAGE_CHANGED, 0, 0);

        HANDLE changed = CreateEventW(NULL, TRUE, FALSE, NULL);
        if (changed == NULL) {
            CloseHandle(handle);
            break;
        }

        BYTE buffer[8192];
        OVERLAPPED overlap;
        ZeroMemory(&overlap, sizeof(overlap));
        overlap.hEvent = changed;

        for (;;) {
            DWORD bytes = 0;
            ResetEvent(changed);
            if (!ReadDirectoryChangesW(handle, buffer, sizeof(buffer), FALSE,
                    FILE_NOTIFY_CHANGE_FILE_NAME | FILE_NOTIFY_CHANGE_LAST_WRITE | FILE_NOTIFY_CHANGE_SIZE,
                    NULL, &overlap, NULL)) {
                break;
            }

            HANDLE waits[2] = { g_stop_event, changed };
            DWORD wait = WaitForMultipleObjects(2, waits, FALSE, INFINITE);
            if (wait == WAIT_OBJECT_0) {
                CancelIoEx(handle, &overlap);
                GetOverlappedResult(handle, &overlap, &bytes, TRUE);
                CloseHandle(changed);
                CloseHandle(handle);
                return 0;
            }
            if (wait != WAIT_OBJECT_0 + 1) break;

            bytes = 0;
            if (!GetOverlappedResult(handle, &overlap, &bytes, FALSE)) break;
            if (bytes == 0 || is_storage_notification(buffer, bytes))
                PostMessageW(g_window, WM_STORAGE_CHANGED, 0, 0);
            ZeroMemory(&overlap, sizeof(overlap));
            overlap.hEvent = changed;
        }

        CancelIoEx(handle, &overlap);
        DWORD discarded = 0;
        GetOverlappedResult(handle, &overlap, &discarded, TRUE);
        CloseHandle(changed);
        CloseHandle(handle);
        if (WaitForSingleObject(g_stop_event, 5000) == WAIT_OBJECT_0) break;
    }
    return 0;
}

static LRESULT CALLBACK window_proc(HWND hwnd, UINT message, WPARAM wparam, LPARAM lparam)
{
    if (message == g_taskbar_created) {
        add_tray_icon();
        return 0;
    }

    switch (message) {
    case WM_SCAN_DONE:
        if (g_worker_wait != NULL) { UnregisterWaitEx(g_worker_wait, INVALID_HANDLE_VALUE); g_worker_wait = NULL; }
        if (g_worker != NULL) { CloseHandle(g_worker); g_worker = NULL; }
        if (g_open_pending) { g_open_pending = FALSE; g_scan_pending = FALSE; launch_main(FALSE); }
        else if (g_scan_pending) { g_scan_pending = FALSE; launch_main(TRUE); }
        return 0;
    case WM_TRAY_CALLBACK:
        if (lparam == WM_LBUTTONUP || lparam == WM_LBUTTONDBLCLK) launch_main(FALSE);
        else if (lparam == WM_RBUTTONUP) show_tray_menu();
        return 0;
    case WM_STORAGE_CHANGED:
        KillTimer(hwnd, ID_SCAN_TIMER);
        SetTimer(hwnd, ID_SCAN_TIMER, SCAN_DEBOUNCE_MS, NULL);
        return 0;
    case WM_TIMER:
        if (wparam == ID_SCAN_TIMER) {
            KillTimer(hwnd, ID_SCAN_TIMER);
            launch_main(TRUE);
        }
        return 0;
    case WM_COMMAND:
        if (LOWORD(wparam) == ID_TRAY_OPEN) launch_main(FALSE);
        else if (LOWORD(wparam) == ID_TRAY_EXIT) DestroyWindow(hwnd);
        return 0;
    case WM_CLOSE:
        DestroyWindow(hwnd);
        return 0;
    case WM_DESTROY:
        KillTimer(hwnd, ID_SCAN_TIMER);
        remove_tray_icon();
        if (g_stop_event != NULL) SetEvent(g_stop_event);
        PostQuitMessage(0);
        return 0;
    default:
        return DefWindowProcW(hwnd, message, wparam, lparam);
    }
}

int WINAPI WinMain(HINSTANCE instance, HINSTANCE previous, LPSTR command_line, int show)
{
    (void)previous;
    (void)command_line;
    (void)show;

    if (has_switch(L"--quit")) {
        HWND existing = FindWindowW(TRAY_CLASS, NULL);
        if (existing != NULL) PostMessageW(existing, WM_CLOSE, 0, 0);
        return 0;
    }

    HANDLE mutex = CreateMutexW(NULL, TRUE, TRAY_MUTEX);
    if (mutex == NULL) return 1;
    if (GetLastError() == ERROR_ALREADY_EXISTS) {
        CloseHandle(mutex);
        return 0;
    }

    g_taskbar_created = RegisterWindowMessageW(L"TaskbarCreated");
    g_icon = load_tray_icon();
    WNDCLASSEXW window_class;
    ZeroMemory(&window_class, sizeof(window_class));
    window_class.cbSize = sizeof(window_class);
    window_class.lpfnWndProc = window_proc;
    window_class.hInstance = instance;
    window_class.hIcon = g_icon;
    window_class.hIconSm = g_icon;
    window_class.lpszClassName = TRAY_CLASS;
    if (!RegisterClassExW(&window_class)) {
        ReleaseMutex(mutex);
        CloseHandle(mutex);
        return 1;
    }

    g_window = CreateWindowExW(WS_EX_TOOLWINDOW, TRAY_CLASS, L"MFG Enabler Tray",
        WS_OVERLAPPED, 0, 0, 0, 0, NULL, NULL, instance, NULL);
    if (g_window == NULL) {
        ReleaseMutex(mutex);
        CloseHandle(mutex);
        return 1;
    }

    add_tray_icon();
    g_stop_event = CreateEventW(NULL, TRUE, FALSE, NULL);
    if (g_stop_event != NULL)
        g_watch_thread = CreateThread(NULL, 0, watch_storage, NULL, 0, NULL);

    MSG message;
    while (GetMessageW(&message, NULL, 0, 0) > 0) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }

    if (g_stop_event != NULL) SetEvent(g_stop_event);
    if (g_watch_thread != NULL) {
        WaitForSingleObject(g_watch_thread, INFINITE);
        CloseHandle(g_watch_thread);
    }
    if (g_stop_event != NULL) CloseHandle(g_stop_event);
    if (g_worker_wait != NULL) UnregisterWaitEx(g_worker_wait, INVALID_HANDLE_VALUE);
    if (g_worker != NULL) CloseHandle(g_worker);
    ReleaseMutex(mutex);
    CloseHandle(mutex);
    return 0;
}
