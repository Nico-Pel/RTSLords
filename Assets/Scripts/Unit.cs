using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Unit : GameBehaviour
{
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