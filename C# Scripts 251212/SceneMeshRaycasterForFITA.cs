// 스크립트 이름 : SceneMeshRaycasterForFITA.cs
// 스크립트 기능 : Viewport UV를 입력으로 받아 CenterEye 카메라에서 SceneMesh 레이어로 Raycast를 수행하고,
//                 충돌 지점(hit point)에 hintObject를 배치/회전시키며 디버그 텍스트(TMP)로 상태를 출력한다.
//                 1. Viewport UV → Ray 변환 (Camera.ViewportPointToRay)
//                 2. SceneMesh 레이어 대상으로 Physics.Raycast 수행
//                 3. hit 시 hintObject 위치/회전 설정 (표면 법선 + 카메라 방향 기준)
//                 4. TMP 텍스트 및 Debug.Log로 결과 출력
// 입력 파라미터 : centerEyeCamera(Camera; OVRCameraRig/TrackingSpace/CenterEyeAnchor의 Camera)
//                 hintObject(Transform; 힌트로 표시할 3D 오브젝트 Transform)
//                 sceneMeshLayerMask(LayerMask; SceneMesh가 속한 레이어만 체크)
//                 debugText(TMP_Text; 디버그 문자열 출력용 UI 텍스트)
//                 (옵션) debugRayFromCenter(bool; true면 testViewportUV로 주기적 테스트 Raycast 수행)
// 리턴 타입 : 외부로 전달하는 데이터 없음 (단, PlaceHintFromViewportUV()는 성공 여부 bool 반환)


using UnityEngine;
using TMPro;

public class SceneMeshRaycasterForFITA : MonoBehaviour
{
    [Header("References")]
    public Camera centerEyeCamera; // Raycast를 수행할 메인카메라
    public Transform hintObject; // Raycast Hit 된 위치에 띄울 3D 오브젝트
    public LayerMask sceneMeshLayerMask; // [BuildingBlock] Scene Mesh 오브젝트가 할당된 Layer

    [Header("Debug UI")]
    public TMP_Text debugText; // 디버그 로그 출력. Raycast 결과를 띄울 TMP Text

    [Header("Raycast Settings")]
    public float rayDistance = 5f; // Raycasting 최대 거리 (*5f : 5미터)

    [Header("Debug Raycast Test")]  // Raycast 디버그 및 테스트 시 설정
    public bool debugRayFromCenter = false; // true : 디버그용. 아래 testViewportUV 값을 기준으로 Raycast
                                            // false : YOLO Bounding Box의 중앙에서 Raycast
    public Vector2 testViewportUV = new Vector2(0.5f, 0.5f); // 위 디버그용(debugRayFromCenter=true일 시에만) Viewport UV의 Raycast 위치.
                                                             // (*(0.5f, 0.5f) : 화면 중앙에서 Raycast. 0,0은 좌하단임.)

    public float sampleInterval = 0.25f; // 위 디버그용(debugRayFromCenter=true일 시에만) 몇 초마다 Raycast 할지 설정.

    [Header("Debug (Override)")] // 3D 오브젝트 디버그 및 테스트 시 설정
    public bool forceAlwaysInFront = false; // 디버그용. true면 Raycast 생략, hintObject를 항상 카메라 정면에 고정 배치


    private float _timer;
    private bool _warnedNoText = false;
    private bool _warnedNoCamera = false;




    // 함수 이름 : Start()
    // 함수 기능 : 초기 디버그 상태 표시 및 시작 로그를 출력.
    //             1. debugText가 있으면 READY 문구 출력
    //             2. Debug.Log로 Start() 호출 확인
    // 입력 파라미터 : 없음
    // 리턴 타입 : 없음
    private void Start()
    {
        if (debugText)
            debugText.text = "SceneMeshRaycaster READY";

        Debug.Log("[SceneMeshRaycasterForFITA] Start() called.");
    }

