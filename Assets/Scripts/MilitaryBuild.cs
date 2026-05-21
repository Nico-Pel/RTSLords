using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MilitaryBuild : Build
{
    public enum MilitaryBuildType
    {
        Barrack,
        Archery,
        Stable,
        Other
    }
    
    public MilitaryBuildType buildType;

    public Unit[] unitPrefabs;

    public override void OpenBuildMenu()
    {
        //Open units menu
        //Open Upgrades menu
    }
}
