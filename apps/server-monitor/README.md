# Lab Server Monitor

SSH로 연구실 서버에 연결해 GPU 메모리·사용률·온도와 RAM 사용량을 1초마다 표시합니다.

## 로그인 추가

1. 앱 상단의 **로그인 관리**를 누릅니다.
2. 서버 주소, 사용자 이름, 비밀번호, SSH 서버 키 지문을 입력합니다.
3. **저장**을 누르면 즉시 서버 카드가 갱신됩니다.

로그인 목록과 비밀번호는 `%LOCALAPPDATA%\LabServerMonitor`에만 저장됩니다. 설치 파일이나 GitHub 저장소에는 포함되지 않습니다.

SSH 연결에는 PuTTY 프로젝트의 Plink를 사용합니다. 라이선스 정보는 함께 설치되는 `PUTTY-LICENSE.html`을 참고하세요.

