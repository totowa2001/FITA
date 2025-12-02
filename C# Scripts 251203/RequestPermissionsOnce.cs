// 이전 버전과 동일
// 251203 변경사항 없음

using UnityEngine;
using System.Collections.Generic; // List를 사용하기 위해 추가

public class RequestPermissionsOnce : MonoBehaviour
{
    // 씬이 시작될 때 한 번만 실행됩니다.
    void Start()
    {
        Debug.Log("Requesting Passthrough Camera Access Permission...");
        
        // 1. 필요한 권한 목록 생성
        var permissions = new List<OVRPermissionsRequester.Permission>();
        
        // 2. Passthrough 카메라 데이터 접근 권한 추가
        permissions.Add(OVRPermissionsRequester.Permission.PassthroughCameraAccess);
        
        // 3. Scene API 사용 권한 추가
        permissions.Add(OVRPermissionsRequester.Permission.Scene);

        // 4. 권한 요청 팝업 띄우기
        OVRPermissionsRequester.Request(permissions.ToArray());
    }
}
