// 스크립트 이름 : YoloDetector.cs
// 스크립트 기능 : PCA 카메라 프레임(Texture)을 입력으로 받아 Barracuda로 YOLO 추론 수행, 탐지 결과를 내보냄
//                 1. Initialize() : 클래스 label 로드, onnx 모델 로드, 입력 레이아웃(NHWC/NCHW) 추론
//                 2. RunDetection() : Texture를 정규화하여 Tensor로 변환, 추론 수행 후 출력 Tensor를 Decode()로 보냄
//                 3. Decode() : 텐서 평탄화 후 Det 리스트로 디코딩
//                 4. NMS(), IoU() : 최종 탐지 결과 생성
//                 5. 최종 탐지 결과를 시각화 스크립트(YoloVisualizer3D.cs, FaucetHintManager.cs)로 내보냄
// 입력 파라미터 : visualizer(YoloVisualizer3D.cs 스크립트 입력)
//                 faucetHintController(FaucetHintManager.cs 스크립트 입력)
//                 onnx(Neural Network Model. 학습시킨 YOLO 모델의 .onnx를 입력)
// 리턴 타입 : 없음 (MonoBehaviour)

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Barracuda;
using UnityEngine;


[Serializable]
public struct Det
{
    public float x1, y1, x2, y2, score;
    public int cls;
}


public class YoloDetector : MonoBehaviour
{
    // 아래 Header들은 스크립트에 파라미터를 입력하는 부분
    [Header("Dependencies")]
    public YoloVisualizer3D visualizer;
    public FaucetHintManager faucetHintController;

    [Header("Model Settings")]  // YOLO 모델 입력
    public NNModel onnx;                    // Barracuda가 load할 ONNX 모델
    public string outputName = "output0";   // 출력 텐서 이름. ONNX 모델 내부의 'output 노드' 이름과 일치해야 함
    public int inputSize = 640;             // 리사이징 정사각 해상도
    public float confThresh = 0.1f;         // 이 threshold 이하 시 탐지 결과를 탈락시킴
    public float iouThresh = 0.45f;         // 이 threshold 이상 시 Intersection Over Union. (중복 탐지)
    public bool normalizeInput = true;      // 입력 픽셀 정규화(0-255 -> 0-1) 여부



    private IWorker worker;
    private Model model;

    private RenderTexture _rt;
    private Texture2D _tmp;
    private Texture _passthroughSource;     // YoloPassthroughInput.cs에서 전달받는 프레임 Texture

    private string _inputName = "";
    private bool _inputIsNHWC = false;

    // 클래스 이름 설정
    // 현재 Initialize()에서 Resources 텍스트로 교체되고 있음
    private string[] _names = new[] { "faucet" };

    // 클래스 개수
    // Initialize() 성공 시 _names.Length로 설정됨. 실패 시 디폴트 값인 -1.
    private int _numClasses = -1;




    // 함수 이름 : Initialize()
    // 함수 기능 : YoloPassthroughInput.cs 스크립트의 Start()에서 호출됨
    //             클래스 이름 로드 및 numClasses 설정
    //             ONNX 모델 로드, 입력 레이아웃(NHWC/NCHW) 추론
    //             Worker/입력 버퍼(RenderTexture, Texture2D) 생성
    // 입력 파라미터 : 없음
    // 리턴 타입 : void
    public void Initialize()
    {
        Debug.Log("YOLO Initialization starting...");

        try
        {
            // 1. 클래스 이름 load
            var ta = Resources.Load<TextAsset>("your1");
            if (ta != null)
                _names = ta.text.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            _numClasses = _names.Length;

            // 2. 모델 load
            model = ModelLoader.Load(onnx);
            if (model == null)
            {
                Debug.LogError("YOLO Model Load FAILED: ModelLoader.Load(onnx) returned null. Check ONNX asset.");
                return;
            }

            // 3. 입력 정보 파악
            if (model.inputs.Count > 0)
            {
                var inp = model.inputs[0];
                _inputName = inp.name;

                InferInputLayout(inp.shape, out _inputIsNHWC, out int H, out int W, out int C);
                if (H > 0 && W > 0)
                    inputSize = Mathf.Max(H, W);
            }

            // 4. Worker / 버퍼 생성
            worker = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, model);
            _rt = new RenderTexture(inputSize, inputSize, 0, RenderTextureFormat.ARGB32);
            _tmp = new Texture2D(inputSize, inputSize, TextureFormat.RGBA32, false);

            Debug.Log($"YOLO Model successfully loaded! Input: {_inputName}, Size: {inputSize}x{inputSize}.");
        }

