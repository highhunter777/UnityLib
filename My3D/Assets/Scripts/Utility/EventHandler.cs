using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class EventHandler 
{
    public static Action<PlayerState> PlayerDeathEvent;
    public static void CallPlayerDeathEvent( PlayerState playerState)
    {

        PlayerDeathEvent?.Invoke(playerState);
    }
}
