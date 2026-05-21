using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class City : Build
{
    public int harvestFieldActiveOnStart = 2;
    public Peasant peasantPrefab;
    public HarvestField[] harvestFields;

    public override void OpenBuildMenu()
    {
        base.OpenBuildMenu();
        //Open Mill menu
        //Open units menu (Peasant)
    }
    
    public int HarvestCount()
    {
        int harvestActive = 0;
        foreach (HarvestField h in harvestFields)
        {
            if (h.gameObject.activeInHierarchy == true)
                harvestActive++;
        }

        return harvestActive;
    }
}