        // 실패 디버그 로그
        catch (Exception e)
        {
            Debug.LogError($"YOLO Model Load FAILED: {e.Message}");
        }
    }



    // 함수 이름 : OnDestroy()
    // 함수 기능 : Barracuda Worker 및 렌더 타깃 리소스 해제
    //             앱 종료 시 YOLO 자동 초기화 (leakage 방지)
    // 입력 파라미터 : 없음
    // 리턴 타입 : void
    private void OnDestroy()
    {
        worker?.Dispose();
        _rt?.Release();
    }



    // 함수 이름 : InferInputLayout()
    // 함수 기능 : 모델 입력 텐서 shape이 NHWC인지 NCHW인지를 추론
    //             이후 H/W/C 값 추론
    // 입력 파라미터 : dims(int[]), isNHWC(out), H(out), W(out), C(out)
    // 리턴 타입 : void
    private static void InferInputLayout(int[] dims, out bool isNHWC, out int H, out int W, out int C)
    {
        isNHWC = false;
        H = W = C = -1;

        if (dims == null || dims.Length == 0)
            return;

        // Barracuda 텐서 shape 배열을 뒤에서부터 읽음
        int last = dims[^1];

        // NHWC 가정: N, H, W, 3
        if (last == 3)
        {
            isNHWC = true;
            H = dims[^3];
            W = dims[^2];
            C = 3;
            return;
        }

        // NCHW 가정: N, C, H, W
        if (dims.Length >= 4 && (dims[1] == 3 || dims[1] == 1))
        {
            isNHWC = false;
            C = dims[1];
            H = dims[2];
            W = dims[3];
        }
    }




    // 함수 이름 : MakeInput()
    // 함수 기능 : YoloPassthroughtInput.cs 스크립트에서 받아온 Texture를 Tensor로 변환
    //             1. 입력 Texture(src)를 inputSize x inputSize로 Blit한 뒤 픽셀을 읽어 Tensor로 변환
    //             2. 본 스크립트 헤더의 normalizeInput 옵션에 따라 0~255를 0~1로 정규화(scaling)
    // 입력 파라미터 : src(Texture)
    // 리턴 타입 : Tensor (Barracuda에 입력할 텐서로 텐서화.)
    private Tensor MakeInput(Texture src)
    {
        int origW = src.width;
        int origH = src.height;
        int targetSize = _rt.width;

        Debug.Log($"[PASSTHROUGH DEBUG] Source Resolution: {origW}x{origH}");
        Debug.Log($"[PASSTHROUGH DEBUG] Target Input Size: {targetSize}x{targetSize}");

        Graphics.Blit(src, _rt);
        RenderTexture.active = _rt;
        _tmp.ReadPixels(new Rect(0, 0, targetSize, targetSize), 0, 0, false);
        _tmp.Apply();
        RenderTexture.active = null;

        // 정규화(Regularization)
        // 입력값 0-255를 0-1로 정규화
        float scale = normalizeInput ? 1f / 255f : 1f;
        var pix = _tmp.GetPixels32();

        // 디버그 로그
        // Tensor의 첫 픽셀값이 유효한지 확인
        Debug.Log($"[PASSTHROUGH DEBUG] Read Pixels Count (Target {targetSize}x{targetSize}): {pix.Length}");

        // N, H, W, C 순서일 시 Tensor 생성
        if (_inputIsNHWC)
        {
            var t = new Tensor(1, targetSize, targetSize, 3);
            for (int y = 0; y < targetSize; y++)
            {
                for (int x = 0; x < targetSize; x++)
                {
                    var c = pix[y * targetSize + x];
                    // R,G,B 순서로 재설정
                    t[0, y, x, 0] = c.r * scale;
                    t[0, y, x, 1] = c.g * scale;
                    t[0, y, x, 2] = c.b * scale;

                    // 첫 픽셀에 관한 디버그 로그
                    if (y == 0 && x == 0)
                        Debug.Log($"[TENSOR DEBUG] R={t[0, 0, 0, 0]:F4}, G={t[0, 0, 0, 1]:F4}, B={t[0, 0, 0, 2]:F4}");
                }
            }
            return t;
        }

        // N, C, H, W 순서일 시 Tensor 생성
        else
        {
            var t = new Tensor(1, 3, targetSize, targetSize);
            for (int y = 0; y < targetSize; y++)
            {
                for (int x = 0; x < targetSize; x++)
                {
                    var c = pix[y * targetSize + x];
                    // R,G,B 순서로 재설정
                    t[0, 0, y, x] = c.r * scale;
                    t[0, 1, y, x] = c.g * scale;
                    t[0, 2, y, x] = c.b * scale;

                    // 첫 픽셀에 관한 디버그 로그
                    if (y == 0 && x == 0)
                        Debug.Log($"[TENSOR DEBUG] R={t[0, 0, 0, 0]:F4}, G={t[0, 1, 0, 0]:F4}, B={t[0, 2, 0, 0]:F4}");
                }
            }
            return t;
        }
    }




    // 함수 이름 : RunDetection(Texture)
    // 함수 기능 : YoloPassthroughInput.cs 스크립트의 Update()에서 호출됨
    //             YoloPassthroughInput.cs에서 전달받은 현재 프레임(passthroughTexture)로 YOLO 추론 수행
    //             Decode + NonMaxSuppression(NMS) 결과(finalDets)를 faucetHintController.cs 스크립트로 전달
    // 입력 파라미터 : currentFrameTexture(Texture) <- YoloPassthroughInput에서 전달받은 Texture
    // 리턴 타입 : void
    public void RunDetection(Texture currentFrameTexture)
    {
        // YoloPassthroughInput.cs 스크립트의 Update()에서 전달받은 Texture.
        // 매 프레임마다 갱신됨
        _passthroughSource = currentFrameTexture;

        if (_passthroughSource == null || visualizer == null)
            return;

        int origW = _passthroughSource.width;
        int origH = _passthroughSource.height;

        Debug.Log($"[PASSTHROUGH DEBUG] Source Resolution: {origW}x{origH}"); // 디버그 로그. Texture가 올바른 resolution으로 전달됐는지 확인



        // 위 Texture를 MakeInput()의 입력 파라미터로 전달.
        // [데이터 흐름] YoloDetector.cs(RunDetection(Texture)) -> YoloDetector.cs(MakeInput(Texture))
        using var input = MakeInput(_passthroughSource);



        // 1. Barracuda 추론 실행
        var dict = new Dictionary<string, Tensor> { { _inputName, input } };
        worker.Execute(dict);

        // 2. 결과 디코딩
        // Barracuda의 output(Tensor)를 Decode() 함수의 입력 파라미터로 전달
        using var output = worker.PeekOutput(outputName);
        var dets = Decode(output, origW, origH);    //  - output(Tensor), origW/origH(원본 해상도)를 Decode()로 전달.
        Debug.Log($"[YOLO DEBUG] Initial detections (before NMS): {dets.Count}"); // 디버그 로그

        // 3. Non Max Suppression 적용.
        var finalDets = NMS(dets, iouThresh, 100);
        Debug.Log($"[YOLO DEBUG] Final detections (after NMS): {finalDets.Count}"); // 디버그 로그

        // 4. 탐지 결과 시각화
        // 바운딩박스 시각화, 3D오브젝트 시각화 스크립트 분리
        // (1) YoloDetector.cs -> YoloVisualizer3D.cs
        // 바운딩박스를 시각화
        visualizer.Draw3DBoxes(finalDets, origW, origH);
        visualizer.Draw2DBoxes(finalDets, origW, origH, _passthroughSource);
        // (2) YoloDetector.cs -> FaucetHintManager.cs
        // 탐지된 위치에 3D 오브젝트를 띄움
        if (faucetHintController != null)
            faucetHintController.OnYoloDetections(finalDets, origW, origH);
    }




    // 함수 이름 : Decode()
    // 함수 기능 : YOLO 출력 텐서(o)를 1차원 배열로 평탄화 후 Det 리스트로 변환 ('Det' : Detection)
    //             각 detection을 [cx, cy, w, h] 포맷으로 읽음
    //             단일 클래스/멀티 클래스 경로를 분리해 score와 cls를 결정
    //             이를 다시 scaling하여 원본 input 해상도에 맞는 Bounding Box로 변환
    // 입력 파라미터 : o(Tensor), origW(int), origH(int)
    // 리턴 타입 : List<Det>
    private List<Det> Decode(Tensor o, int origW, int origH)
    {
        // 디버그 로그. _Numclasses가 제대로 초기화되지 않았을 시 확인
        if (_numClasses <= 0)
        {
            Debug.LogError("[YOLO] NumClasses was not initialized!");
            return new List<Det>();
        }
        
        // 디버그 로그. 텐서 정보 검출
        int b = o.shape.batch;
        int h = o.shape.height;
        int w = o.shape.width;
        int c = o.shape.channels;
        int total = o.length;
        Debug.Log($"[YOLO TENSOR] shape = ({b},{h},{w},{c}), length = {total}, numClasses = {_numClasses}");


        // Detection 당 feature 계산
        // 4(Bounding Box) + n(클래스 score 개수들)
        // 다중 클래스 YOLO 사용 시 재검토 필요 (현재 faucet 단일클래스 YOLO용에 맞춰져 있음)
        int featPerDet = 4 + _numClasses;
        int numDetections = total / featPerDet;
        // 디버그 로그. feature 개수 검출. 잘못된 feature 시에도 로그에 표시
        if (total % featPerDet != 0)
        {
            Debug.LogError(
                $"[YOLO DECODE] Tensor length({total}) is not divisible by (4 + numClasses)={featPerDet}. " +
                $"Check ONNX export or numClasses.");
            return new List<Det>();
        }
        Debug.Log($"[YOLO DECODE] numDetections = {numDetections}, featPerDet = {featPerDet}");


        // Tensor를 1차원 배열로 평탄화하여 재인식 후 Bounding Box 설정
        // 251111 - Tensor 채널 개수 오인식하던 문제 해결 part
        var data = o.ToReadOnlyArray();
        var dets = new List<Det>(numDetections);
        float scaleX = (float)origW / inputSize;
        float scaleY = (float)origH / inputSize;

        for (int i = 0; i < numDetections; i++)
        {
            int baseIdx = i * featPerDet;

            float cx = data[baseIdx + 0];
            float cy = data[baseIdx + 1];
            float ww = data[baseIdx + 2];
            float hh = data[baseIdx + 3];

            float score;
            int classId;

            // YOLO의 클래스 개수가 1개일 시 사용 (현재 faucet 단독 클래스)
            if (_numClasses == 1)
            {
                score = data[baseIdx + 4];
                classId = 0;
            }
            // 다중 클래스 YOLO 시 사용 (추후 Hydrant, Descending Line Device 등 학습 시)
            else
            {
                float obj = data[baseIdx + 4];

                float best = 0f;
                int bestCls = 0;

                for (int cls = 0; cls < _numClasses; cls++)
                {
                    float clsProb = data[baseIdx + 5 + cls];
                    float combined = obj * clsProb;

                    if (combined > best)
                    {
                        best = combined;
                        bestCls = cls;
                    }
                }

                score = best;
                classId = bestCls;
            }

            // confThresh를 넘겨야 탐지 결과가 탈락되지 않음
            if (score < confThresh)
                continue;

            // Bounding Box의 재설정
            float x1 = (cx - ww / 2f) * scaleX;
            float y1 = (cy - hh / 2f) * scaleY;
            float x2 = (cx + ww / 2f) * scaleX;
            float y2 = (cy + hh / 2f) * scaleY;

            // YOLO 출력 텐서를 Det 리스트로 변환 (Detection 리스트)
            dets.Add(new Det
            {
                x1 = x1,
                y1 = y1,
                x2 = x2,
                y2 = y2,
                score = score,
                cls = classId
            });
        }

        return dets;
    }



    // 함수 이름 : NMS()
    // 함수 기능 : RunDetection() 함수에 의해 호출됨.
    //             IoU 기반(iouThresh 값을 따름) Non Max Suppression 적용
    //             Intersection over Union - 중복된 바운딩 박스 제거
    // 입력 파라미터 : dets(List<Det>), iou(float), topK(int)
    // 리턴 타입 : List<Det>
    public static List<Det> NMS(List<Det> dets, float iou = 0.45f, int topK = 100)
    {
        dets.Sort((a, b) => b.score.CompareTo(a.score));

        var keep = new List<Det>();
        foreach (var d in dets)
        {
            bool drop = false;
            foreach (var k in keep)
            {
                if (IoU(d, k) > iou)
                {
                    drop = true;
                    break;
                }
            }

            if (!drop)
                keep.Add(d);

            if (keep.Count >= topK)
                break;
        }

        return keep;
    }



    // 함수 이름 : IoU()
    // 함수 기능 : RunDetection() 함수에 의해 호출됨.
    //             중복 탐지된 두 Bounding Box(Det)의 IoU(Intersection over Union) 계산
    // 입력 파라미터 : a(Det), b(Det)
    // 리턴 타입 : float
    private static float IoU(in Det a, in Det b)
    {
        float xx1 = Mathf.Max(a.x1, b.x1), yy1 = Mathf.Max(a.y1, b.y1);
        float xx2 = Mathf.Min(a.x2, b.x2), yy2 = Mathf.Min(a.y2, b.y2);

        float ww = Mathf.Max(0, xx2 - xx1);
        float hh = Mathf.Max(0, yy2 - yy1);

        float inter = ww * hh;
        float areaA = (a.x2 - a.x1) * (a.y2 - a.y1);
        float areaB = (b.x2 - b.x1) * (b.y2 - b.y1);
        float uni = areaA + areaB - inter;

        return uni <= 0 ? 0 : inter / uni;
    }
}
