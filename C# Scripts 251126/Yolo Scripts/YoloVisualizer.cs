// YoloVisualizer.cs

using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Android;
// using Unity.Barracuda; // Tensor를 직접 다루지 않으므로 제거

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

    void Start()
    {
        // ... (초기화 로직 유지) ...
    }

    // [수정] Update 루프 제거

    // [추가] YoloDetector.cs에서 최종 결과를 받아 3D 박스를 그리는 함수
    public void Draw3DBoxes(List<Det> dets, int imgW, int imgH)
    {

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

    void EnsurePool(int n)
    {
        while (pool.Count < n)
        {
            var go = Instantiate(boxPrefab3D, detectionsRoot);
            pool.Add(go.transform);
        }
    }
}
