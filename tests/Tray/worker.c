#define UNICODE
#define _UNICODE
#include <windows.h>
#include <wchar.h>
static void record(char value) {
    wchar_t path[MAX_PATH];
    GetModuleFileNameW(NULL, path, MAX_PATH);
    wcscpy(wcsrchr(path, L'\\') + 1, L"calls.txt");
    HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_ALWAYS, 0, NULL);
    DWORD written;
    WriteFile(file, &value, 1, &written, NULL);
    CloseHandle(file);
}
int WINAPI WinMain(HINSTANCE a, HINSTANCE b, LPSTR c, int d) {
    (void)a; (void)b; (void)c; (void)d;
    record('S'); Sleep(3000); record('E'); return 0;
}
