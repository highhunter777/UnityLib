using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SettingButton : MonoBehaviour
{
    public GameObject menu;

    public void SwitchMenuActive()
    {
        if(menu.activeInHierarchy)
        menu.SetActive(false);

        else menu.SetActive(true);
    }
}
