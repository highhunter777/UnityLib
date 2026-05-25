using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ErekiBall : MonoBehaviour
{
    public float life = 1.5f;
    private float endTime;

    private void Start()
    {
        endTime = life+Time.time;
    }

    private void Update()
    {
        if (Time.time > endTime) { 
        
        Destroy(gameObject);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if(! collision.gameObject.CompareTag("Player"))
        Destroy(gameObject );
    }
}
