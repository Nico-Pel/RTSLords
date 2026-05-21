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

    public void SpawnUnit(Unit unit)
    {
        Vector3 spawnPoint = spawnPos == null ? playerActivator.transform.position : spawnPos.position;
        Instantiate(unit, spawnPoint, Quaternion.identity, null);
    }
}