# NVAPI 프록시 독립 빌드

이 폴더에 현재 앱의 NVAPI 프록시 소스와 고정 버전 컴파일러를 함께 보관합니다.
`nvapi_proxy.c`가 실제 빌드 소스이며, 기존 프록시 동작은 변경하지 않았습니다.

## 포함 파일

- `nvapi_proxy.c`: NVAPI DLL 소스.
- `build.ps1`: Windows x64 DLL 빌드 스크립트.
- `tools/zig-0.15.2.zip`: Zig 0.15.2 Windows x64 공식 배포본(라이선스 포함).
- `LICENSE`: 프록시 소스의 MIT 라이선스.
- `bin/nvapi64.dll`: 빌드 출력. 처음에는 기존 빌드 DLL을 보관하며 빌드할 때 재생성합니다.
- `.cache/`: 압축 해제한 컴파일러. 없어도 동봉된 ZIP에서 복원합니다.

## DLL만 빌드

Windows x64에서 이 폴더의 `build.ps1`을 실행합니다. 별도 NVIDIA SDK나 Visual Studio 없이 동봉된 Zig로 컴파일하며, DLL 빌드에는 인터넷이 필요하지 않습니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

컴파일러 ZIP의 SHA-256은 매 빌드마다 검증합니다.
원본: https://ziglang.org/download/0.15.2/zig-x86_64-windows-0.15.2.zip

SHA-256: `3a0ed1e8799a2f8ce2a6e6290a9ff22e6906f8227865911fb7ddedc3cc14cb0c`

## 앱 전체 빌드

VS 프로젝트 루트의 `build.ps1` 또는 Visual Studio의 빌드를 사용합니다.
앱 빌드가 이 스크립트를 먼저 실행하고 새 DLL을 `spoof5080.dll`이라는 내부 리소스명으로 포함합니다.
전체 앱 빌드에는 .NET 10 SDK, WinUI 개발 구성 요소 및 NuGet 복원이 필요합니다.

다른 PC로 이전할 때는 `MFG-Enabler-VS2026` 폴더 전체를 복사하세요.
이 폴더의 `tools` 압축본은 빌드 입력이므로 유지해야 합니다.
`bin`과 `.cache`는 재생성할 수 있습니다. 기존 프로젝트 루트의 `.cache/native` 및 `.cache/toolchains`는 이제 빌드에 사용하지 않습니다.