    // 함수 이름 : Update()
    // 함수 기능 : 디버그를 위한 기능. Raycast에 따른 오브젝트 배치 OR 카메라 정면에 오브젝트 배치
    //             1. forceAlwaysInFront=true면 hintObject를 카메라 정면에 고정 배치 후 종료 (Raycast X)
    //             2. debugRayFromCenter=false면 즉시 종료 (Raycast X. YOLO 입력만 처리)
    //             3. 디버그 모드에서 sampleInterval 주기에 맞춰 testViewportUV로 Raycast 테스트 수행
    // 입력 파라미터 : 없음
    // 리턴 타입 : 없음
    private void Update()
    {
        // 분기점 1.
        // forceAlwaysInFront = true. 오브젝트를 카메라 정면에 배치.
        if (forceAlwaysInFront && centerEyeCamera && hintObject)
        {
            Transform cam = centerEyeCamera.transform;
            float dist = 0.7f; // 카메라로부터의 offset

            hintObject.gameObject.SetActive(true);
            hintObject.position = cam.position + cam.forward * dist;
            hintObject.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);

            // 디버그 로그. 오브젝트 활성화 확인
            Vector3 vp = centerEyeCamera.WorldToViewportPoint(hintObject.position);
            var rend = hintObject.GetComponentInChildren<Renderer>();
            Debug.Log($"[HINT TEST] active={hintObject.gameObject.activeInHierarchy} " +
                      $"hasRenderer={(rend != null)} " +
                      $"rendererEnabled={(rend != null && rend.enabled)} " +
                      $"vp=({vp.x:F2},{vp.y:F2},{vp.z:F2})");

            return;
        }

        // 분기점 2.
        // forceAlwaysInFront = false. 오브젝트를 Raycast에 따라 배치하되, YOLO Bounding Box를 따르지 않음
        if (!debugRayFromCenter)
            return;

        _timer += Time.deltaTime;
        if (_timer < sampleInterval)
            return;

        _timer = 0f;


        // Camera 연결 체크
        if (!centerEyeCamera)
        {
            if (!_warnedNoCamera)
            {
                Debug.LogWarning("[SceneMeshRaycasterForFITA] No centerEyeCamera in Unity Inspector");
                _warnedNoCamera = true;
            }

            if (debugText)
                debugText.text = "No CenterEyeCamera.";

            return;
        }

