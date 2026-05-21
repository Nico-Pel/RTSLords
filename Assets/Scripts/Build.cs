using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Build : GameBehaviour
{
    public string buildName;
    public Sprite buildSprite;
    
    public PlayerActivator playerActivator;
    
    // Start is called before the first frame update
    void Start()
    {
        if (playerActivator == null)
        {
            playerActivator.onPlayerTriggered.AddListener(OpenBuildMenu);
            playerActivator.onPlayerExit.AddListener(CloseMenu);
        }
    }

    public virtual void OpenBuildMenu()
    {
        //Base -> Open upgrades menu
    }

    private void CloseMenu()
    {
        //Close all menus
    }
}