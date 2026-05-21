using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Hitbox : GameBehaviour
{
    public int teamID = 1;
    public enum DamageTypes
    {
        melee,
        distance,
        other
    }
    
    public UnitStats unitStats;
    private int _currentHp;

    private bool _isDead;

    private void Start()
    {
        _currentHp = unitStats.health;
    }

    public virtual void TakeDamage(int damage, DamageTypes damageType)
    {
        if (damageType == DamageTypes.melee)
        {
            damage -= unitStats.armor;
        }
        else if (damageType == DamageTypes.distance)
        {
            damage -= unitStats.distanceArmor;
        }

        if (damage <= 0)
            damage = 1;
        
        _currentHp -= damage;
        if(_currentHp <= 0)
            Death();
    }

    public virtual void Heal(int heal)
    {
        _currentHp += heal;
        if (_currentHp > unitStats.health)
            _currentHp = unitStats.health;
    }

    public virtual void Death()
    {
        _isDead = true;
    }
}