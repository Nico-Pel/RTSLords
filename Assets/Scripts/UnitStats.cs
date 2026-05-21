using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "UnitStats", menuName = "UnitStats")]
public class UnitStats : ScriptableObject
{
    public Hitbox.DamageTypes damageType;
    
    public Unit.UnitType[] unitTypes;
    public Unit.ArmorType armorType;

    public int goldPrice = 5;
    
    public int health = 100;
    public float speed = 1f;
    public float chargeSpeed = 1.5f;
    public int armor = 0;
    public int distanceArmor = 0;
    public float range = 1f;
    public int damages = 3;
    public float detectionRaidRange = 15f;
    public float detectionDefenseRange = 25f;
}