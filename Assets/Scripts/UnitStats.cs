using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "UnitStats", menuName = "UnitStats")]
public class UnitStats : ScriptableObject
{
    public Hitbox.DamageTypes damageType;
    
    public Unit.UnitType[] unitTypes;
    public Unit.ArmorType armorType;
    
    public int health = 100;
    public float speed = 1f;
    public float chargeSpeed = 1.5f;
    public int armor = 0;
    public int distanceArmor = 0;
}