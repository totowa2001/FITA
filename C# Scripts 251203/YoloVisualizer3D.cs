// 251203 수정 버전


using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.UI;


public class YoloVisualizer3D : MonoBehaviour
{
    [Header("Dependencies")]
    public YoloDetector yoloDetector; // 라벨링/임계값 참조용
    public Camera mainCam;
    public Transform detectionsRoot;
    public GameObject boxPrefab3D;

    [Header("Display (optional)")]
    public TextMeshProUGUI label;

    [Header("3D Placement")]
    public float depthMeters = 1.5f;
    public bool fitWidthAndHeight = true;

    readonly List<Transform> pool = new();
    string[] _names = new[] { "obj" };

    [Header("2D Bounding Boxes")]
    [Tooltip("2D Bounding Box들을 올려 놓을 부모 RectTransform (예: Canvas 안의 Panel)")]
    public RectTransform bboxRoot;

    [Tooltip("하나의 박스를 나타내는 UI Prefab (Image + Outline 등), 반드시 RectTransform이어야 함")]
    public RectTransform bboxPrefab2D;

    [Header("Debug View")]
    public RawImage debugRawImage;


    // 2D 박스 풀
    readonly List<RectTransform> bboxPool = new();


    void Start()
    {
        // ... (초기화 로직 유지) ...
    }

    // [수정] Update 루프 제거

    // [추가] YoloDetector.cs에서 최종 결과를 받아 3D 박스를 그리는 함수
    public void Draw3DBoxes(List<Det> dets, int imgW, int imgH)
    {
        // 0. 박스 프리팹 없을 시 라벨만 업데이트
        if (boxPrefab3D == null || detectionsRoot == null)
        {
            if (label != null)
            {
                if (dets != null && dets.Count > 0)
                {
                    var d0 = dets[0];
                    string cname =
                        (d0.cls >= 0 && d0.cls < _names.Length)
                        ? _names[d0.cls]
                        : "obj";
                    label.text = $"Detected: {cname} ({d0.score:0.00}) • total {dets.Count}";
                }
                else
                {
                    label.text = "Detected: (none)";
                }
            }
            return; // ❗ 여기서 끝내버리기 (Instantiate 안 함)
        }

        // 1. 라벨(선택)
        if (label != null)
        {
            if (dets.Count > 0)
            {
                var d = dets[0];
                string cname = (d.cls >= 0 && d.cls < _names.Length) ? _names[d.cls] : "obj";
                label.text = $"Detected: {cname} ({d.score:0.00}) • total {dets.Count}";
            }
            else label.text = "Detected: (none)";
        }

        // 2. 풀링 및 활성화 관리
        EnsurePool(dets.Count);
        for (int i = 0; i < pool.Count; i++)
            pool[i].gameObject.SetActive(i < dets.Count);

        // 3. 3D 박스 배치 (기존 Draw3DBoxes 로직 유지)
        float vFOV = mainCam.fieldOfView * Mathf.Deg2Rad; // 수직
        float hFOV = 2f * Mathf.Atan(Mathf.Tan(vFOV * 0.5f) * mainCam.aspect); // 수평

        for (int i = 0; i < dets.Count; i++)
        {
            // ... (기존 3D 배치 로직 유지) ...
        }
    }

    


    public void Draw2DBoxes(List<Det> dets, int imgW, int imgH, Texture srcTex = null)
    {
        if (bboxRoot == null || bboxPrefab2D == null)
            return;

        // 🔹 YOLO가 본 원본 텍스처를 RawImage에 깔기
        if (debugRawImage != null && srcTex != null)
            debugRawImage.texture = srcTex;

        // 1) 풀 확보
        while (bboxPool.Count < dets.Count)
        {
            var go = Instantiate(bboxPrefab2D, bboxRoot);
            var rt = go.GetComponent<RectTransform>();
            bboxPool.Add(rt);
        }

        // 2) 활성/비활성 관리
        for (int i = 0; i < bboxPool.Count; i++)
        {
            bool active = i < dets.Count;
            if (bboxPool[i].gameObject.activeSelf != active)
                bboxPool[i].gameObject.SetActive(active);
        }

        float rootW = bboxRoot.rect.width;
        float rootH = bboxRoot.rect.height;

        for (int i = 0; i < dets.Count; i++)
        {
            var d = dets[i];
            var rt = bboxPool[i];

            float boxW = d.x2 - d.x1;
            float boxH = d.y2 - d.y1;

            // YOLO: (0,0) = 좌상단, imgW/imgH 기준
            float cx = d.x1 + boxW * 0.5f;
            float cy = d.y1 + boxH * 0.5f;

            // 🔸 우선 “좌우 뒤집기 없이” 그대로 써보자
            float u = cx / imgW;   // 0~1 (왼→오른)
            float v = cy / imgH;   // 0~1 (위→아래)

            // bboxRoot의 Pivot이 (0.5, 0.5)라고 가정
            float uiX = (u - 0.5f) * rootW;
            float uiY = ((1f - v) - 0.5f) * rootH; // Y 뒤집기

            float uiW = (boxW / imgW) * rootW;
            float uiH = (boxH / imgH) * rootH;

            rt.anchoredPosition = new Vector2(uiX, uiY);
            rt.sizeDelta = new Vector2(uiW, uiH);
        }
    }





    void EnsurePool(int n)
    {
        while (pool.Count < n)
        {
            var go = Instantiate(boxPrefab3D, detectionsRoot);
            pool.Add(go.transform);
        }
    }
}
