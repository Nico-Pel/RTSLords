using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BuildZone : GameBehaviour
{
    public PlayerActivator playerActivator;

    private void Start()
    {
        playerActivator.onPlayerTriggered.AddListener(OpenBuildMenu);
        playerActivator.onPlayerExit.AddListener(CloseMenu);
    }

    private void OpenBuildMenu()
    {
        //Open build menu
    }

    private void CloseMenu()
    {
        //Close All Menu
    }
}