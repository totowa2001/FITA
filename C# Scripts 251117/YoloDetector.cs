using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Barracuda;
using UnityEngine;

[Serializable]
public struct Det {
    public float x1, y1, x2, y2, score; public int cls;
}

public class YoloDetector : MonoBehaviour {
    [Header("Dependencies")]
    public YoloVisualizer3D visualizer; // 결과 시각화 스크립트 연결
    
    [Header("Model Settings")]
    public NNModel onnx;             // best.onnx 연결
    public string outputName = "output0";
    public int inputSize = 640;
    public float confThresh = 0.1f;
    public float iouThresh = 0.45f;
    public bool normalizeInput = true;

    IWorker worker;
    Model model;
    
    RenderTexture _rt;
    Texture2D _tmp; 
    Texture _passthroughSource; // PassthroughInput에서 넘겨받을 WebCamTexture

    string _inputName = "";
    bool _inputIsNHWC = false;

    // 각 클래스 이름!!!
    string[] _names = new[] { "obj" };
    
    // 클래스 개수 변수 (-1는 디폴트값, Initialize가 제대로 되지 않을 시 -1)
    int _numClasses = -1;

    // YoloPassthroughInput.cs에서 호출됩니다.
    public void Initialize()
    {
        Debug.Log("YOLO Initialization starting...");
        
        try
        {
            // 1. 클래스 이름(names) 파일 로드 및 초기화
            var ta = Resources.Load<TextAsset>("your1");
            if (ta != null)
                _names = ta.text.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            _numClasses = _names.Length;

            // 2. 모델 로드 및 초기화
            model = ModelLoader.Load(onnx);
            
            if (model == null)
            {
                Debug.LogError("YOLO Model Load FAILED: ModelLoader.Load(onnx) returned null. Check ONNX asset.");
                return;
            }

            if (model.inputs.Count > 0) {
                var inp = model.inputs[0];
                _inputName = inp.name;
                // InferInputLayout 오류 해결: 함수 호출
                InferInputLayout(inp.shape, out _inputIsNHWC, out int H, out int W, out int C); 
                if (H > 0 && W > 0) inputSize = Mathf.Max(H, W);
            }
            
            worker = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, model); 
            _rt = new RenderTexture(inputSize, inputSize, 0, RenderTextureFormat.ARGB32);
            _tmp = new Texture2D(inputSize, inputSize, TextureFormat.RGBA32, false);

            Debug.Log($"YOLO Model successfully loaded! Input: {_inputName}, Size: {inputSize}x{inputSize}."); // C. 성공 확인
        }
        catch (Exception e)
        {
            Debug.LogError($"YOLO Model Load FAILED: {e.Message}"); // D. 실패 확인
        }
    }
    
    void OnDestroy() { worker?.Dispose(); _rt?.Release(); }

    // [추가된 함수] InferInputLayout 오류 해결 (이전 코드에서 누락됨)
    static void InferInputLayout(int[] dims, out bool isNHWC, out int H, out int W, out int C) {
        isNHWC = false; H = W = C = -1;
        if (dims == null || dims.Length == 0) return;
        // Barracuda 텐서 shape 배열은 뒤에서부터 차례로 읽습니다.
        int last = dims[^1]; 
        if (last == 3) { isNHWC = true; H = dims[^3]; W = dims[^2]; C = 3; return; }
        if (dims.Length >= 4 && (dims[1] == 3 || dims[1] == 1)) {
            isNHWC = false; C = dims[1]; H = dims[2]; W = dims[3];
        }
    }


    // --- MakeInput (Texture -> Tensor 변환) ---
    Tensor MakeInput(Texture src) {

        // 원본 Passthrough 텍스처 해상도 확인 로그
        int origW = src.width;
        int origH = src.height;
        int inputSize = _rt.width; // _rt는 inputSize x inputSize로 초기화되었을 것으로 가정

        Debug.Log($"[PASSTHROUGH DEBUG] Source Resolution: {origW}x{origH}");
        Debug.Log($"[PASSTHROUGH DEBUG] Target Input Size: {inputSize}x{inputSize}");


        Graphics.Blit(src, _rt);
        RenderTexture.active = _rt;
        _tmp.ReadPixels(new Rect(0, 0, inputSize, inputSize), 0, 0, false);
        _tmp.Apply();
        RenderTexture.active = null;

        // input에 대한 regularization (입력 정규화)
        // 입력값 0-255를 0-1로 정규화
        float scale = normalizeInput ? 1f / 255f : 1f;
        var pix = _tmp.GetPixels32();

        // *********** 새 로그 추가 (텐서 첫 픽셀 값 확인) ***********
        Debug.Log($"[PASSTHROUGH DEBUG] Read Pixels Count (Target {inputSize}x{inputSize}): {pix.Length}");

        // 🚨 2. 픽셀 데이터 바이트 수 간접 확인 로그
        // _tmp.width와 _tmp.height는 현재 inputSize와 같으므로 pix.Length는 inputSize * inputSize와 같아야 함.
        Debug.Log($"[PASSTHROUGH DEBUG] Read Pixels Count (Target {inputSize}x{inputSize}): {pix.Length}");

        

        // NHWC 또는 NCHW 형식에 따라 Tensor를 생성하고 픽셀 데이터 채우기 
        if (_inputIsNHWC)
        {
            var t = new Tensor(1, inputSize, inputSize, 3);
            for (int y = 0; y < inputSize; y++)
                for (int x = 0; x < inputSize; x++)
                {
                    var c = pix[y * inputSize + x];
                    // 🚨 RGB 순서로 재설정 (R, G, B)
                    t[0, y, x, 0] = c.r * scale; // R
                    t[0, y, x, 1] = c.g * scale; // G
                    t[0, y, x, 2] = c.b * scale; // B

                    // ************ 중요! 첫 픽셀 디버그 로그 추가 ************
                    if (y == 0 && x == 0)
                    Debug.Log($"[TENSOR DEBUG] R={t[0, 0, 0, 0]:F4}, G={t[0, 0, 0, 1]:F4}, B={t[0, 0, 0, 2]:F4}");
                }
            return t;
        }
        else
        {
            var t = new Tensor(1, 3, inputSize, inputSize);
            for (int y = 0; y < inputSize; y++)
                for (int x = 0; x < inputSize; x++)
                {
                    var c = pix[y * inputSize + x];
                    // 🚨 RGB 순서로 재설정 (R, G, B)
                    t[0, 0, y, x] = c.r * scale; // R
                    t[0, 1, y, x] = c.g * scale; // G
                    t[0, 2, y, x] = c.b * scale; // B


                    // ************ 중요! 첫 픽셀 디버그 로그 추가 ************
                    if (y == 0 && x == 0)
                    Debug.Log($"[TENSOR DEBUG] R={t[0, 0, 0, 0]:F4}, G={t[0, 1, 0, 0]:F4}, B={t[0, 2, 0, 0]:F4}");
                }
            return t;
        }
    }
    
    // YoloPassthroughInput.cs에서 호출됩니다.
    public void RunDetection(Texture currentFrameTexture) {

        // 🚨 _passthroughSource를 매 프레임 갱신합니다.
        _passthroughSource = currentFrameTexture;

        if (_passthroughSource == null || visualizer == null) return;
        
        
        int origW = _passthroughSource.width;
        int origH = _passthroughSource.height;
        
        // 🚨 필수 로그: Passthrough 텍스처의 실제 해상도 확인
        Debug.Log($"[PASSTHROUGH DEBUG] Source Resolution: {origW}x{origH}");

        // 1. Texture를 Tensor로 변환 
        using var input = MakeInput(_passthroughSource);

        // 2. 추론 실행
        var dict = new Dictionary<string, Tensor> { { _inputName, input } };
        worker.Execute(dict);
        
        // 3. 결과 디코딩 및 NMS 적용
        using var output = worker.PeekOutput(outputName);
        var dets = Decode(output, origW, origH);

        // 🚨 디버그 로그 추가: Decode 후 탐지된 초기 박스 개수 확인
        Debug.Log($"[YOLO DEBUG] Initial detections (before NMS): {dets.Count}");

        // [수정] NMS 오류 해결: 클래스 내부의 정적 함수이므로 YoloDetector.NMS 대신 NMS로 호출
        var finalDets = NMS(dets, iouThresh, 100); 
        
        // 🚨 디버그 로그 추가: NMS 후 최종 박스 개수 확인
        Debug.Log($"[YOLO DEBUG] Final detections (after NMS): {finalDets.Count}");
        
        // 4. 시각화 스크립트로 전달
        visualizer.Draw3DBoxes(finalDets, origW, origH);
    }
    
    // --- Decode & NMS ---

