// 251202 버전
// CenterEyeAnchor 카메라의 컴포넌트로 추가

using UnityEngine;
using TMPro; // TextMeshPro가 없다면 UnityEngine.UI로 바꾸고 Text로 쓰세요

public class SimpleMeshDebugger : MonoBehaviour
{
    [Header("UI (없으면 비워둬도 로그로 나옴)")]
    public TextMeshProUGUI debugText; 

    [Header("시각화")]
    public GameObject markerPrefab; // 빨간 공 같은 거

    private GameObject _marker;

    void Start()
    {
        // 마커가 없으면 코드로 대충 빨간 공 하나 만듦
        if (markerPrefab == null)
        {
            _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _marker.transform.localScale = Vector3.one * 0.1f; // 10cm
            _marker.GetComponent<Renderer>().material.color = Color.red;
            Destroy(_marker.GetComponent<Collider>()); //지 충돌 방지
        }
        else
        {
            _marker = Instantiate(markerPrefab);
        }
        _marker.SetActive(false);
    }

    void Update()
    {
        // 매 프레임 눈(카메라) 정면으로 레이저 발사
        Ray ray = new Ray(transform.position, transform.forward);
        RaycastHit hit;

        // 레이어 상관없이(Everything) 5미터 이내 모든 것 충돌 검사
        if (Physics.Raycast(ray, out hit, 5.0f))
        {
            _marker.SetActive(true);
            _marker.transform.position = hit.point;
            
            // 회전 (벽에 붙게)
            _marker.transform.rotation = Quaternion.LookRotation(hit.normal);

            string log = $"[Collision!!] Target: {hit.collider.gameObject.name} / Layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)}";
            Debug.Log(log);
            
            if (debugText != null) debugText.text = log;
        }
        else
        {
            _marker.SetActive(false);
            if (debugText != null) debugText.text = "VOID (No Collision)";
            // 허공 (충돌없음)
        }
    }
}
