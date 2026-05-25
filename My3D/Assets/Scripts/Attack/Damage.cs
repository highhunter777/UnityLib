using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Damage : MonoBehaviour
{
    public Weapon weapon;
    public Character character;
    public float damage;
    public bool isAttack=false;

    public void AttackStart()
    {
        isAttack=true;
    }

    public void AttackEnd()
    {
        isAttack=false;
    }

}
