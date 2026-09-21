# MFG Enabler 1.4

- **MFG 오버라이드:** 감지된 DX12 프레임 생성 지원 게임에 런타임 DLL 자동 설치. 제외 게임과 앞으로 발견되는 게임의 자동 적용을 설정할 수 있습니다.
- **게임에 맞는 DLL 자동 선택:** 게임에 가장 적절한 DLL 파일을 자동으로 찾아 MFG 적용에 사용합니다. 기존 파일과의 충돌을 확인하고, 게임 실행 파일이 바뀌면 다시 선택합니다.
- **NVIDIA 인스펙터 방식 FG 설정:** 1.3에서 도입한 전역·게임별 FG 프리셋, 고정·동적 배수, 목표 FPS 설정을 1.4에서 완성했습니다. DLSS SR/RR 프리셋도 지원합니다.
- **백그라운드 트레이:** NVIDIA App 게임 목록 변경을 감시하고 필요할 때만 자동 적용 작업을 실행합니다.
- 설정창 스크롤바 겹침 수정, 버튼·메뉴 크기 정리, Smooth Motion 메뉴 제거, DX12 문자열 검색 효율 개선.
- 1.4부터 Beta 채널도 최신 정식 버전을 함께 받습니다. 기존 1.4b2 사용자는 한 번 Stable 채널로 전환해 업데이트하거나 1.4 ZIP을 설치하세요.

트레이 대기 30초 실측: CPU 0.00%, RAM 약 10~15MiB. 이 PC에서의 측정이며 검색·설치 중 사용량과 다른 환경의 사용량은 달라집니다.

## English

- **MFG override:** automatic runtime DLL installation for detected DX12 frame-generation games, with exclusions and optional application to future games.
- **Automatic DLL selection:** finds the best-matching DLL for each game and uses it to apply MFG, checks existing-file conflicts, and refreshes the selection when the rendering executable changes.
- **NVIDIA Profile Inspector-style FG controls:** introduced in 1.3 and completed in 1.4; global/per-game presets, fixed/dynamic multipliers and target FPS, plus DLSS SR/RR presets.
- **Background tray:** watches NVIDIA App library changes and starts a worker only when needed.
- Corrected scrollbar spacing, compact controls, removed Smooth Motion controls, and more efficient DX12 string scanning.
- From 1.4, Beta also receives the latest stable releases. Existing 1.4b2 users must switch once to Stable or install the 1.4 ZIP manually.

Extract the complete ZIP, including `MFG-Enabler.Tray.exe`, before running `MFG-Enabler.exe`. Static API/proxy detection is evidence, not a guarantee of the renderer or DLL actually loaded by every game. Vulkan remains opt-in; unknown APIs are excluded from automatic installation.
