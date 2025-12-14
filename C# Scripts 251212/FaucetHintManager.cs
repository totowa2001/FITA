// 스크립트 이름 : FaucetHintManager.cs
// 스크립트 기능 : YoloDetector.cs에서 전달된 YOLO 탐지 결과에서 faucet의 바운딩박스 선별,
//                 박스 중심점을 Viewport UV로 변환하여 Raycasting을 위해 SceneMeshRaycasterForFITA.cs에 전달
//                 1. YOLO 결과(List<Det>)에서 faucet 후보를 score 기준으로 선별
//                 2. 선택된 bbox 중심(cx, cy)을 Viewport UV(u, v)로 변환 (Y축 뒤집기 포함)
//                 3. SceneMeshRaycasterForFITA.cs에 viewportUV를 전달
// 입력 파라미터 : sceneRaycaster(SceneMeshRaycasterForFITA.cs가 붙은 컴포넌트)
//                 faucetClassId(int)
//                 minScore(float)
// 리턴 타입 : viewportUV(Vector2)


using System.Collections.Generic;
using UnityEngine;

public class FaucetHintManager : MonoBehaviour
{
    [Header("Dependencies")]
    public SceneMeshRaycasterForFITA sceneRaycaster; // YOLO bbox 중심(Viewport UV) → SceneMesh Raycast를 수행하는 Raycaster
    public PassthroughCameraAccess cameraAccess; // Raycasting 할 카메라

    [Header("YOLO 조건")]
    public int faucetClassId = 0; // YOLO에서 faucet class ID (0부터 시작 -> 단일 클래스 사용 시 0 고정)

    [Range(0f, 1f)]
    public float minScore = 0.4f; // 해당 score를 넘겨야 3D Object를 Raycast Collision Area에 배치함

    // 함수 이름 : Awake()
    // 함수 기능 : sceneRaycaster, Camera가 비어있으면 GetComponent로 자동 연결 시도, 실패 시 에러 로그 출력
    // 입력 파라미터 : 없음
    // 리턴 타입 : 없음
    private void Awake()
    {
        if (!sceneRaycaster)
        {
            sceneRaycaster = GetComponent<SceneMeshRaycasterForFITA>();
            if (!sceneRaycaster)
                Debug.LogError("[FaucetHintManager] SceneMeshRaycasterForFITA를 찾을 수 없습니다. 같은 오브젝트에 붙였는지 확인하세요.");
        }

        if (!cameraAccess)
        {
            cameraAccess = FindFirstObjectByType<PassthroughCameraAccess>();
            if (!cameraAccess)
                Debug.LogWarning("[FaucetHintManager] PassthroughCameraAccess를 찾지 못했습니다. Inspector에서 수동으로 연결해 주세요.");
        }
    }


    // 함수 이름 : OnYoloDetections()
    // 함수 기능 : confidence가 가장 높은 faucet의 Bounding Box 중심점 좌표를 Viewport UV로 변환
    //             1. YoloDetector.cs에서 Det 리스트를 전달받음.
    //             2. confidence score가 가장 높은 faucet 선택
    //             3. Bounding Box의 중심(cx, cy)를 계산, 정규화 -> (u, v)
    //             4. YOLO 픽셀좌표계(좌상단 원점) → Viewport UV(좌하단 원점) 변환 (Y축 reverse)
    //             5. sceneRaycaster 호출 -> Raycast
    // 입력 파라미터 : dets(List<Det>) YoloDetector.cs의 Decode/NMS 결과
    //                 frameWidth(int) : Passthrough 원본 프레임 너비
    //                 frameHeight(int) : Passthrough 원본 프레임 높이
    // 리턴 타입 : 없음
    public void OnYoloDetections(List<Det> dets, int frameWidth, int frameHeight)
    {
        if (sceneRaycaster == null)
            return;

        // 1. 예외 처리. 탐지 결과가 없을 때
        // 오브젝트도 함께 숨기려면 아래 block 안의 주석 해제.
        if (dets == null || dets.Count == 0) {
            //if (sceneRaycaster.hintObject)
            //    sceneRaycaster.hintObject.gameObject.SetActive(false);
            return;
        }

        // 2. 탐지된 faucet 후보 중 최고 score 선택
        Det bestDet = default;
        bool found = false;
        float bestScore = -1f;

        foreach (var d in dets)
        {
            if (d.cls != faucetClassId) continue;   // class 필터
            if (d.score < minScore) continue;       // score 필터

            if (d.score > bestScore)
            {
                bestScore = d.score;
                bestDet = d;
                found = true;
            }
        }

        // faucet이 사라져도 object는 살아있음
        // 함께 사라지게 할 경우 아래 block 안의 주석 해제
        if (!found) {
            // if (sceneRaycaster.hintObject)
            //     sceneRaycaster.hintObject.gameObject.SetActive(false);
            return;
        }


        // 3. Bounding Box 중심(cx, cy) 계산 (YOLO 픽셀 좌표, 원점=좌상단)
        float cx = (bestDet.x1 + bestDet.x2) * 0.5f;
        float cy = (bestDet.y1 + bestDet.y2) * 0.5f;


        // 4. 픽셀좌표 → Viewport UV(0~1) 변환
        // u : 좌→우 그대로
        // v : YOLO는 y가 위->아래 방향으로 증가, Viewport는 y가 아래->위 증가
        // 따라서 y축 좌표인 v값 reverse.
        float u = cx / (float)frameWidth;
        float v = 1.0f - (cy / (float)frameHeight);

        // viewportUV(Vector2)를 SceneMeshRaycasterForFITA.cs로 전달 (함수 자체 리턴은 void)
        Vector2 viewportUV = new Vector2(u, v);


        // 5. Raycast
        // PlaceHintFromViewportUV()는 SceneMeshRaycasterForFITA.cs에 있음
        sceneRaycaster.PlaceHintFromViewportUV(viewportUV);
    }
}
