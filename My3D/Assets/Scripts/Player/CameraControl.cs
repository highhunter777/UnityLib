using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

public class CameraControl : MonoBehaviour
{
    public Transform targetPos;

    public Transform target; // 角色Transform
    public float distance = 5f; // 相机距离
    public float height = 2f; // 相机高度
    public float rotationSpeed = 3f; // 旋转速度
    public float damping = 3f; // 平滑阻尼

    private float currentRotationX = 4.5f;
    private float currentRotationY = 0f;


    private void Start()
    {
       

        transform.position=targetPos.position;
        transform.rotation=targetPos.rotation;
       
        Cursor.visible = false;
    }

    private void Update()
    {
       
      
        if (Input.GetKey(KeyCode.LeftAlt))
        {

            Cursor.visible = true;
            return;
        }

        else
        {
            Cursor.visible = false;
        }
        //摄像机跟随
        //transform.position = Vector3.Lerp(transform.position,targetPos.position, Time.deltaTime * damping);
       // transform.rotation = Quaternion.Lerp(transform.rotation, targetPos.rotation, Time.deltaTime * 3f);

    }
    void LateUpdate()
    {

        if (Input.GetKey(KeyCode.LeftAlt))
            return;
        if (!target) return;

        // 获取鼠标输入
        currentRotationX += Input.GetAxis("Mouse X") * rotationSpeed;
        currentRotationY -= Input.GetAxis("Mouse Y") * rotationSpeed;
        currentRotationY = Mathf.Clamp(currentRotationY, -20f, 80f); // 限制垂直角度

        // 计算相机位置
        Quaternion rotation = Quaternion.Euler(currentRotationY, currentRotationX, 0);
        Vector3 targetPosition = target.position + rotation * new Vector3(0, height, -distance);

        // 相机碰撞检测（防止穿墙）
        RaycastHit hit;
        if (Physics.Linecast(target.position + Vector3.up * height, targetPosition, out hit))
        {
            targetPosition = hit.point + hit.normal * 0.3f;
        }

        // 平滑移动相机
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * damping);
        transform.LookAt(target.position + Vector3.up * height * 0.5f);
    }

}