        // 테스트용 Raycast 실행 (YOLO Bounding Box에서 나가는 Raycast가 아님)
        SampleAtViewportUV(testViewportUV, moveHintObject: true, isFromYolo: false);
    }




    // 함수 이름 : PlaceHintFromViewportUV()
    // 함수 기능 : FaucetHintManager.cs에서 전달받은 UV 좌표 기반으로 SceneMesh Raycast 수행 및 오브젝트 배치.
    //             SampleAtViewportUV()를 호출, viewportUV를 호출
    // 입력 파라미터 : viewportUV(Vector2)
    // 리턴 타입 : bool (Raycast hit 성공 여부)
    public bool PlaceHintFromViewportUV(Vector2 viewportUV)
    {
        return SampleAtViewportUV(viewportUV, moveHintObject: true, isFromYolo: true);
    }




    // 함수 이름 : SampleAtViewportUV()
    // 함수 기능 : PlaceHintFromViewportUV()에서 전달받은 UV 좌표로 Ray 생성, SceneMesh에 Raycast.
    //             hit이면 hintObject(3D 오브젝트)를 hit 지점에 배치.
    //             1. centerEyeCamera.ViewportPointToRay로 Ray 생성
    //             2. Physics.Raycast(ray, ..., sceneMeshLayerMask)로 SceneMesh 충돌 검사
    //             3. hit이면 서페이스 수직 방향으로 offset 후 hintObject 위치 지정
    //             4. 카메라를 향하도록 회전값 계산 후 hintObject.rotation 지정
    //             5. TMP/debug 로그로 결과 출력
    // 입력 파라미터 : viewportUV(Vector2)
    //                 moveHintObject(bool) - true면 hintObject를 실제로 이동/활성화)
    //                 isFromYolo(bool) - true면 로그에 [YOLO]로 표기)
    // 리턴 타입 : bool (Raycast hit 성공 여부)
    private bool SampleAtViewportUV(Vector2 viewportUV, bool moveHintObject = true, bool isFromYolo = false)
    {
        if (!centerEyeCamera)
        {
            if (debugText)
                debugText.text = "No CenterEyeCamera.";
            return false;
        }

        Ray ray = centerEyeCamera.ViewportPointToRay(viewportUV);
        bool hit = Physics.Raycast(ray, out RaycastHit hitInfo, rayDistance, sceneMeshLayerMask);

        string src = isFromYolo ? "[YOLO]" : "[TEST]";

        // Raycast가 hit 되지 않음
        if (!hit)
        {
            string msg = $"{src} VOID (No Collision)\nUV: {Fmt(viewportUV)}";
            if (debugText) debugText.text = msg;
            Debug.Log("[SceneMeshRaycasterForFITA] " + msg.Replace("\n", " | "));
            return false;
        }


        // Raycast가 성공적으로 hit 시
        Vector3 hitPos = hitInfo.point;
        Vector3 hitNorm = hitInfo.normal;
        string colName = hitInfo.collider ? hitInfo.collider.name : "(no collider)";

        // hint 배치가 필요 없으면 로그만 출력하고 종료
        if (!moveHintObject || !hintObject)
        {
            string msg =
                $"{src} HIT!\n" +
                $"Mesh: {colName}\n" +
                $"HitPos: {Fmt(hitPos)}\n" +
                $"Dist: {hitInfo.distance:F2} m\n" +
                $"Normal: {Fmt(hitNorm)}\n" +
                $"UV: {Fmt(viewportUV)}";

            if (debugText) debugText.text = msg;
            Debug.Log("[SceneMeshRaycasterForFITA] " + msg.Replace("\n", " | "));
            return true;
        }

        // hintObject 활성화
        if (!hintObject.gameObject.activeSelf)
            hintObject.gameObject.SetActive(true);

        // 서페이스 법선방향(수직)으로 오브젝트를 살짝 띄움(offset)
        float offset = 0.02f;
        Vector3 hintPos = hitPos + hitNorm * offset;

        // 오브젝트가 카메라를 바라보도록 하는 rotation 값 계산
        Vector3 camPos = centerEyeCamera.transform.position;
        Vector3 camFwd = centerEyeCamera.transform.forward;
        Vector3 camToHint = (hintPos - camPos);

        // 디버그용. 3D 오브젝트가 카메라 정면에 있는지 측정
        Vector3 vp = centerEyeCamera.WorldToViewportPoint(hintPos);
        float dot = Vector3.Dot(camFwd.normalized, camToHint.normalized);

        // 항상 카메라 쪽을 보게(수평 회전 위주) - 필요 시 정책 변경 가능
        Vector3 lookDir = camToHint;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude < 1e-4f)
        {
            lookDir = camFwd;
            lookDir.y = 0f;
        }
        lookDir.Normalize();

        //======================//
        //======== 주의 ========//
        // Object Asset이 뒤집혀서 생성되었을 경우, 아래 'lookDir'의 부호 바꿔줄 것.

        Quaternion hintRot = Quaternion.LookRotation(-lookDir, Vector3.up);
        //======================//
        //======================//

        hintObject.position = hintPos;
        hintObject.rotation = hintRot;

        // 디버그 로그
        string msgFull =
            $"{src} HIT!\n" +
            $"Mesh: {colName}\n" +
            $"HitPos:  {Fmt(hitPos)}\n" +
            $"HintPos: {Fmt(hintPos)}\n" +
            $"Dist: {hitInfo.distance:F2} m\n" +
            $"Normal: {Fmt(hitNorm)}\n" +
            $"UV: {Fmt(viewportUV)}\n" +
            $"CamPos: {Fmt(camPos)}\n" +
            $"ViewPos: ({vp.x:F2},{vp.y:F2},{vp.z:F2})\n" +
            $"Dot(Fwd·CamToHint): {dot:F2}";

        if (debugText) debugText.text = msgFull;
        Debug.Log("[SceneMeshRaycasterForFITA] " + msgFull.Replace("\n", " | "));

        return true;
    }



    // 함수 이름 : Fmt()
    // 함수 기능 : 디버그 문자열 출력용 - Vector3(혹은 Vector2) formatting.
    // 입력 파라미터 : v(Vector3)
    // 리턴 타입 : string
    private static string Fmt(Vector3 v)
    {
        return $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
    }

    private static string Fmt(Vector2 v)
    {
        return $"({v.x:F2}, {v.y:F2})";
    }
}