List<Det> Decode(Tensor o, int origW, int origH)
{
    // _Numclasses가 제대로 초기화되지 않았을 때 에러 검출
    if (_numClasses <= 0) {
    Debug.LogError("[YOLO] NumClasses was not initialized!");
    return new List<Det>();
    }

    // 1. 텐서 정보 디버그 (한 번만 찍어봐도 좋음)
    int b = o.shape.batch;
    int h = o.shape.height;
    int w = o.shape.width;
    int c = o.shape.channels;
    int total = o.length;

    Debug.Log($"[YOLO TENSOR] shape = ({b},{h},{w},{c}), length = {total}, numClasses = {_numClasses}");

    // 2. 한 detection 당 feature 개수 = 4(bbox) + numClasses(클래스 score들)
    int featPerDet = 4 + _numClasses;

    if (total % featPerDet != 0)
    {
        Debug.LogError(
            $"[YOLO DECODE] Tensor length({total}) is not divisible by (4 + numClasses)={featPerDet}. " +
            $"Check ONNX export or numClasses.");
        return new List<Det>();
    }

    int numDetections = total / featPerDet;
    Debug.Log($"[YOLO DECODE] numDetections = {numDetections}, featPerDet = {featPerDet}");

    // 3. 텐서를 1D 배열로 평탄화해서 축에 상관없이 읽기
    // Barracuda 버전에 따라 AsFloats() / ToReadOnlyArray() 이름이 다를 수 있음.
    // 안되면 o.ToReadOnlyArray() 대신 o.AsFloats() 써줘.
    var data = o.ToReadOnlyArray();

    var dets = new List<Det>(numDetections);
    float unitX = origW;
    float unitY = origH;

    for (int i = 0; i < numDetections; i++)
    {
        int baseIdx = i * featPerDet;

        // 4. bbox (cx, cy, w, h)
        float cx = data[baseIdx + 0];
        float cy = data[baseIdx + 1];
        float ww = data[baseIdx + 2];
        float hh = data[baseIdx + 3];

        float score;
        int classId;

        if (_numClasses == 1)
        {
            // 현재 best.onnx(단일 클래스)용 경로
            // 5번째 값이 이미 "최종 score"라고 가정
            score = data[baseIdx + 4];
            classId = 0;
        }
        else
        {
            // 멀티 클래스용 경로
            // [base+4]는 objectness, [base+5 .. base+4+_numClasses-1]는 클래스 확률이라고 가정
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

        if (score < confThresh)
            continue;

        float x1 = (cx - ww / 2f) * unitX;
        float y1 = (cy - hh / 2f) * unitY;
        float x2 = (cx + ww / 2f) * unitX;
        float y2 = (cy + hh / 2f) * unitY;

        dets.Add(new Det
        {
            x1 = x1, y1 = y1, x2 = x2, y2 = y2,
            score = score,
            cls = classId
        });
    }
    return dets;
}

 
    public static List<Det> NMS(List<Det> dets, float iou = 0.45f, int topK = 100) {
        dets.Sort((a, b) => b.score.CompareTo(a.score));
        var keep = new List<Det>();
        foreach (var d in dets) {
            bool drop = false;
            foreach (var k in keep) {
                if (IoU(d, k) > iou) { drop = true; break; }
            }
            if (!drop) keep.Add(d);
            if (keep.Count >= topK) break;
        }
        return keep;
    }

    static float IoU(in Det a, in Det b) {
        float xx1 = Mathf.Max(a.x1, b.x1), yy1 = Mathf.Max(a.y1, b.y1);
        float xx2 = Mathf.Min(a.x2, b.x2), yy2 = Mathf.Min(a.y2, b.y2);
        float w = Mathf.Max(0, xx2 - xx1), h = Mathf.Max(0, yy2 - yy1);
        float inter = w * h;
        float areaA = (a.x2 - a.x1) * (a.y2 - a.y1);
        float areaB = (b.x2 - b.x1) * (b.y2 - b.y1);
        float uni = areaA + areaB - inter;
        return uni <= 0 ? 0 : inter / uni;
    }
}
