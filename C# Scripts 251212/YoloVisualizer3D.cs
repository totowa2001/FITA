// 스크립트 이름 : YoloVisualizer3D.cs
// 스크립트 기능 : YoloDetector.cs에서 탐지결과(Det 리스트)를 전달받아 Bounding Box를 표시
//                 아래 a,b,c는 선택 기능으로 구현
//                 a) 풀링방식 3D Bounding Box - 3D 공간에 박스 프리팹을 배치
//                 b) 풀링방식 2D Bounding Box - Canvas UI 상에 2D Bounding Box(RectTransform)를 배치
//                 c) 개선된 2D Bounding Box - YOLO 입력 원본 텍스처(srcTex)를 RawImage(debugRawImage)에 출력하여 배치
// 입력 파라미터 : yoloDetector(YoloDetector.cs 참조), mainCam(Camera), detectionsRoot(Transform),
//                 boxPrefab3D(GameObject), label(TextMeshProUGUI),
//                 bboxRoot(RectTransform), bboxPrefab2D(RectTransform), debugRawImage(RawImage)
// 리턴 타입 : 없음 (MonoBehaviour)

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class YoloVisualizer3D : MonoBehaviour
{
    [Header("Dependencies")]
    public YoloDetector yoloDetector;   // 라벨링/임계값 등을 받아오는 곳 : YoloDetector.cs 스크립트
    public Camera mainCam;              // 메인 카메라 - 3D 박스가 배치되는 시야 정보(FOV/Aspect)
    public Transform detectionsRoot;    // 3D Bounding Box 프리팹 인스턴스를 담는 부모 (Transform 사용)
    public GameObject boxPrefab3D;      // 3D Bounding Box 프리팹(비워둘 시 label만 업데이트)

    [Header("Display (optional)")]
    public TextMeshProUGUI label;       // 탐지 상태 로그 출력 - UI Canvas 상의 TMP Text로 출력함

    [Header("3D Placement")]
    public float depthMeters = 1.5f;    // Pooling 방식(이전 로직) - 3D 박스를 카메라 기준으로 offset 시켜두는 거리
    public bool fitWidthAndHeight = true; // Pooling 방식(이전 로직) - 3D 박스 크기 스케일링 여부

    [Header("2D Bounding Boxes")]
    public RectTransform bboxRoot;      // 2D Bounding Box 프리팹 인스턴스를 담는 부모 (Rect Transform 사용)
    public RectTransform bboxPrefab2D;  // 2D Bounding Box 프리팹 (비워둘 시 label만 업데이트)

    [Header("Debug View")]
    public RawImage debugRawImage;      // YOLO가 보고 있는 원본 텍스처의 RawImage - 디버그용, 2D Bounding Box 대체

    // 3D 박스 풀(재사용)
    private readonly List<Transform> pool = new();

    // 2D 박스 풀(재사용)
    private readonly List<RectTransform> bboxPool = new();



    // 클래스 이름 설정
    // YoloDetection.cs에 중복 존재
    // YOLO 탐지 결과가 TMP Text에 출력 시 아래 이름으로 출력됨.
    private string[] _names = new[] { "obj" };




    // 함수 이름 : Draw3DBoxes()
    // 함수 기능 : YoloDetector.cs에서 Det 리스트를 전달받아 3D 박스 프리팹을 풀링 방식으로 배치.
    //             boxPrefab3D(또는 detectionsRoot) 없을 시 label만 갱신
    // 입력 파라미터 : dets(List<Det>), imgW(int), imgH(int)
    // 리턴 타입 : void
    public void Draw3DBoxes(List<Det> dets, int imgW, int imgH)
    {
        // boxPrefab3D(또는 detectionsRoot) 없을 시 3D 바운딩박스 배치 없이 label만 갱신
        if (boxPrefab3D == null || detectionsRoot == null)
        {
            UpdateLabelOnly(dets);
            return;
        }

        // 라벨 업데이트(선택)
        UpdateLabelOnly(dets);


        // 풀 확보 및 활성/비활성 관리
        int detCount = (dets != null) ? dets.Count : 0;
        EnsurePool(detCount);

        for (int i = 0; i < pool.Count; i++)
            pool[i].gameObject.SetActive(i < detCount);


        // 3D 배치에 필요한 카메라 시야각 계산(기존 로직 유지)
        float vFOV = mainCam.fieldOfView * Mathf.Deg2Rad;                       // 수직 FOV(rad)
        float hFOV = 2f * Mathf.Atan(Mathf.Tan(vFOV * 0.5f) * mainCam.aspect);  // 수평 FOV(rad)

        for (int i = 0; i < detCount; i++)
        {
            Det d = dets[i];
            Transform boxTf = pool[i];

            // 1. YOLO Bounding Box → 정규화 좌표 (0~1)
            // YOLO 좌표계: (0,0) = 좌상단
            float boxW = d.x2 - d.x1;
            float boxH = d.y2 - d.y1;

            float cx = d.x1 + boxW * 0.5f;
            float cy = d.y1 + boxH * 0.5f;

            float u = cx / imgW;   // 0~1 (좌 → 우)
            float v = cy / imgH;   // 0~1 (상 → 하)

            // 2. 정규화 좌표 → 카메라 시야각 기준 각도
            float xAngle = (u - 0.5f) * hFOV;
            float yAngle = (0.5f - v) * vFOV; // Y는 위가 +이므로 반전

            // 3. 카메라 기준 로컬 방향 벡터 생성
            Vector3 dir =
                mainCam.transform.forward +
                Mathf.Tan(xAngle) * mainCam.transform.right +
                Mathf.Tan(yAngle) * mainCam.transform.up;

            dir.Normalize();

            // 4. 깊이(depthMeters)만큼 전방으로 offset
            Vector3 worldPos = mainCam.transform.position + dir * depthMeters;
            boxTf.position = worldPos;

            // 5. 항상 카메라를 바라보게 회전
            boxTf.rotation = Quaternion.LookRotation(
                boxTf.position - mainCam.transform.position,
                Vector3.up
            );

            // 6. Bounding Box 크기 → 월드 스케일로 변환
            if (fitWidthAndHeight)
            {
                float worldW = 2f * depthMeters * Mathf.Tan((boxW / imgW) * hFOV * 0.5f);
                float worldH = 2f * depthMeters * Mathf.Tan((boxH / imgH) * vFOV * 0.5f);

                boxTf.localScale = new Vector3(worldW, worldH, boxTf.localScale.z);
            }
        }
    }




    // 함수 이름 : Draw2DBoxes()
    // 함수 기능 : YoloDetector.cs에서 전달받은 Det 리스트를 Canvas 상의 2D UI 박스로 표시
    //             srcTex를 입력받아 debugRawImage에 원본 프레임 텍스처를 출력
    // 입력 파라미터 : dets(List<Det>), imgW(int), imgH(int), srcTex(Texture, optional)
    // 리턴 타입 : void
    public void Draw2DBoxes(List<Det> dets, int imgW, int imgH, Texture srcTex = null)
    {
        // 예외 처리
        if (bboxRoot == null || bboxPrefab2D == null || dets == null)
            return;

        // YoloDetector.cs의 RunDetection() 함수에서 전달받은 _passthroughSource 사용
        // dets : NMS 이후 최종 Det 리스트
        // imgW/imgH : Passthrough 원본 해상도
        // srcTex : 패스스루 원본 텍스처

        // 1. YOLO가 본 원본 프레임 텍스처를 RawImage에 표시
        if (debugRawImage != null && srcTex != null)
            debugRawImage.texture = srcTex;

        // 2. 2D 박스 풀 확보(필요 개수만큼 생성)
        while (bboxPool.Count < dets.Count)
        {
            RectTransform rt = Instantiate(bboxPrefab2D, bboxRoot);
            bboxPool.Add(rt);
        }

        // 3. 활성/비활성 관리
        for (int i = 0; i < bboxPool.Count; i++)
        {
            bool active = i < dets.Count;
            if (bboxPool[i].gameObject.activeSelf != active)
                bboxPool[i].gameObject.SetActive(active);
        }

        // 4. bboxRoot 크기 기준으로 Det(픽셀 좌표)를 UI 좌표로 변환
        float rootW = bboxRoot.rect.width;
        float rootH = bboxRoot.rect.height;

        for (int i = 0; i < dets.Count; i++)
        {
            Det d = dets[i];
            RectTransform rt = bboxPool[i];

            float boxW = d.x2 - d.x1;
            float boxH = d.y2 - d.y1;

            // YOLO Det 좌표계: (0,0)=좌상단, imgW/imgH 기준 픽셀 좌표
            float cx = d.x1 + boxW * 0.5f;
            float cy = d.y1 + boxH * 0.5f;

            // 픽셀 -> 정규화(0~1)
            float u = cx / imgW;   // 0~1 (좌->우)
            float v = cy / imgH;   // 0~1 (상->하)

            // bboxRoot pivot이 (0.5,0.5)라는 가정 하에 anchoredPosition 계산
            float uiX = (u - 0.5f) * rootW;
            float uiY = ((1f - v) - 0.5f) * rootH; // 상하 반전(UI는 Y값 증가방향이 아래->위, YOLO는 위->아래)

            float uiW = (boxW / imgW) * rootW;
            float uiH = (boxH / imgH) * rootH;

            rt.anchoredPosition = new Vector2(uiX, uiY);
            rt.sizeDelta = new Vector2(uiW, uiH);
        }
    }




    // 함수 이름 : EnsurePool()
    // 함수 기능 : dets 개수(n)에 맞춰 3D 박스 풀을 확장
    // 입력 파라미터 : n(int)
    // 리턴 타입 : void
    private void EnsurePool(int n)  // n : 필요한 최소 풀 크기
    {
        while (pool.Count < n)
        {
            GameObject go = Instantiate(boxPrefab3D, detectionsRoot);
            pool.Add(go.transform);
        }
    }




    // 함수 이름 : UpdateLabelOnly(List<Det>)
    // 함수 기능 : dets 첫 원소를 기준으로 label 텍스트 갱신
    // 입력 파라미터 : dets(List<Det>)
    // 리턴 타입 : void
    private void UpdateLabelOnly(List<Det> dets)
    {
        if (label == null)
            return;

        if (dets != null && dets.Count > 0)
        {
            Det d0 = dets[0];
            string cname = (d0.cls >= 0 && d0.cls < _names.Length) ? _names[d0.cls] : "obj";
            label.text = $"Detected: {cname} ({d0.score:0.00}) • total {dets.Count}";
        }
        else
        {
            label.text = "Detected: (none)";
        }
    }
}
