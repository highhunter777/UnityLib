using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[RequireComponent(typeof(HealthPoint))]

public class EnemyHpUI : MonoBehaviour
{
    private float maxHp,maxLength;
    private HealthPoint healthPoint;
    public Transform hpTransform;
    public Transform hpBackgroundTransform;

    private void Awake()
    {
        healthPoint=GetComponent<HealthPoint>();
    }
    private void Start()
    {
        maxHp = healthPoint.hp;
        maxLength= hpTransform.localScale.x;
    }

    private void Update()
    {

        float length = maxLength * healthPoint.hp / maxHp;
        if (length < 0)
        {
            //Destroy(hpTransform.gameObject);
            return;
        }
        hpTransform.localScale = new Vector3(length, hpTransform.localScale.y, hpTransform.localScale.z);
        hpTransform.localPosition = new Vector3((maxLength - length) / 2, 0, 0);


       
        hpBackgroundTransform.LookAt(Camera.main.transform);
       
    }
}
