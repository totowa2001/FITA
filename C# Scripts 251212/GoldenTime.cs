// 스크립트 이름 : ServerManagerGoldenTime.cs
// 스크립트 기능 : FastAPI 서버에서 골든타임(golden_time) 값을 주기적으로 받아 TMP_Text에 그대로 출력하는 네트워크 매니저(고정 URL 사용 버전)
//                          1. Inspector에 지정된 baseApiUrl을 기준으로 /golden_time/calculate/{user_id} 를 주기적으로 GET 호출한다.
//                          2. 서버 응답(정수)을 파싱해 TMP_Text에 표시한다.
//                          3. (테스트용) /golden_time/fire_start 를 POST 호출할 수 있다.
//                          4. 연결 실패/파싱 실패 시 로그 및 UI 표시(옵션)로 상태를 확인한다.
// 입력 파라미터 :
//      goldenTimeText (TMP_Text)  : (Hierarchy) Canvas 내 TextMeshProUGUI 등을 연결. 서버에서 받은 golden_time을 표시할 UI 텍스트.
//      baseApiUrl (string)        : (Inspector) API 서버 베이스 URL. 예) "http://43.203.39.23:8000" 등
//                                   - 주의: "/docs"는 Swagger UI 페이지이므로 base로 넣지 않는 것을 권장.
//      userId (int)               : (Inspector) 서버 DB의 user.id 값. /calculate/{user_id} 경로에 사용.
//      autoPoll (bool)            : (Inspector) Start() 시 자동 폴링 시작 여부.
//      pollIntervalSec (float)    : (Inspector) /calculate 호출 주기(초).
//      requestTimeoutSec (int)    : (Inspector) UnityWebRequest timeout(초).
//      showHttpErrorsOnText (bool): (Inspector) HTTP 에러를 TMP_Text에도 표시할지 여부(디버깅용).
// 리턴 타입 : 없음 (MonoBehaviour 컴포넌트 스크립트)

