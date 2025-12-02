// 그 이전 버전과 동일한 내용
// 251203 수정내용 없음.

// YoloPassthroughInput.cs
using UnityEngine;
using System.Collections;
using Meta.XR; // 👈 PCA API를 위한 네임스페이스

// YoloSystem 오브젝트에 부착됩니다.
public class YoloPassthroughInput : MonoBehaviour
{
    [Header("YOLO Core")]
    public YoloDetector yoloDetectorScript; 

    [Header("Meta XR Passthrough (PCA)")]
    // 🚨 [수정] 1단계에서 OVRCameraRig에 추가한 PassthroughCameraAccess 컴포넌트를 연결합니다.
    public PassthroughCameraAccess cameraAccess;

    private bool isYoloInitialized = false;

    void Start()
    {
        Debug.Log("YoloPassthroughInput.Start() called (Using PCA API)");
        
        if (yoloDetectorScript == null) {
            Debug.LogError("YoloDetector 스크립트가 연결되지 않았습니다!");
            return;
        }
        if (cameraAccess == null) {
            Debug.LogError("PassthroughCameraAccess 컴포넌트가 연결되지 않았습니다! OVRCameraRig에 추가하고 연결해주세요.");
            return;
        }

        // 1. YOLO 모델 로드 (텍스처 전달 없음)
        yoloDetectorScript.Initialize();
        isYoloInitialized = true;

        // 2. PCA 카메라 재생 시작
        Debug.Log("PCA 컴포넌트가 자동으로 재생을 시작합니다.");
    }

    void Update()
    {
        // YOLO 초기화가 안됐거나, PCA가 준비되지 않았다면 대기
        if (!isYoloInitialized || !cameraAccess.IsPlaying)
        {
            return;
        }

        // 🚨 [핵심] Meta SDK로부터 유효한 텍스처를 매 프레임 가져옵니다.
        Texture passthroughTexture = cameraAccess.GetTexture();

        if (passthroughTexture == null)
        {
            Debug.LogWarning("Waiting for PCA Texture...");
            return;
        }

        // 🚨 YoloDetector에 유효한 텍스처를 전달하여 추론 실행
        yoloDetectorScript.RunDetection(passthroughTexture);
    }
}
