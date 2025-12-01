// 251202 YOLO → SceneMeshRaycaster 브릿지 전용 버전
// FaucetHintManager 오브젝트의 컴포넌트.
// 기존에 사용되던 동명의 스크립트와 다른 역할 수행

using System.Collections.Generic;
using UnityEngine;
using Meta.XR;

/// <summary>
/// YoloDetector에서 나온 detections를 받아서
/// - 수도꼭지(class) 중 하나를 고르고
/// - bbox 중심을 Viewport UV로 바꾼 뒤
/// - SceneMeshRaycasterForFITA에 넘겨서 실제 Raycast + 힌트 배치를 시키는 브릿지
/// </summary>
public class FaucetHintManager : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("YOLO bbox → SceneMesh Raycast를 실제로 수행할 Raycaster")]
    public SceneMeshRaycasterForFITA sceneRaycaster;

    [Tooltip("Main Camera에 붙어 있는 PassthroughCameraAccess (UV 기준만 맞추기 위해 참조)")]
    public PassthroughCameraAccess cameraAccess;

    [Header("YOLO 조건")]
    [Tooltip("YOLO에서 수도꼭지 class ID (현재 best.onnx 단일 클래스면 0이라고 가정)")]
    public int faucetClassId = 0;

    [Range(0f, 1f)]
    [Tooltip("이 score 이상일 때만 힌트 표시")]
    public float minScore = 0.4f;

    void Awake()
    {
        // sceneRaycaster를 같은 오브젝트에서 자동으로 찾아보기 (인스펙터에 안 넣었을 때 대비)
        if (!sceneRaycaster)
        {
            sceneRaycaster = GetComponent<SceneMeshRaycasterForFITA>();
            if (!sceneRaycaster)
            {
                Debug.LogError("[FaucetHintManager] SceneMeshRaycasterForFITA를 찾을 수 없습니다. 같은 오브젝트에 붙였는지 확인하세요.");
            }
        }

        if (!cameraAccess)
        {
            // 대략적인 안전장치. 그래도 가능하면 인스펙터에서 직접 연결 추천.
            cameraAccess = FindFirstObjectByType<PassthroughCameraAccess>();
            if (!cameraAccess)
            {
                Debug.LogWarning("[FaucetHintManager] PassthroughCameraAccess를 찾지 못했습니다. Inspector에서 수동으로 연결해 주세요.");
            }
        }
    }

    /// <summary>
    /// YoloDetector.RunDetection() 마지막에서 호출됨.
    /// detections : YOLO 결과 리스트
    /// frameWidth, frameHeight : PCA 텍스처 원본 해상도
    /// </summary>
    public void OnYoloDetections(List<Det> dets, int frameWidth, int frameHeight)
    {
        if (sceneRaycaster == null)
            return;

        // 1) detection이 아예 없으면 힌트 숨기기
        if (dets == null || dets.Count == 0)
        {
            if (sceneRaycaster.hintObject)
                sceneRaycaster.hintObject.gameObject.SetActive(false);
            return;
        }

        // 2) 조건(Class ID, Score)을 만족하는 가장 score 높은 수도꼭지 bbox 하나 고르기
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
            // 수도꼭지 class가 아예 없으면 힌트 숨김
            if (sceneRaycaster.hintObject)
                sceneRaycaster.hintObject.gameObject.SetActive(false);
            return;
        }

        // 3) bbox 중심 (픽셀 좌표)
        float cx = (bestDet.x1 + bestDet.x2) * 0.5f;
        float cy = (bestDet.y1 + bestDet.y2) * 0.5f;

        // 4) 픽셀 → Viewport UV (0~1)
        // YOLO 좌표계(좌상단 0,0) → Unity 뷰포트(좌하단 0,0) 변환 위해 Y 반전
        float u = cx / (float)frameWidth;
        float v = 1.0f - (cy / (float)frameHeight);
        Vector2 viewportUV = new Vector2(u, v);

        // 5) 실제 SceneMesh Raycast 수행 (YOLO 플래그 켜고)
        bool hit = sceneRaycaster.PlaceHintFromViewportUV(viewportUV);

        // (선택) Raycast가 실패했다면 힌트 숨기기
        if (!hit && sceneRaycaster.hintObject)
        {
            sceneRaycaster.hintObject.gameObject.SetActive(false);
        }
    }
}
