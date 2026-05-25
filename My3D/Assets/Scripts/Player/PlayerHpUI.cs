using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(HealthPoint))]
public class PlayerHpUI : MonoBehaviour
{
    public Image hpImage;
    public TextMeshProUGUI hpText;
    private HealthPoint hpPoint;
  

    private void Awake()
    {
        hpPoint = GetComponent<HealthPoint>();
      
    }

    private void Start()
    {
        
        if (hpImage != null)
        {
            UpdateHpUI(hpPoint.hp,hpPoint.maxHp);

        }
    }

    public void UpdateHpUI(float hp,float maxHp)
    {

        hpText.text = hp + "/" + maxHp;

        float hpScale = hp / maxHp;

        hpImage.fillAmount = hpScale;




    }
}
