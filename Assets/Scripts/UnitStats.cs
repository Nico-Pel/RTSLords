using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "UnitStats", menuName = "RTSLords/Unit Stats")]
public class UnitStats : ScriptableObject
{
    [Header("Display")]
    public Sprite sprite;
    [TextArea] public string description;

    [Header("Combat")]
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
    public float attackCooldown = 1.1f;
    public float attackMoveStopDistance = 0.9f;
    public float aggroRange = 8f;
    public float detectionRaidRange = 15f;
    public float detectionDefenseRange = 25f;

    [Header("Porjectile")] 
    public bool useProjectile = false;
    public GameObject projectilePrefab;
    public float projectileSpeed = 10f;

    [Header("Formation")]
    public float followDistance = 2.25f;
    public float defendAnchorDistance = 5f;
    public float separationDistance = 1.25f;

    [Header("Economy")]
    public float harvestInterval = 5f;
    public float woodHitInterval = 1f;
    public int woodHitsPerDelivery = 5;

    public Unit.UnitType PrimaryUnitType
    {
        get
        {
            if (unitTypes != null && unitTypes.Length > 0)
            {
                return unitTypes[0];
            }

            return Unit.UnitType.other;
        }
    }
}
