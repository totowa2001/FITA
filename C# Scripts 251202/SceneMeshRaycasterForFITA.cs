// 251202 추가 스크립트
// 그 이전 버전에는 나오지 않는 새로운 스크립트임.


using UnityEngine;
using TMPro;

[RequireComponent(typeof(SceneMeshRaycasterForFITA))] // 같은 컴포넌트에 붙을 거니까 사실 없어도 됨
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

    float _timer;
    bool _warnedNoText = false;
    bool _warnedNoCamera = false;

    void Start()
    {
        // 혹시라도 centerEyeCamera 안 넣었으면 마지막 safety로 Camera.main 시도
        if (!centerEyeCamera)
        {
            centerEyeCamera = Camera.main;
        }

        if (debugText)
        {
            debugText.text = "SceneMeshRaycaster READY";
        }

        Debug.Log("[SceneMeshRaycaster] Start() called.");
    }

    void Update()
    {
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

        // 3) 실제 샘플링
        SampleAtViewportUV(testViewportUV);
    }

    /// <summary>
    /// 외부에서 YOLO가 BBox 중심 UV를 넘겨줄 때 호출할 함수
    /// </summary>
    public bool PlaceHintFromViewportUV(Vector2 viewportUV)
    {
        return SampleAtViewportUV(viewportUV, moveHintObject: true, isFromYolo: true);
    }

    /// <summary>
    /// 내부·외부 공용 Raycast 로직
    /// moveHintObject=true이면 hintObject를 히트 위치로 옮김
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
            if (moveHintObject && hintObject)
            {
                hintObject.position = hitInfo.point;
                hintObject.rotation = Quaternion.LookRotation(-hitInfo.normal, Vector3.up);
                hintObject.position += hitInfo.normal * 0.01f; // 표면에서 1cm 띄우기
            }

            string src = isFromYolo ? "[YOLO]" : "[TEST]";
            string msg =
                $"{src} HIT!\n" +
                $"Pos: {Fmt(hitInfo.point)}\n" +
                $"Dist: {hitInfo.distance:F2} m\n" +
                $"Normal: {Fmt(hitInfo.normal)}\n" +
                $"UV: {Fmt(viewportUV)}";

            if (debugText)
                debugText.text = msg;

            Debug.Log("[SceneMeshRaycaster] " + msg.Replace("\n", " | "));
            return true;
        }
        else
        {
            string src = isFromYolo ? "[YOLO]" : "[TEST]";
            string msg =
                $"{src} VOID (No Collision)\n" +
                $"UV: {Fmt(viewportUV)}";

            if (debugText)
                debugText.text = msg;

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
