using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static UnityEngine.UI.Image;

public class HealthPoint : MonoBehaviour
{
    [Header("血量设置")]
    public float hp=100;
    public float maxHp=100;
    public float increaseHpValue = 10f;
    [Header("施害者")]
    public Character attackCharacter;

    [Header("UI组件")]
    private PlayerHpUI playerHpUI;
   

    private void Awake()
    {
        playerHpUI = GetComponent<PlayerHpUI>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.E)){

           IncreaseHp();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        DecreaseHp(collision.collider.gameObject.GetComponent<Damage>());
    }

    private void OnTriggerEnter(Collider other)
    {
        DecreaseHp(other.gameObject.GetComponent<Damage>());
    }

    private void DecreaseHp(Damage damage)
    {
        if (damage == null)
            return;
        if(damage.character== attackCharacter && damage.isAttack == true)
        {

            hp -= damage.damage;
            damage.AttackEnd();
            if(playerHpUI != null)
                playerHpUI. UpdateHpUI(hp,maxHp);

        }

    }

    private void IncreaseHp()
    {

        if (playerHpUI!=null)
        {

            hp = MathF.Min(hp + increaseHpValue, maxHp);
            playerHpUI.UpdateHpUI(hp, maxHp);
        }
    }
}
