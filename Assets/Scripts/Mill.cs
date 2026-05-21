using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mill : Build
{
    public int harvestFieldActiveOnStart = 0;
    public HarvestField[] harvestFields;
    public override void OpenBuildMenu()
    {
        //Open Mill Menu
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