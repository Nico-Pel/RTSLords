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
    public bool destroyOnDeath = true;

    [SerializeField, ReadOnly] private int _currentHp;
    private bool _isDead;

    public event Action<Hitbox> OnDeath;

    public int CurrentHp => _currentHp;
    public bool IsDead => _isDead;
    public TeamManager OwnerTeam { get; private set; }

    private void Start()
    {
        _currentHp = unitStats == null ? 1 : unitStats.health;

        TeamManager parentTeam = GetComponentInParent<TeamManager>();
        if (parentTeam != null)
        {
            AssignTeam(parentTeam);
        }
    }

    public void AssignTeam(TeamManager team)
    {
        OwnerTeam = team;
        if (team != null)
        {
            teamID = team.TeamId;
        }
    }

    public virtual void TakeDamage(int damage, DamageTypes damageType)
    {
        if (_isDead)
        {
            return;
        }

        if (damageType == DamageTypes.melee)
        {
            damage -= unitStats == null ? 0 : unitStats.armor;
        }
        else if (damageType == DamageTypes.distance)
        {
            damage -= unitStats == null ? 0 : unitStats.distanceArmor;
        }

        if (damage <= 0)
        {
            damage = 1;
        }

        _currentHp -= damage;
        if (_currentHp <= 0)
        {
            Death();
        }
    }

    public virtual void Heal(int heal)
    {
        if (_isDead || unitStats == null)
        {
            return;
        }

        _currentHp += heal;
        if (_currentHp > unitStats.health)
        {
            _currentHp = unitStats.health;
        }
    }

    public virtual void Death()
    {
        if (_isDead)
        {
            return;
        }

        _isDead = true;
        OnDeath?.Invoke(this);

        if (destroyOnDeath)
        {
            Destroy(gameObject);
        }
    }
}
