# GPT Usage Tray

Codex에 로그인된 ChatGPT 계정의 사용량을 Windows 알림 영역에 표시합니다.

- 작업표시줄 위젯: 작업표시줄 맨 왼쪽에 배경 없이 색상 원형 바와 숫자 표시
- 숫자 색상: Windows 다크 모드에서는 흰색, 라이트 모드에서는 검정으로 자동 전환
- 색상: 잔여량 모드(초록 → 노랑 → 빨강), 단일색, 시작·끝 색상을 정하는 커스텀 모드. 자동 저장
- 위젯 클릭: 주간·5시간 잔여량 카드, 초기화 시각과 색상 설정 표시
- 상세 창: Windows 앱의 라이트·다크 설정에 맞춰 자동 전환. Pretendard 글꼴 포함
- 위젯 우클릭: 상세 보기, 새로고침 또는 앱 종료
- 60초마다 자동 갱신
- Windows 로그인 시 자동 실행
- 우클릭 메뉴에서 상세 보기, 새로고침, 종료 가능

Codex가 설치되어 있고 ChatGPT 계정으로 로그인되어 있어야 합니다. 별도 API 키를 사용하지 않으며, 설치된 Codex app-server의 읽기 인터페이스를 이용합니다. Codex 업데이트로 app-server 프로토콜이 바뀌면 앱 업데이트가 필요할 수 있습니다.

기존 알림 영역의 작은 아이콘은 사용하지 않습니다. 44×44 원형 게이지 하나만 작업표시줄 맨 왼쪽에 표시합니다.

## 설치

GitHub 최신 릴리스의 **My Windows Apps Setup.exe**를 실행하고 GPT Usage Tray를 선택해 설치합니다. `%LOCALAPPDATA%\Programs\GPTUsageTray`에 설치되고 바탕화면 바로가기와 Windows 자동 실행이 설정됩니다.

개인 제작 앱으로 코드 서명이 없습니다. Windows Smart App Control이 켜진 PC에서는 실행을 차단할 수 있습니다.

## 글꼴 및 UI 확인

상세 창은 [Pretendard](https://github.com/orioncactus/pretendard) Regular/Bold를 실행 파일에 포함합니다. 별도 글꼴 설치는 필요 없으며 라이선스는 `Pretendard-OFL.txt`에 있습니다. 원형 위젯의 글꼴·색상 계산·그리기 코드는 변경하지 않습니다.

개발 시 `tests/Test-GptUsageDetails.ps1`로 색상 설정, 테마, 새로고침과 배율별 배치를 확인할 수 있습니다. `GPT Usage Tray.exe --details-preview output.png light` 또는 `dark`는 계정에 연결하지 않고 가상 데이터 화면을 만듭니다.
