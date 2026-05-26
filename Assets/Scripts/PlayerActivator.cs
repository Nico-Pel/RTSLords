using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class PlayerActivator : GameBehaviour
{
    private static readonly HashSet<PlayerActivator> AllActivators = new HashSet<PlayerActivator>();
    private static readonly Dictionary<Unit, HashSet<PlayerActivator>> OverlapsByUnit = new Dictionary<Unit, HashSet<PlayerActivator>>();
    private static readonly Dictionary<Unit, PlayerActivator> ActiveActivatorByUnit = new Dictionary<Unit, PlayerActivator>();
    private static readonly HashSet<Unit> PendingStayRefreshUnits = new HashSet<Unit>();

    public UnityEvent onPlayerTriggered;
    public UnityEvent onPlayerExit;

    public TeamManager LastTriggeringTeam { get; private set; }
    public Unit LastTriggeringUnit { get; private set; }

    private readonly HashSet<Unit> _ignoredStartupUnits = new HashSet<Unit>();
    private Collider _triggerCollider;

    private void Awake()
    {
        _triggerCollider = GetComponent<Collider>();
        AllActivators.Add(this);
    }

    private void Start()
    {
        CacheStartupUnitsInsideTrigger();
    }

    private void OnDisable()
    {
        ReleaseTrackedUnit(true);
    }

    private void OnDestroy()
    {
        ReleaseTrackedUnit(true);
        AllActivators.Remove(this);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryActivateFromCollider(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryActivateFromCollider(other, true);
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

        if (OverlapsByUnit.TryGetValue(unit, out HashSet<PlayerActivator> overlaps))
        {
            overlaps.Remove(this);
            if (overlaps.Count == 0)
            {
                OverlapsByUnit.Remove(unit);
            }
        }

        if (unit == LastTriggeringUnit)
        {
            onPlayerExit?.Invoke();
            LastTriggeringUnit = null;
            LastTriggeringTeam = null;
        }

        if (ActiveActivatorByUnit.TryGetValue(unit, out PlayerActivator activeActivator) && activeActivator == this)
        {
            ActiveActivatorByUnit.Remove(unit);
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

    private static HashSet<PlayerActivator> GetOrCreateOverlapSet(Unit unit)
    {
        if (!OverlapsByUnit.TryGetValue(unit, out HashSet<PlayerActivator> overlaps))
        {
            overlaps = new HashSet<PlayerActivator>();
            OverlapsByUnit[unit] = overlaps;
        }

        return overlaps;
    }

    private void ReleaseTrackedUnit(bool tryPromoteReplacement)
    {
        Unit unit = LastTriggeringUnit;
        if (unit == null)
        {
            return;
        }

        if (OverlapsByUnit.TryGetValue(unit, out HashSet<PlayerActivator> overlaps))
        {
            overlaps.Remove(this);
            if (overlaps.Count == 0)
            {
                OverlapsByUnit.Remove(unit);
            }
        }

        if (ActiveActivatorByUnit.TryGetValue(unit, out PlayerActivator activeActivator) && activeActivator == this)
        {
            ActiveActivatorByUnit.Remove(unit);
        }

        LastTriggeringUnit = null;
        LastTriggeringTeam = null;

        if (tryPromoteReplacement)
        {
            TryPromoteReplacementActivator(unit);
        }
    }

    private static void TryPromoteReplacementActivator(Unit unit)
    {
        if (unit == null || ActiveActivatorByUnit.ContainsKey(unit) || unit.CollisionCollider == null)
        {
            return;
        }

        PlayerActivator bestCandidate = null;
        float bestDistance = float.MaxValue;
        foreach (PlayerActivator candidate in AllActivators)
        {
            if (candidate == null || !candidate.isActiveAndEnabled || candidate._triggerCollider == null || !candidate._triggerCollider.enabled)
            {
                continue;
            }

            if (!IsUnitOverlappingActivator(unit, candidate))
            {
                continue;
            }

            Vector3 closestPoint = candidate._triggerCollider.ClosestPoint(unit.transform.position);
            float sqrDistance = (closestPoint - unit.transform.position).sqrMagnitude;
            if (sqrDistance < bestDistance)
            {
                bestDistance = sqrDistance;
                bestCandidate = candidate;
            }
        }

        if (bestCandidate != null)
        {
            GetOrCreateOverlapSet(unit).Add(bestCandidate);
            bestCandidate.LastTriggeringUnit = unit;
            bestCandidate.LastTriggeringTeam = unit.Team;
            ActiveActivatorByUnit[unit] = bestCandidate;
            bestCandidate.onPlayerTriggered?.Invoke();
        }
    }

    private static bool IsUnitOverlappingActivator(Unit unit, PlayerActivator activator)
    {
        if (unit == null || activator == null || unit.CollisionCollider == null || activator._triggerCollider == null)
        {
            return false;
        }

        return Physics.ComputePenetration(
            unit.CollisionCollider,
            unit.CollisionCollider.transform.position,
            unit.CollisionCollider.transform.rotation,
            activator._triggerCollider,
            activator._triggerCollider.transform.position,
            activator._triggerCollider.transform.rotation,
            out _,
            out _);
    }

    public static void RefreshInteractionsFor(Unit unit)
    {
        if (unit == null || !unit.IsHero)
        {
            return;
        }

        if (ActiveActivatorByUnit.TryGetValue(unit, out PlayerActivator activeActivator) && activeActivator != null)
        {
            activeActivator.LastTriggeringUnit = null;
            activeActivator.LastTriggeringTeam = null;
        }

        ActiveActivatorByUnit.Remove(unit);
        OverlapsByUnit.Remove(unit);
        PendingStayRefreshUnits.Add(unit);
        TryPromoteReplacementActivator(unit);
    }

    private void TryActivateFromCollider(Collider other, bool fromStay = false)
    {
        if (other == null || !other.CompareTag("Player"))
        {
            return;
        }

        Unit unit = other.GetComponentInParent<Unit>();
        if (unit == null || !unit.IsHero)
        {
            return;
        }

        if (_ignoredStartupUnits.Contains(unit))
        {
            return;
        }

        if (fromStay && !PendingStayRefreshUnits.Contains(unit))
        {
            return;
        }

        HashSet<PlayerActivator> overlaps = GetOrCreateOverlapSet(unit);
        overlaps.Add(this);
        if (ActiveActivatorByUnit.TryGetValue(unit, out PlayerActivator activeActivator) && activeActivator != null && activeActivator != this)
        {
            return;
        }

        if (LastTriggeringUnit == unit)
        {
            return;
        }

        LastTriggeringUnit = unit;
        LastTriggeringTeam = unit.Team;
        ActiveActivatorByUnit[unit] = this;
        PendingStayRefreshUnits.Remove(unit);
        onPlayerTriggered?.Invoke();
    }
}
