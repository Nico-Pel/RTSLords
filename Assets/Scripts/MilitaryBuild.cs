using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MilitaryBuild : Build, IBuildProductionSource
{
    private const int MaxProductionQueueSize = 10;

    public enum MilitaryBuildType
    {
        Barrack,
        Archery,
        Stable,
        Other
    }
    
    public MilitaryBuildType buildType;
    
    public Unit[] unitPrefabs;

    private readonly Queue<Unit> _productionQueue = new Queue<Unit>();
    private Coroutine _productionRoutine;
    private Unit _currentProductionUnit;
    private float _currentProductionStartTime;
    private float _currentProductionDuration;

    public override void OpenBuildMenu()
    {
        if (!CanOpenMenuForHumanPlayer())
        {
            return;
        }

        UIGame.Instance?.OpenMenuUnits(this);
    }

    public bool TrySpawnUnit(int unitIndex)
    {
        ProductionQueueBlockReason blockReason = GetUnitQueueBlockReason(unitIndex);
        if (blockReason != ProductionQueueBlockReason.None)
        {
            return false;
        }

        Unit unit = unitPrefabs[unitIndex];
        if (!CanQueueUnit(unit, out Hitbox unitHitbox) || Team == null)
        {
            return false;
        }

        if (!Team.SpendGold(unitHitbox.unitStats.goldPrice))
        {
            return false;
        }

        _productionQueue.Enqueue(unit);
        if (_productionRoutine == null)
        {
            _productionRoutine = StartCoroutine(ProductionRoutine());
        }

        return true;
    }

    public ProductionQueueBlockReason GetUnitQueueBlockReason(int unitIndex)
    {
        if (unitPrefabs == null || unitIndex < 0 || unitIndex >= unitPrefabs.Length)
        {
            return ProductionQueueBlockReason.Invalid;
        }

        Unit unit = unitPrefabs[unitIndex];
        if (!CanQueueUnit(unit, out Hitbox unitHitbox) || Team == null)
        {
            return ProductionQueueBlockReason.Invalid;
        }

        if (GetProductionPreviewCount() >= MaxProductionQueueSize)
        {
            return ProductionQueueBlockReason.QueueFull;
        }

        if (Team.HasReachedCombatUnitCap())
        {
            return ProductionQueueBlockReason.UnitCapReached;
        }

        if (!CanAddNewUnitType(unit))
        {
            return ProductionQueueBlockReason.TypeLimitReached;
        }

        if (Team.CurrentGold < unitHitbox.unitStats.goldPrice)
        {
            return ProductionQueueBlockReason.InsufficientGold;
        }

        return ProductionQueueBlockReason.None;
    }

    private bool CanQueueUnit(Unit unit, out Hitbox unitHitbox)
    {
        unitHitbox = null;
        if (unit == null)
        {
            return false;
        }

        unitHitbox = unit.GetComponent<Hitbox>();
        return unitHitbox != null && unitHitbox.unitStats != null;
    }

    private bool CanAddNewUnitType(Unit unit)
    {
        if (Team == null || unit == null || unit.StatsAsset == null)
        {
            return false;
        }

        if (unit.IsHero || unit is Peasant || unit.PrimaryUnitType == Unit.UnitType.other)
        {
            return true;
        }

        if (Team.HasActiveControllableType(unit.StatsAsset))
        {
            return true;
        }

        return Team.GetActiveControllableTypeCount() < 5;
    }

    private IEnumerator ProductionRoutine()
    {
        while (_productionQueue.Count > 0)
        {
            Unit nextUnit = _productionQueue.Dequeue();
            if (!CanQueueUnit(nextUnit, out Hitbox unitHitbox) || Team == null)
            {
                continue;
            }

            _currentProductionUnit = nextUnit;
            float creationTime = unitHitbox.unitStats.GetResolvedCreationTime(false);
            _currentProductionStartTime = Time.time;
            _currentProductionDuration = creationTime;
            if (creationTime > 0f)
            {
                yield return new WaitForSeconds(creationTime);
            }

            SpawnQueuedUnit(nextUnit);
            ClearCurrentProduction();
        }

        _productionRoutine = null;
    }

    public bool HasProductionInProgress => _currentProductionUnit != null;

    public float ProductionProgress01
    {
        get
        {
            if (_currentProductionUnit == null || _currentProductionDuration <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01((Time.time - _currentProductionStartTime) / _currentProductionDuration);
        }
    }

    public int GetProductionPreviewCount()
    {
        return (_currentProductionUnit == null ? 0 : 1) + _productionQueue.Count;
    }

    public Sprite GetProductionPreviewSprite(int index)
    {
        if (index < 0)
        {
            return null;
        }

        if (_currentProductionUnit != null)
        {
            if (index == 0)
            {
                return ResolveUnitSprite(_currentProductionUnit);
            }

            index--;
        }

        if (index >= _productionQueue.Count)
        {
            return null;
        }

        int currentIndex = 0;
        foreach (Unit queuedUnit in _productionQueue)
        {
            if (currentIndex == index)
            {
                return ResolveUnitSprite(queuedUnit);
            }

            currentIndex++;
        }

        return null;
    }

    public BuildProductionVisualType GetProductionPreviewVisualType(int index)
    {
        return BuildProductionVisualType.Unit;
    }

    private void SpawnQueuedUnit(Unit unit)
    {
        if (unit == null || Team == null)
        {
            return;
        }

        Vector3 spawnPoint = spawnPos == null ? transform.position + transform.forward * 2f : spawnPos.position;
        Unit spawnedUnit = Instantiate(unit, spawnPoint, Quaternion.Euler(0f, Team.BuildFacingY, 0f), Team.UnitsRoot);
        spawnedUnit.SetState(Team.ResolveInheritedState(spawnedUnit));
        Team.RegisterUnit(spawnedUnit);
    }

    private void ClearCurrentProduction()
    {
        _currentProductionUnit = null;
        _currentProductionStartTime = 0f;
        _currentProductionDuration = 0f;
    }

    private Sprite ResolveUnitSprite(Unit unit)
    {
        if (unit == null)
        {
            return null;
        }

        Hitbox hitbox = unit.GetComponent<Hitbox>();
        if (hitbox != null && hitbox.unitStats != null)
        {
            return hitbox.unitStats.sprite;
        }

        return null;
    }
}
