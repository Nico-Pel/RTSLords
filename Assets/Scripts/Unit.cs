using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Unit : GameBehaviour
{
    public enum UnitState
    {
        defendBase,
        followPlayer,
        raidEnemies
    }
    
    private UnitState _currentUnitState = UnitState.defendBase;
    
    public string unitName;
    public Sprite unitSprite;
    public enum UnitType
    {
        infantryman,
        archer,
        spearman,
        cavalier,
        build,
        other
    }
    
    public enum ArmorType
    {
        lightArmor,
        heavyArmor,
        building,
    }
}