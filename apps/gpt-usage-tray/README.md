# GPT Usage Tray

ChatGPT · Claude · Gemini의 주간 남은 사용량을 Windows 작업표시줄 왼쪽에 한 번에 표시합니다.

## 화면

- **작업표시줄**: AI마다 색 점 + 남은 % 숫자 + 남은 양 밑줄을 GPT · Claude · Gemini 순서로 표시합니다.
  - 남은 양이 20% 미만이면 숫자가 빨간색이 됩니다.
  - 값을 아직 못 읽었으면 회색 `–`, 연결된 AI가 하나도 없으면 회색 `?`가 표시됩니다.
- **자동 숨김**: 이 PC에서 쓰지 않는(설치·로그인하지 않은) AI는 자동으로 빠지고 나머지만 빈틈없이 붙여 표시합니다. 60초마다 다시 확인하므로 나중에 로그인하면 자동으로 나타납니다.
- **클릭**: 각 AI 칸(숫자 주변 빈 공간 포함)을 누르면 그 AI의 상세 화면이 바로 열립니다.
- **상세 화면**: 오른쪽 위 GPT / Claude / Gemini 버튼으로 전환하고, 5시간·주간 잔여량과 초기화 시각을 보여줍니다. 연결되지 않은 AI를 누르면 연결 방법이 나옵니다.
- **색상**: AI마다 기본 색상 또는 슬라이더로 점·밑줄 색을 고를 수 있으며 자동 저장됩니다.
- 우클릭으로 새로고침·종료, Windows 로그인 시 자동 실행, 다크·라이트 작업표시줄에 맞춰 숫자 색 자동 전환.

## AI별 연결 방법

| AI | 필요한 것 | 읽는 곳 |
|---|---|---|
| GPT | **Codex 앱** 설치 + ChatGPT 계정 로그인 | Codex app-server |
| Claude | **Claude Code CLI** 설치 + 로그인 | `%USERPROFILE%\.claude\.credentials.json` → Claude 사용량 조회 |
| Gemini | **Antigravity 앱** 설치 + Google 계정 로그인 | 실행 중인 Antigravity 언어 서버 |

- **Claude**: Windows용 Claude 데스크톱 앱 로그인만으로는 표시되지 않습니다. PowerShell에서 `irm https://claude.ai/install.ps1 | iex`로 CLI를 설치한 뒤 `claude`를 실행해 로그인하세요. `claude`를 찾을 수 없다고 나오면 `& "$env:USERPROFILE\.local\bin\claude.exe"`로 실행합니다. 로그인이 만료되면 앱이 자동으로 갱신합니다.
- **Gemini**: Antigravity가 켜져 있을 때만 새 값을 읽고, 닫으면 마지막 값을 유지합니다.
- Claude 사용량 조회는 5분 간격이며, 서버 제한(HTTP 429)이 오면 마지막 값을 유지하며 최대 30분까지 늦춥니다. 일시적인 오류가 나도 마지막 값을 유지하고 상세 화면에 오류를 표시합니다.

세 경로 모두 별도 API 키 없이 각 앱의 로그인 정보를 쓰는 비공개 인터페이스라, 각 회사가 바꾸면 앱 업데이트가 필요할 수 있습니다.

## 설치

GitHub 최신 릴리스의 **My Windows Apps Setup.exe**를 실행하고 GPT Usage Tray를 선택해 설치합니다. `%LOCALAPPDATA%\Programs\GPTUsageTray`에 설치되고 바탕화면 바로가기와 Windows 자동 실행이 설정됩니다.

개인 제작 앱으로 코드 서명이 없습니다. Windows Smart App Control이 켜진 PC에서는 실행을 차단할 수 있습니다.
