using UnityEngine;
using System.Collections.Generic; // List를 사용하기 위해 추가

public class RequestPermissionsOnce : MonoBehaviour
{
    void Start()
    {
        Debug.Log("Requesting Passthrough Camera Access Permission...");
        
        // 1. 필요한 권한 목록 생성
        var permissions = new List<OVRPermissionsRequester.Permission>();
        
        // 2. Passthrough 카메라 데이터 접근 권한 추가
        permissions.Add(OVRPermissionsRequester.Permission.PassthroughCameraAccess);
        
        // Scene API 사용 시 함께 사용
        // permissions.Add(OVRPermissionsRequester.Permission.Scene);

        // 3. 권한 요청 팝업
        OVRPermissionsRequester.Request(permissions.ToArray());
    }
}
