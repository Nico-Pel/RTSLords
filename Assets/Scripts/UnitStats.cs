using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "UnitStats", menuName = "RTSLords/Unit Stats")]
public class UnitStats : ScriptableObject
{
    public enum DamageModifierTarget
    {
        damageType,
        unitType
    }

    [System.Serializable]
    public class DamageModifier
    {
        public DamageModifierTarget targetType = DamageModifierTarget.unitType;
        public Hitbox.DamageTypes damageType = Hitbox.DamageTypes.other;
        public Unit.UnitType unitType = Unit.UnitType.other;
        public float multiplier = 1f;

        public bool Matches(Hitbox.DamageTypes incomingDamageType, UnitStats otherStats)
        {
            if (targetType == DamageModifierTarget.damageType)
            {
                return damageType == incomingDamageType;
            }

            if (otherStats == null || otherStats.unitTypes == null)
            {
                return false;
            }

            for (int i = 0; i < otherStats.unitTypes.Length; i++)
            {
                if (otherStats.unitTypes[i] == unitType)
                {
                    return true;
                }
            }

            return false;
        }
    }

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
    public float creationTime = 10f;

    [Header("Strengths & Weaknesses")]
    public List<DamageModifier> outgoingDamageModifiers = new List<DamageModifier>();
    public List<DamageModifier> incomingDamageModifiers = new List<DamageModifier>();

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

    public float GetResolvedCreationTime(bool isPeasant)
    {
        if (creationTime > 0f)
        {
            return creationTime;
        }

        return isPeasant ? 5f : 10f;
    }

    public float GetOutgoingDamageMultiplier(Hitbox.DamageTypes outgoingDamageType, UnitStats targetStats)
    {
        return ResolveMultiplier(outgoingDamageModifiers, outgoingDamageType, targetStats);
    }

    public float GetIncomingDamageMultiplier(Hitbox.DamageTypes incomingDamageType, UnitStats sourceStats)
    {
        return ResolveMultiplier(incomingDamageModifiers, incomingDamageType, sourceStats);
    }

    private float ResolveMultiplier(List<DamageModifier> modifiers, Hitbox.DamageTypes damageTypeToMatch, UnitStats otherStats)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return 1f;
        }

        float multiplier = 1f;
        for (int i = 0; i < modifiers.Count; i++)
        {
            DamageModifier modifier = modifiers[i];
            if (modifier == null)
            {
                continue;
            }

            if (modifier.Matches(damageTypeToMatch, otherStats))
            {
                multiplier *= Mathf.Max(0f, modifier.multiplier);
            }
        }

        return multiplier;
    }
}
