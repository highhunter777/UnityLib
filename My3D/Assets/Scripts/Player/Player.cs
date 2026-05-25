using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Player : MonoBehaviour
{
    private Animator animator;
    public GameObject effectPrefab, effectPos;
    private GameObject effect;
    public AudioClip audioClip;
    public GameObject erekiBallPrefab;
    private HealthPoint healthPoint;
    private void Start()
    {
        animator = GetComponent<Animator>();
        healthPoint = GetComponent<HealthPoint>();

    }
    private void Update()
    {

        CheckDeath();
        PlayerControl();
    }

    public void Hit()
    {
        //ÌúÉ°ÕÆ
        effect = Instantiate(effectPrefab, effectPos.transform);
        AudioSource.PlayClipAtPoint(audioClip, effectPos.transform.position, 1);

        //Æø¹¦µ¯
        GameObject ball = Instantiate(erekiBallPrefab, effectPos.transform.position, Quaternion.Euler(0, 0, 0));
        ball.GetComponent<Rigidbody>().AddForce(transform.forward * 1000);
    }

    public void HitEnd()
    {

        Destroy(effect);

    }
    private void CheckDeath()
    {

        if (healthPoint.hp <= 0)
        {
            animator.SetBool("Death", true);

            this.enabled = false;
            EventHandler.CallPlayerDeathEvent(PlayerState.Death);
            StartCoroutine(ClearBody());
        }

    }
    private IEnumerator ClearBody()
    {

        yield return new WaitForSeconds(3f);
        gameObject.SetActive(false);
    }


    private void PlayerControl(){

          float WS_Value = Input.GetAxis("Vertical");
        if (Input.GetKey(KeyCode.LeftShift))
            WS_Value *= 2;

        animator.SetFloat("Speed", WS_Value, 0.15f, Time.deltaTime);

        //float AD_Value = Mathf.Clamp(Input.GetAxis("Mouse X") * 50, -2, 2);
        float AD_Value = Input.GetAxis("Horizontal");

    animator.SetFloat("Direction", AD_Value, 0.15f, Time.deltaTime);

        if (Input.GetMouseButton(0) && !Input.GetKey(KeyCode.LeftAlt))
        {
            animator.SetBool("IsAttack", true);
        }
        else
      {
    animator.SetBool("IsAttack", false);
       }


        }

    private void OnTriggerEnter(Collider other)
    {
        var relativePos = other.bounds.ClosestPoint(transform.position)-transform.position;
        var relativeAngle = Mathf.Atan2(relativePos.x, relativePos.z) / Mathf.PI * 180;
        animator.SetTrigger("BeAttacked");
        animator.SetFloat("AttackAngle", relativeAngle);
    }
}
