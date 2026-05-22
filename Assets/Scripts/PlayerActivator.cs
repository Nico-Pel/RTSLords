using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class PlayerActivator : GameBehaviour
{
    public UnityEvent onPlayerTriggered;
    public UnityEvent onPlayerExit;

    public TeamManager LastTriggeringTeam { get; private set; }
    public Unit LastTriggeringUnit { get; private set; }

    private readonly HashSet<Unit> _ignoredStartupUnits = new HashSet<Unit>();
    private Collider _triggerCollider;

    private void Awake()
    {
        _triggerCollider = GetComponent<Collider>();
    }

    private void Start()
    {
        CacheStartupUnitsInsideTrigger();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player"))
        {
            return;
        }

        Unit unit = other.GetComponentInParent<Unit>();
        if (unit != null && unit.IsHero)
        {
            if (_ignoredStartupUnits.Contains(unit))
            {
                return;
            }

            LastTriggeringUnit = unit;
            LastTriggeringTeam = unit.Team;
            onPlayerTriggered?.Invoke();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player"))
        {
            return;
        }

        Unit unit = other.GetComponentInParent<Unit>();
        if (unit == null || !unit.IsHero)
        {
            return;
        }

        if (_ignoredStartupUnits.Remove(unit))
        {
            return;
        }

        if (unit == LastTriggeringUnit)
        {
            onPlayerExit?.Invoke();
            LastTriggeringUnit = null;
            LastTriggeringTeam = null;
        }
    }

    private void CacheStartupUnitsInsideTrigger()
    {
        if (_triggerCollider == null)
        {
            return;
        }

        Bounds bounds = _triggerCollider.bounds;
        Collider[] overlaps = Physics.OverlapBox(
            bounds.center,
            bounds.extents,
            Quaternion.identity,
            ~0,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider overlap = overlaps[i];
            if (overlap == null || !overlap.CompareTag("Player"))
            {
                continue;
            }

            Unit unit = overlap.GetComponentInParent<Unit>();
            if (unit != null && unit.IsHero)
            {
                _ignoredStartupUnits.Add(unit);
            }
        }
    }
}
