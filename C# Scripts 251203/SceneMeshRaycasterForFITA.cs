// 251202 추가 스크립트
// 251203 수정

// YOLO가 탐지한 부분을 Raycast.
// YOLO 탐지 영역 및 3D object의 위치를 TMP Text 디버그 로그로 출력

using UnityEngine;
using TMPro;

public class SceneMeshRaycasterForFITA : MonoBehaviour
{
    [Header("References")]
    [Tooltip("OVRCameraRig 안의 CenterEyeAnchor 카메라를 넣어줘")]
    public Camera centerEyeCamera;

    [Tooltip("히트한 위치로 옮길 힌트 오브젝트(3D 아이콘 등)")]
    public Transform hintObject;

    [Tooltip("Scene Mesh가 들어있는 레이어 마스크 (예: SceneMesh 레이어만 체크)")]
    public LayerMask sceneMeshLayerMask;

    [Header("Debug UI")]
    [Tooltip("Raycast 결과를 띄울 TMP Text")]
    public TMP_Text debugText;

    [Header("Raycast Settings")]
    [Tooltip("테스트용으로 쏠 Viewport UV (0~1, 좌하단 기준)")]
    public Vector2 testViewportUV = new Vector2(0.5f, 0.5f); // 화면 중앙

    [Tooltip("Ray를 쏘는 최대 거리 (미터)")]
    public float rayDistance = 5f;

    [Tooltip("몇 초마다 한 번씩 샘플링할지")]
    public float sampleInterval = 0.25f;

    [Header("Debug Raycast Test")]
    [Tooltip("true면 testViewportUV 기준으로 주기적으로 Raycast(테스트용). false면 YOLO에서 넘어온 UV만 사용")]
    public bool debugRayFromCenter = false;


    [Header("Debug")]
    public bool forceAlwaysInFront = false;  // 👈 추가





    float _timer;
    bool _warnedNoText = false;
    bool _warnedNoCamera = false;






    void Start()
    {
        if (debugText)
        {
            debugText.text = "SceneMeshRaycaster READY";
        }

        Debug.Log("[SceneMeshRaycaster] Start() called.");
    }

    void Update()
    {
        if (forceAlwaysInFront && centerEyeCamera && hintObject)
        {
            var cam = centerEyeCamera.transform;
            float dist = 0.7f;

            hintObject.gameObject.SetActive(true);
            hintObject.position = cam.position + cam.forward * dist;
            hintObject.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);

            // 디버그 로그
            var vp = centerEyeCamera.WorldToViewportPoint(hintObject.position);
            var rend = hintObject.GetComponentInChildren<Renderer>();
            Debug.Log($"[HINT TEST] active={hintObject.gameObject.activeInHierarchy} " +
              $"hasRenderer={(rend!=null)} " +
              $"rendererEnabled={(rend!=null && rend.enabled)} " +
              $"vp=({vp.x:F2},{vp.y:F2},{vp.z:F2})");

            return;   // 👈 Raycast 로직은 완전히 건너뜀
        }



        // 👉 이제 Update는 "테스트 모드"일 때만 동작
        if (!debugRayFromCenter)
            return;

        _timer += Time.deltaTime;
        if (_timer < sampleInterval) return;
        _timer = 0f;

        // 1) TMP 연결 체크
        if (!debugText)
        {
            if (!_warnedNoText)
            {
                Debug.LogWarning("[SceneMeshRaycaster] debugText가 비어 있음. TMP Text를 인스펙터에 연결해줘.");
                _warnedNoText = true;
            }
            // TMP가 없더라도, Raycast는 계속 시도하긴 함
        }

        // 2) 카메라 체크
        if (!centerEyeCamera)
        {
            if (!_warnedNoCamera)
            {
                Debug.LogWarning("[SceneMeshRaycaster] centerEyeCamera가 비어 있음. OVRCameraRig/TrackingSpace/CenterEyeAnchor의 Camera를 넣어줘.");
                _warnedNoCamera = true;
            }
            if (debugText)
            {
                debugText.text = "No CenterEyeCamera.";
            }
            return;
        }

