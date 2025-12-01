// FaucetHintManager 오브젝트의 컴포넌트로 부착
// Raycast 설정 -> ARMesh, Distance 5
// YOLO 조건 : Faucet Class Id = 0, Min Score = 0.4
// 배치 설정 : Surface Offset = 5, Face Camera = True

using System.Collections.Generic;
using UnityEngine;
using Meta.XR; // PassthroughCameraAccess 사용을 위해 필수

/// <summary>
/// YOLO가 탐지한 수도꼭지(faucet) bbox 정보를 받아서,
/// PassthroughCameraAccess를 통해 실제 공간(Scene Mesh)에 Ray를 쏘고,
/// 벽이나 싱크대 위에 정확히 3D 힌트를 부착하는 매니저.
/// </summary>
public class FaucetHintManager : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("Main Camera에 붙여둔 PassthroughCameraAccess 스크립트를 연결하세요.")]
    public PassthroughCameraAccess cameraAccess;

    [Tooltip("수도꼭지 위에 띄울 3D 프리팹")]
    public GameObject faucetHintPrefab;

    [Header("Raycast 설정")]
    [Tooltip("Raycast가 충돌할 레이어 (ARMesh 레이어만 선택하세요)")]
    public LayerMask meshLayerMask;

    [Tooltip("Raycast 최대 거리 (미터)")]
    public float maxRayDistance = 5.0f;

    [Header("YOLO 조건")]
    [Tooltip("YOLO에서 수도꼭지 class ID (지금은 0으로 가정)")]
    public int faucetClassId = 0;

    [Range(0f, 1f)]
    [Tooltip("이 score 이상일 때만 힌트 표시")]
    public float minScore = 0.4f;

    [Header("배치 설정")]
    [Tooltip("충돌 지점에서 표면 법선(Normal) 방향으로 얼마나 띄울지 (미터)")]
    public float surfaceOffset = 0.05f;

    [Tooltip("true면 벽에 붙되 사용자를 보게 회전, false면 벽 표면에 평평하게 붙음")]
    public bool faceCamera = true;

    private Transform _hintInstance;

    void Start()
    {
        // 1. 힌트 오브젝트 생성 및 초기화
        if (faucetHintPrefab != null)
        {
            _hintInstance = Instantiate(faucetHintPrefab, transform).transform;
            _hintInstance.gameObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning("[FaucetHint] faucetHintPrefab이 비어 있습니다.");
        }

        // 2. Camera Access 연결 확인
        if (cameraAccess == null)
        {
            // 만약 Inspector에서 연결을 안 했다면, Main Camera에서 찾아봄
            // [수정] 구형 API인 FindObjectOfType 대신 최신 API 사용
            cameraAccess = FindFirstObjectByType<PassthroughCameraAccess>();
            
            if (cameraAccess == null)
            {
                Debug.LogError("[FaucetHint] PassthroughCameraAccess를 찾을 수 없습니다! Main Camera에 붙어있는지 확인하세요.");
            }
        }

        // 3. LayerMask 자동 설정 (만약 Inspector에서 설정 안 했을 경우)
        if (meshLayerMask.value == 0)
        {
            int arMeshLayer = LayerMask.NameToLayer("ARMesh");
            if (arMeshLayer != -1)
            {
                meshLayerMask = 1 << arMeshLayer;
            }
            else
            {
                // ARMesh 레이어가 없으면 일단 Default 레이어라도 쓰도록 설정
                meshLayerMask = LayerMask.GetMask("Default");
                Debug.LogWarning("[FaucetHint] 'ARMesh' 레이어를 찾을 수 없습니다. Default 레이어로 설정합니다.");
            }
        }
    }

    /// <summary>
    /// YoloDetector에서 매 프레임 호출.
    /// </summary>
    public void OnYoloDetections(List<Det> dets, int frameWidth, int frameHeight)
    {
        // 필수 요소들이 없으면 중단
        if (_hintInstance == null || cameraAccess == null)
            return;

        // 0) 탐지된 것이 없으면 힌트 숨김
        if (dets == null || dets.Count == 0)
        {
            _hintInstance.gameObject.SetActive(false);
            return;
        }

        // 1) 조건(Class ID, Score)을 만족하는 가장 좋은 박스 찾기
        Det bestDet = default;
        bool found = false;
        float bestScore = -1f;

        foreach (var d in dets)
        {
            if (d.cls != faucetClassId) continue;
            if (d.score < minScore) continue;

            if (d.score > bestScore)
            {
                bestScore = d.score;
                bestDet = d;
                found = true;
            }
        }

        if (!found)
        {
            _hintInstance.gameObject.SetActive(false);
            return;
        }

        // 2) BBox 중심점 계산 (픽셀 좌표)
        float cx = (bestDet.x1 + bestDet.x2) * 0.5f;
        float cy = (bestDet.y1 + bestDet.y2) * 0.5f;

        // 3) 픽셀 좌표 -> 정규화된 뷰포트 좌표 (0~1) 변환
        // YOLO(OpenCV)는 좌상단(0,0), Unity는 좌하단(0,0)이므로 Y축 반전 필요 (1.0 - y)
        float u = cx / (float)frameWidth;
        float v = 1.0f - (cy / (float)frameHeight);

        // 4) [핵심] 3D Ray 생성 (카메라 렌즈 왜곡 보정 포함)
        // PassthroughCameraAccess의 ViewportPointToRay를 사용해야 정확합니다.
        Ray ray = cameraAccess.ViewportPointToRay(new Vector2(u, v));
        RaycastHit hit;

        // 5) Scene Mesh(ARMesh)를 향해 Ray 쏘기
        if (Physics.Raycast(ray, out hit, maxRayDistance, meshLayerMask))
        {
            // 충돌 지점(벽/싱크대) 발견!
            Vector3 targetPos = hit.point + (hit.normal * surfaceOffset); // 벽에서 살짝 띄움

            _hintInstance.position = targetPos;

            // 회전 처리
            if (faceCamera)
            {
                // 오브젝트가 사용자를 바라보게 함 (LookAt)
                // 카메라의 Y축 회전만 반영하거나 그대로 LookAt 사용
                _hintInstance.LookAt(cameraAccess.transform); 
                // 필요하다면 180도 회전 (프리팹의 정면 방향에 따라 다름)
                // _hintInstance.Rotate(0, 180, 0); 
            }
            else
            {
                // 벽면에 착 달라붙게 함 (LookRotation을 법선 방향으로)
                _hintInstance.rotation = Quaternion.LookRotation(hit.normal);
            }

            _hintInstance.gameObject.SetActive(true);

            // 디버그용 (Scene 뷰에서 초록색 선이 보임)
            Debug.DrawLine(ray.origin, hit.point, Color.green);
        }
        else
        {
            // 허공을 보고 있거나, 아직 메쉬가 생성되지 않은 곳을 볼 때는 숨김
            _hintInstance.gameObject.SetActive(false);
        }
    }
}
