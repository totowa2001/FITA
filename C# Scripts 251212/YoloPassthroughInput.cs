// 스크립트 이름 : YoloPassthroughInput.cs
// 스크립트 기능 : Meta PassthroughCameraAccess(PCA)에서 매 프레임 텍스처를 받아 YOLO 추론(YoloDetector.RunDetection)을 호출한다.
// 입력 파라미터 : yoloDetectorScript(YoloDetector.cs 스크립트 입력)
//                 cameraAccess(메인카메라 연결)
// 리턴 타입 : 없음 (MonoBehaviour)
// 대상 오브젝트 : YoloSystem의 Component로 부착

using UnityEngine;
using Meta.XR;

public class YoloPassthroughInput : MonoBehaviour
{
    [Header("YOLO Core")]
    public YoloDetector yoloDetectorScript;

    [Header("Meta XR Passthrough (PCA)")]
    public PassthroughCameraAccess cameraAccess;

    private bool isYoloInitialized = false;



    // 함수 이름 : Start()
    // 함수 기능 : YOLO 초기화(YoloDetector.cs의 Initialize() 호출)
    //             Update()에서 패스스루(PCA) 텍스쳐를 받아 Rundetection(Texture)로 전달할 준비
    // 입력 파라미터 : 없음
    // 리턴 타입 : void
    private void Start()
    {
        Debug.Log("YoloPassthroughInput.Start() called (Using PCA API)");

        if (yoloDetectorScript == null)
        {
            Debug.LogError("'YoloDetector.cs' Script Not Connected");
            return;
        }

        if (cameraAccess == null)
        {
            Debug.LogError("'PassthroughCameraAccess' Component Not Connected.");
            return;
        }

        // 1) YOLO 모델 로드/초기화
        // [데이터 흐름] YoloPassthroughInput.cs(Start()) -> YoloDetector.cs(Initialize())
        // for. YOLO 추론 준비(모델 로드/Worker 생성/입력 레이아웃 파악/버퍼 생성)
        yoloDetectorScript.Initialize();
        isYoloInitialized = true;

        // 2) PCA 카메라 재생 로그 (패스스루를 컴포넌트 차원에서 관리함)
        Debug.Log("PCA Component Starting Aumatically.");
    }



    // 함수 이름 : Update()
    // 함수 기능 : Start() 이후 (PCA 재생 중) && (텍스처 유효) 시 프레임 단위로 Texture 확보
    //             확보한 Texture를 YoloDetector.cs의 RunDetection(Texture)로 전달
    //             전달된 Texture로 YOLO가 추론을 실행함.
    // 입력 파라미터 : 없음
    // 리턴 타입 : void
    private void Update()
    {
        // YOLO 미초기화 OR 패스스루(PCA) 미준비 시 대기
        if (!isYoloInitialized || cameraAccess == null || !cameraAccess.IsPlaying)
            return;

        // cameraAccess(스크립트 인풋 파라미터)에서 passthroughTexture(Texture)를 받음
        Texture passthroughTexture = cameraAccess.GetTexture();
        if (passthroughTexture == null)
        {
            Debug.LogWarning("Waiting for PCA Texture...");
            return;
        }

        // YoloDetector.cs의 RunDetection(Texture) 함수로 passthroughTexture 텍스처를 전달
        yoloDetectorScript.RunDetection(passthroughTexture);
    }
}
