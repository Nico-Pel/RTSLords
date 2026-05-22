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
        if (playerActivator == null || playerActivator.LastTriggeringTeam != Team)
        {
            return;
        }

        UIGame.Instance?.OpenMenuUnits(this);
    }

    public bool TrySpawnUnit(int unitIndex)
    {
        if (unitPrefabs == null || unitIndex < 0 || unitIndex >= unitPrefabs.Length)
        {
            return false;
        }

        Unit unit = unitPrefabs[unitIndex];
        if (unit == null || Team == null)
        {
            return false;
        }

        Hitbox unitHitbox = unit.GetComponent<Hitbox>();
        if (unitHitbox == null || unitHitbox.unitStats == null)
        {
            return false;
        }

        if (!Team.SpendGold(unitHitbox.unitStats.goldPrice))
        {
            return false;
        }

        Vector3 spawnPoint = spawnPos == null ? transform.position + transform.forward * 2f : spawnPos.position;
        Unit spawnedUnit = Instantiate(unit, spawnPoint, Quaternion.Euler(0f, Team.BuildFacingY, 0f), Team.UnitsRoot);
        spawnedUnit.SetState(Team.ResolveInheritedState(spawnedUnit));
        Team.RegisterUnit(spawnedUnit);
        return true;
    }
}
