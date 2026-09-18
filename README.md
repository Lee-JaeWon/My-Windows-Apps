# My Windows Apps

연구실에서 쓰려고 만든 가벼운 Windows 데스크톱 앱 모음입니다. C# Windows Forms / .NET Framework 4.x 기반입니다.

| 앱 | 주요 기능 |
| --- | --- |
| **GIF Generator** (v1.4) | MP4 → GIF, MP4 배속 변환 |
| **Lab Server Monitor** (v1.0.2) | SSH로 서버 GPU·RAM 상태 확인 |
| **GPT Usage Tray** (v1.1.1) | Codex Pro 주간 사용량을 작업표시줄 원형 위젯으로 표시 |

## GIF Generator

- **GIF 탭:** 최대 50MB(50,000,000바이트), 999프레임에 맞춰 해상도와 FPS를 자동 조절합니다.
- **MP4 배속 탭:** 0.25~16배속 MP4를 만들고, 소리는 음높이를 유지하며 배속합니다.
- 파일 끌어놓기, 저장 위치 선택, 취소, 초기화를 지원합니다.
- 변환은 로컬에서 수행하며 원본 파일을 덮어쓰지 않습니다.

## Lab Server Monitor

- 서버별 연결 상태, GPU별 사용/전체 메모리(MiB), 사용률(%), 온도(°C), RAM 사용량(GiB)을 표시합니다.
- SSH 연결을 유지하며 **1초 간격**으로 갱신하고, 연결이 끊기면 자동 재접속합니다.
- 서버에 설치 파일을 남기지 않으며 앱 종료 시 조회 작업을 종료합니다.
- 서버에는 Python 3와 `nvidia-smi`가 필요합니다. 연결 상태는 SSH 기준이며 물리적 전원 상태를 직접 측정하지 않습니다.

## GPT Usage Tray

- Codex Pro의 주간 잔여 사용량을 작업표시줄 맨 왼쪽의 원형 바와 숫자로 표시합니다.
- 기존 알림 영역의 작은 아이콘은 사용하지 않으며, 원형 위젯을 누르면 상세 창이 열립니다.
- 잔여량이 줄어들수록 링 색상이 초록 → 노랑 → 빨강으로 바뀝니다.
- 1분마다 갱신하며, 아이콘을 누르면 초기화 시각과 오늘·누적 토큰을 확인할 수 있습니다.
- Windows 로그인 시 자동 실행되고, 상주 작업 집합은 테스트 PC에서 약 7~17MB였습니다.
- 설치된 Codex와 ChatGPT 로그인을 사용하므로 API 키가 필요하지 않습니다.
- [GPT Usage Tray v1.1.1 다운로드](https://github.com/Lee-JaeWon/My-Windows-Apps/releases/tag/gpt-usage-tray-v1.1.1)

## 빌드 및 실행

Windows 10/11 64비트에서 PowerShell로 실행합니다.

```powershell
.\build.ps1
```

생성된 실행 파일은 `dist` 아래 각 앱 폴더에 있습니다. 별도 실행 의존성을 다음처럼 배치하세요.

```text
dist/
  GIF Generator/
    GIF Generator.exe
    tools/
      ffmpeg.exe
      ffprobe.exe
  Lab Server Monitor/
    Lab Server Monitor.exe
    plink.exe
    collector.sh
    settings/
      servers.json
      server1.password.txt
  GPT Usage Tray/
    GPT Usage Tray.exe
    설치.cmd
    Install.ps1
```

- GIF 변환 도구: [FFmpeg Windows 빌드](https://www.gyan.dev/ffmpeg/builds/)
- SSH 클라이언트: [PuTTY Plink 공식 배포처](https://www.chiark.greenend.org.uk/~sgtatham/putty/latest.html)
- 기존 도구가 있다면 `build.ps1 -FFmpegDirectory "C:\tools\ffmpeg\bin" -PlinkPath "C:\tools\plink.exe"`로 함께 복사할 수 있습니다.
- 외부 실행 파일은 저장소에 포함하지 않습니다. 외부 도구의 라이선스는 각 배포처를 참고하세요.

### 서버 설정

1. `dist/Lab Server Monitor/settings/servers.example.json`을 `servers.json`으로 복사합니다.
2. 서버 주소, 사용자 이름, 신뢰할 수 있는 SSH 호스트 키의 SHA256 지문을 입력합니다. `Servers` 배열에 여러 서버를 등록할 수 있습니다.
3. `PasswordFile`에 지정한 파일을 같은 폴더에 만들고 비밀번호만 저장합니다. 공백도 비밀번호의 일부이므로 보존하세요.

**실제 서버 주소·로그인 정보·비밀번호·조회 결과는 저장소에 포함하지 않습니다.** 개인 설정과 비밀번호 파일은 `.gitignore`로 제외합니다. 비밀번호 파일은 현재 Windows 사용자만 접근하도록 관리하세요.

개인 제작 앱으로 코드 서명이 없으므로 일부 PC에서는 Windows 보안 정책에 따라 실행이 차단될 수 있습니다.
