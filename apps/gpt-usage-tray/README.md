# GPT Usage Tray

Codex에 로그인된 ChatGPT 계정의 사용량을 Windows 알림 영역에 표시합니다.

- 아이콘: Pro 계정의 주간 잔여 비율을 원형 진행 링과 가운데 숫자로 표시
- 색상: 잔여량이 줄어들수록 초록 → 노랑 → 빨강으로 변화
- 아이콘 클릭: 단기/주간 잔여량, 초기화 시각, 오늘·누적 토큰 표시
- 60초마다 자동 갱신
- Windows 로그인 시 자동 실행
- 우클릭 메뉴에서 새로고침, 자동 실행 설정, 종료 가능

Codex가 설치되어 있고 ChatGPT 계정으로 로그인되어 있어야 합니다. 별도 API 키를 사용하지 않으며, 설치된 Codex app-server의 읽기 인터페이스를 이용합니다. Codex 업데이트로 app-server 프로토콜이 바뀌면 앱 업데이트가 필요할 수 있습니다.

## 설치

GitHub Release의 ZIP을 내려받아 압축을 푼 뒤 **설치.cmd**를 실행합니다. `%LOCALAPPDATA%\Programs\GPTUsageTray`에 설치되고 바탕화면 바로가기와 Windows 자동 실행이 설정됩니다.

개인 제작 앱으로 코드 서명이 없습니다. Windows Smart App Control이 켜진 PC에서는 실행을 차단할 수 있습니다.