        // 3) 테스트용 샘플링 (YOLO와 무관)
        SampleAtViewportUV(testViewportUV, moveHintObject: true, isFromYolo: false);
    }

    /// <summary>
    /// 외부에서 YOLO가 BBox 중심 UV를 넘겨줄 때 호출할 함수
    /// FaucetHintManager에서 여기만 호출해주면 됨
    /// </summary>
    public bool PlaceHintFromViewportUV(Vector2 viewportUV)
    {
        return SampleAtViewportUV(viewportUV, moveHintObject: true, isFromYolo: true);
    }

    /// <summary>
    /// 내부·외부 공용 Raycast 로직
    /// moveHintObject=true이면 hintObject를 히트 위치로 옮김
    /// isFromYolo=true면 로그/텍스트에 [YOLO]로 표시
    /// </summary>
    bool SampleAtViewportUV(Vector2 viewportUV, bool moveHintObject = true, bool isFromYolo = false)
    {
        if (!centerEyeCamera)
        {
            if (debugText)
                debugText.text = "No CenterEyeCamera.";
            return false;
        }

        Ray ray = centerEyeCamera.ViewportPointToRay(viewportUV);
        bool hit = Physics.Raycast(ray, out RaycastHit hitInfo, rayDistance, sceneMeshLayerMask);

        if (hit)
        {
            Vector3 hitPos   = hitInfo.point;
            Vector3 hitNorm  = hitInfo.normal;
            string colName   = hitInfo.collider ? hitInfo.collider.name : "(no collider)";

            Vector3 hintPos = hitPos;
            Quaternion hintRot = Quaternion.identity;

            if (moveHintObject && hintObject)
            {
                if (!hintObject.gameObject.activeSelf)
                    hintObject.gameObject.SetActive(true);

                float offset = 0.02f;
                hintPos = hitPos + hitNorm * offset;

                Vector3 camPos   = centerEyeCamera.transform.position;
                Vector3 camFwd   = centerEyeCamera.transform.forward;
                Vector3 camToHint = (hintPos - camPos);

                // 🔥 핵심 디버그
                Vector3 vp  = centerEyeCamera.WorldToViewportPoint(hintPos);
                float dot   = Vector3.Dot(camFwd.normalized, camToHint.normalized);

                // 항상 카메라 쪽을 보게
                Vector3 lookDir = camToHint;
                lookDir.y = 0f;
                if (lookDir.sqrMagnitude < 1e-4f)
                {
                    lookDir = camFwd; lookDir.y = 0f;
                }
                lookDir.Normalize();
                hintRot = Quaternion.LookRotation(-lookDir, Vector3.up);

                hintObject.position = hintPos;
                hintObject.rotation = hintRot;

                // extra 정보는 msg에 합쳐서 한 번만 쓰기
                string extra =
                    $"\nCamPos: {Fmt(camPos)}" +
                    $"\nHintPos: {Fmt(hintPos)}" +
                    $"\nViewPos: ({vp.x:F2},{vp.y:F2},{vp.z:F2})" +
                    $"\nDot(Fwd·CamToHint): {dot:F2}";

                string src = isFromYolo ? "[YOLO]" : "[TEST]";
                string msg =
                    $"{src} HIT!\n" +
                    $"Mesh: {colName}\n" +
                    $"HitPos:  {Fmt(hitPos)}\n" +
                    $"HintPos: {Fmt(hintPos)}\n" +
                    $"Delta:   {Fmt(hintPos - hitPos)}\n" +
                    $"Dist: {hitInfo.distance:F2} m\n" +
                    $"Normal: {Fmt(hitNorm)}\n" +
                    $"UV: {Fmt(viewportUV)}" +
                    extra;

                if (debugText) debugText.text = msg;
                Debug.Log("[SceneMeshRaycaster] " + msg.Replace("\n", " | "));
            }
            else
            {
                // moveHintObject=false 인 경우에도 기본 로그는 찍어주자
                string src = isFromYolo ? "[YOLO]" : "[TEST]";
                string msg =
                    $"{src} HIT!\n" +
                    $"Mesh: {colName}\n" +
                    $"HitPos:  {Fmt(hitPos)}\n" +
                    $"Dist: {hitInfo.distance:F2} m\n" +
                    $"Normal: {Fmt(hitNorm)}\n" +
                    $"UV: {Fmt(viewportUV)}";

                if (debugText) debugText.text = msg;
                Debug.Log("[SceneMeshRaycaster] " + msg.Replace("\n", " | "));
            }

            return true;
        }
        else
        {
            string src = isFromYolo ? "[YOLO]" : "[TEST]";
            string msg =
                $"{src} VOID (No Collision)\n" +
                $"UV: {Fmt(viewportUV)}";

            if (debugText) debugText.text = msg;
            Debug.Log("[SceneMeshRaycaster] " + msg.Replace("\n", " | "));
            return false;
        }
    }


    // 보기 좋게 포맷하는 헬퍼들
    static string Fmt(Vector3 v)
    {
        return $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
    }

    static string Fmt(Vector2 v)
    {
        return $"({v.x:F2}, {v.y:F2})";
    }
}