using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public class ServerManagerGoldenTime : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text goldenTimeText;

    [Header("Server")]
    [SerializeField] private string baseApiUrl = "http://43.203.39.23:8000"; // 권장: /docs 제외

    [Header("User")]
    [SerializeField] private int userId = 1; // DB에 실제로 존재하는 user.id 값을 넣을 것

    [Header("Polling")]
    [SerializeField] private bool autoPoll = true; // FITA 시작 시 자동으로 Server Polling 시작 여부.
                                                   // 추후 Button 등으로 수동으로 화재대피상황을 시작하게 될 경우 false로 둘 것.
    [SerializeField] private float pollIntervalSec = 5;  // 서버에 골든타임 계산 요청 보내는 인터벌 시간.
                                                            // 현재 로컬에서 카운트다운 계산하므로 길게 두어 5초로 설정.
    [SerializeField] private int requestTimeoutSec = 5; // UnityWebRequest가 응답을 기다리는 최대 시간

    [Header("Debug")]
    [SerializeField] private bool showHttpErrorsOnText = true;  // HTTP 에러 및 실패 이미지를 TMP Text 디버그 텍스트에 표시하는지 여부.
                                                                // 실제 서비스 런칭 시에는 UI 혹은 Icon 등으로 대체할 계획.



    // FastAPI router 기준 엔드포인트 (질문 코드 기준)
    private const string CalculatePathFmt = "/golden_time/calculate/{0}";
    private const string FireStartPath = "/golden_time/fire_start";


    // 로컬 카운트다운 (추후 Server에서 카운트다운 구현 시 삭제)
    private Coroutine _countdownRoutine;
    private int _currentDisplayedSeconds = -1;



    private Coroutine _pollRoutine;

    // 함수 이름 : Start()
    // 함수 기능 : autoPoll이 켜져있다면 폴링 루프를 시작.
    // 입력 파라미터 : 없음
    // 리턴 타입 : void
    private void Start()
    {
        if (autoPoll)
            _pollRoutine = StartCoroutine(PollLoop());
    }

    // 함수 이름 : OnDisable()
    // 함수 기능 : 오브젝트 Disable 시 실행 중인 폴링 코루틴 정리(중복 폴링/누수 방지)
    // 입력 파라미터 : 없음
    // 리턴 타입 : void
    private void OnDisable()
    {
        if (_pollRoutine != null)
        {
            StopCoroutine(_pollRoutine);
            _pollRoutine = null;
        }
        StopLocalCountdown();
    }

    // 함수 이름 : StartPolling()
    // 함수 기능 : (외부 호출용) 수동으로 폴링을 시작. 이미 실행 중이면 중복 실행하지 않음.
    // 입력 파라미터 : 없음
    // 리턴 타입 : void
    public void StartPolling()
    {
        if (_pollRoutine == null)
            _pollRoutine = StartCoroutine(PollLoop());
    }

    // 함수 이름 : StopPolling()
    // 함수 기능 : (외부 호출용) 수동으로 폴링을 중지.
    // 입력 파라미터 : 없음
    // 리턴 타입 : void
    public void StopPolling()
    {
        if (_pollRoutine != null)
        {
            StopCoroutine(_pollRoutine);
            _pollRoutine = null;
        }
        StopLocalCountdown();
    }


    // 함수 이름 : TriggerFireStart()
    // 함수 기능 : (테스트용) 서버의 fire_start API를 호출하여 랜덤 앵커의 fireDT를 갱신(화재 시작 이벤트 트리거)
    // 입력 파라미터 : 없음
    // 리턴 타입 : void
    public void TriggerFireStart()
    {
        StartCoroutine(FireStartOnce());
    }


    // 함수 이름 : PollLoop()
    // 함수 기능 : 전체 폴링 루프의 메인 코루틴
    //              1) baseApiUrl 유효성 간단 점검
    //              2) /calculate를 pollIntervalSec마다 반복 호출하여 TMP_Text 업데이트
    // 입력 파라미터 : 없음
    // 리턴 타입 : IEnumerator (Coroutine)
    private IEnumerator PollLoop()
    {
        if (string.IsNullOrWhiteSpace(baseApiUrl))
        {
            SetText("Base URL missing");
            yield break;
        }

        SetText("Connecting...");

        while (true)
        {
            yield return GetGoldenTimeOnce();
            yield return new WaitForSeconds(pollIntervalSec);
        }
    }


    // 함수 이름 : GetGoldenTimeOnce()
    // 함수 기능 : /golden_time/calculate/{userId} 를 1회 호출하여 golden_time 값을 받아 UI에 출력한다.
    //              - 응답이 정수(예: 123) 또는 문자열 정수(예: "123") 또는 공백/개행 포함 형태라도 파싱 시도
    //              - HTTP 실패 시 로그 + UI 표시(옵션)
    // 입력 파라미터 : 없음
    // 리턴 타입 : IEnumerator (Coroutine)
    private IEnumerator GetGoldenTimeOnce()
    {
        string url = BuildUrl(baseApiUrl, string.Format(CalculatePathFmt, userId));

        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = requestTimeoutSec;
            req.SetRequestHeader("Accept", "application/json");

            yield return req.SendWebRequest();

            if (!IsSuccess(req))
            {
                string msg = $"HTTP {(req.responseCode)}";
                if (!string.IsNullOrEmpty(req.error)) msg += $" | {req.error}";

                Debug.LogWarning($"[ServerManagerGoldenTime] calculate 실패: {msg} | body: {SafeBody(req)}");

                if (showHttpErrorsOnText)
                    SetText(msg);

                yield break;
            }

            if (TryParseIntFlexible(req.downloadHandler.text, out int goldenTime))
            {
                // 1) 처음 수신했을 때만 시작
                if (_countdownRoutine == null)
                {
                    StartOrResetLocalCountdown(goldenTime);
                }
                // 2) 이미 카운트다운 중이면, 서버값이 현재 표시값보다 "크게" 바뀔 때만 리셋(예: 이벤트 발생/재계산 상황)
                else if (goldenTime > _currentDisplayedSeconds + 1)
                {
                    StartOrResetLocalCountdown(goldenTime);
                }
                // 그 외(서버값이 같거나 더 작으면) 로컬 카운트다운 유지
            }
        }
    }


    // 함수 이름 : FireStartOnce()
    // 함수 기능 : (테스트용) /golden_time/fire_start 를 1회 POST 호출한다.
    //              - 성공 시 "Fire started"를 UI에 표시하고, 서버 응답(JSON)을 로그로 출력한다.
    // 입력 파라미터 : 없음
    // 리턴 타입 : IEnumerator (Coroutine)
    private IEnumerator FireStartOnce()
    {
        if (string.IsNullOrWhiteSpace(baseApiUrl))
        {
            SetText("Base URL missing");
            yield break;
        }

        SetText("FireStart...");

        string url = BuildUrl(baseApiUrl, FireStartPath);

        using (var req = UnityWebRequest.PostWwwForm(url, string.Empty))
        {
            req.timeout = requestTimeoutSec;
            req.SetRequestHeader("Accept", "application/json");

            yield return req.SendWebRequest();

            if (!IsSuccess(req))
            {
                string msg = $"HTTP {(req.responseCode)}";
                if (!string.IsNullOrEmpty(req.error)) msg += $" | {req.error}";

                Debug.LogWarning($"[ServerManagerGoldenTime] fire_start 실패: {msg} | body: {SafeBody(req)}");
                if (showHttpErrorsOnText) SetText(msg);
                yield break;
            }

            Debug.Log($"[ServerManagerGoldenTime] fire_start OK: {req.downloadHandler.text}");
            SetText("Fire started");
        }
    }




    // 함수 이름 : IsSuccess()
    // 함수 기능 : Unity 버전에 따라 UnityWebRequest 성공 여부를 일관되게 판정한다.
    // 입력 파라미터 : req (UnityWebRequest) - 완료된 요청 객체
    // 리턴 타입 : bool (성공 true, 실패 false)
    private static bool IsSuccess(UnityWebRequest req)
    {
#if UNITY_2020_2_OR_NEWER
        return req.result == UnityWebRequest.Result.Success;
#else
        return !req.isNetworkError && !req.isHttpError;
#endif
    }

    // 함수 이름 : SafeBody()
    // 함수 기능 : 다운로드 핸들러의 body를 안전하게 가져온다(예외 방지)
    // 입력 파라미터 : req (UnityWebRequest)
    // 리턴 타입 : string (응답 body 또는 빈 문자열)
    private static string SafeBody(UnityWebRequest req)
    {
        try { return req.downloadHandler != null ? req.downloadHandler.text : ""; }
        catch { return ""; }
    }


    // 함수 이름 : BuildUrl()
    // 함수 기능 : baseUrl + path를 결합하여 최종 요청 URL을 만든다(슬래시 중복 방지)
    // 입력 파라미터 :
    //      baseUrl (string) : "http://host:port" 또는 "http://host:port/prefix"
    //      path (string)    : "/golden_time/calculate/1" 처럼 앞에 '/' 포함
    // 리턴 타입 : string (결합된 URL)
    private static string BuildUrl(string baseUrl, string path)
    {
        return $"{TrimTrailingSlash(baseUrl)}{path}";
    }


    // 함수 이름 : TrimTrailingSlash()
    // 함수 기능 : 문자열 끝의 '/'를 제거한다(URL 결합 시 '//' 방지)
    // 입력 파라미터 : s (string)
    // 리턴 타입 : string
    private static string TrimTrailingSlash(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.EndsWith("/") ? s.TrimEnd('/') : s;
    }


    // 함수 이름 : TryParseIntFlexible()
    // 함수 기능 : FastAPI 응답이 정수/문자열정수/개행 포함이어도 최대한 int로 파싱한다.
    // 입력 파라미터 :
    //      raw (string) : 서버 응답 텍스트
    //      value (out int) : 파싱 결과
    // 리턴 타입 : bool (파싱 성공 true)
    private static bool TryParseIntFlexible(string raw, out int value)
    {
        value = 0;
        if (raw == null) return false;

        string t = raw.Trim();

        // "123" 형태 대응
        if (t.Length >= 2 && t[0] == '"' && t[t.Length - 1] == '"')
            t = t.Substring(1, t.Length - 2).Trim();

        return int.TryParse(t, out value);
    }


    // 함수 이름 : SetText()
    // 함수 기능 : TMP_Text가 존재하면 텍스트를 갱신한다(NullRef 방지)
    // 입력 파라미터 : s (string) - 화면에 표시할 문자열
    // 리턴 타입 : void
    private void SetText(string s)
    {
        if (goldenTimeText != null)
            goldenTimeText.text = s;
    }



    //==================================================================================//
    //======================== 아래부터는 로컬 카운트다운 함수. ========================//
    //================== 추후 Server에서 카운트다운 구현 시 삭제할 것 ==================//
    //==================================================================================//


    // 함수 이름 : StartOrResetLocalCountdown()
    // 함수 기능 : 서버에서 받은 초 값을 기준으로 로컬 카운트다운을 시작/리셋한다.
    // 입력 파라미터 : startSeconds (int) - 시작 초(서버에서 받은 골든타임 값)
    // 리턴 타입 : void
    private void StartOrResetLocalCountdown(int startSeconds)
    {
        _currentDisplayedSeconds = Mathf.Max(startSeconds, 0);
        SetText(_currentDisplayedSeconds.ToString());

        if (_countdownRoutine != null)
        {
            StopCoroutine(_countdownRoutine);
            _countdownRoutine = null;
        }

        _countdownRoutine = StartCoroutine(LocalCountdownLoop());
    }

    // 함수 이름 : LocalCountdownLoop()
    // 함수 기능 : 현재 표시 중인 값을 1초마다 -1 감소시키며 TMP_Text를 갱신한다(0 이하로 내려가지 않음).
    // 입력 파라미터 : 없음
    // 리턴 타입 : IEnumerator (Coroutine)
    private IEnumerator LocalCountdownLoop()
    {
        while (_currentDisplayedSeconds > 0)
        {
            yield return new WaitForSeconds(1f);

            _currentDisplayedSeconds = Mathf.Max(_currentDisplayedSeconds - 1, 0);
            SetText(_currentDisplayedSeconds.ToString());
        }
    }

    // 함수 이름 : StopLocalCountdown()
    // 함수 기능 : (선택) 외부에서 버튼 등으로 로컬 카운트다운만 중지하고 싶을 때 호출
    // 입력 파라미터 : 없음
    // 리턴 타입 : void
    public void StopLocalCountdown()
    {
        if (_countdownRoutine != null)
        {
            StopCoroutine(_countdownRoutine);
            _countdownRoutine = null;
        }
    }

}
