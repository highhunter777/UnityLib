using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[RequireComponent(typeof(Damage))]
public class AttackEffect : MonoBehaviour
{
    private Damage damage;
    [Header("特效相关")]
    public ParticleSystem effect;
    private Vector3 effectPos;
    private GameObject attackTarget;
    public AudioClip audioClip;
    private void Awake()
    {
        damage=GetComponent<Damage>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        attackTarget=collision.gameObject;
        effectPos = collision.contacts[0].point;
        if (damage.isAttack)
        {
            PlayEffect();
        }
    }

   

    private void OnTriggerEnter(Collider other)
    {
        attackTarget = other.gameObject;
        effectPos=other.bounds.ClosestPoint(transform.position);
        if (damage.isAttack) {
            PlayEffect();
        }
    }

    private void PlayEffect()
    {
        AudioSource.PlayClipAtPoint(audioClip, effectPos);
        StartCoroutine("InstantiateEffect", Instantiate(effect,effectPos,Quaternion.Euler(Vector3
            .zero),attackTarget.transform));
    }

   private IEnumerator InstantiateEffect(ParticleSystem effect)
    {
        yield return new WaitForSeconds(0.5f);
        Destroy(effect);
    }
}
