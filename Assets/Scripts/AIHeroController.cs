using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class AIHeroController : MonoBehaviour
{
    private enum StrategyPhase
    {
        GrowOpeningEconomy,
        BuildFirstStructure,
        MidGameProduction,
        BuildSecondStructure,
        LateGameProduction
    }

    private enum PlannedBuildCategory
    {
        Military,
        Economic
    }

    public Build[] possibleBuildsPrefabs;

    public float purchaseCheckInterval = 5f;
    public float lateGameDecisionIntervalMin = 5f;
    public float lateGameDecisionIntervalMax = 8f;
    public int maxBuildZonesToManage = 4;

    private float _purchaseTimer;
    private bool _strategyInitialized;
    private int _openingPeasantTarget;
    private int _expansionPeasantTarget;
    private int _raidCombatUnitTarget;
    private PlannedBuildCategory _firstPlannedCategory;
    private Build _plannedFirstBuildPrefab;
    private Build _plannedSecondBuildPrefab;
    private StrategyPhase _currentPhase;
    private bool _hasIssuedRaidOrder;

    public TeamManager Team { get; private set; }
    public Unit ControlledUnit { get; private set; }

    private void Awake()
    {
        ControlledUnit = GetComponent<Unit>();
        if (ControlledUnit != null)
        {
            ControlledUnit.isHero = true;
            ConfigureHeroPersistence();
        }
    }

    private void Start()
    {
        ConfigureHeroPersistence();
        EnsureStrategyInitialized();
    }

    public void BindTeam(TeamManager team)
    {
        Team = team;
        if (ControlledUnit != null)
        {
            ControlledUnit.AssignTeam(team);
        }

        EnsureStrategyInitialized();
    }

    private void Update()
    {
        if (ControlledUnit == null || Team == null)
        {
            return;
        }

        if (Team.player != null || Team.playerAI != this)
        {
            return;
        }

        ControlledUnit.SetControllerMoveInput(Vector3.zero);
        EnsureStrategyInitialized();

        _purchaseTimer -= Time.deltaTime;
        if (_purchaseTimer > 0f)
        {
            return;
        }

        ExecuteStrategyStep();
        _purchaseTimer = GetDecisionDelayForCurrentPhase();
    }

    private void EnsureStrategyInitialized()
    {
        if (_strategyInitialized || Team == null)
        {
            return;
        }

        _openingPeasantTarget = UnityEngine.Random.Range(7, 11);
        _expansionPeasantTarget = UnityEngine.Random.Range(10, 16);
        _raidCombatUnitTarget = UnityEngine.Random.Range(3, 8);
        _firstPlannedCategory = UnityEngine.Random.value < 0.5f
            ? PlannedBuildCategory.Military
            : PlannedBuildCategory.Economic;
        _plannedFirstBuildPrefab = ResolveFirstPlannedBuildPrefab();
        _plannedSecondBuildPrefab = null;
        _currentPhase = StrategyPhase.GrowOpeningEconomy;
        _hasIssuedRaidOrder = false;
        _purchaseTimer = 0f;
        _strategyInitialized = true;
        ApplyCombatStateObjective();
    }

    private void ExecuteStrategyStep()
    {
        if (Team == null)
        {
            return;
        }

        ApplyCombatStateObjective();
        RefreshPhaseState();

        switch (_currentPhase)
        {
            case StrategyPhase.GrowOpeningEconomy:
                TryAdvancePeasantGrowth();
                break;

            case StrategyPhase.BuildFirstStructure:
                TryConstructPlannedBuild(_plannedFirstBuildPrefab);
                break;

            case StrategyPhase.MidGameProduction:
                TryAdvancePeasantGrowth();
                break;

            case StrategyPhase.BuildSecondStructure:
                if (_plannedSecondBuildPrefab == null)
                {
                    _plannedSecondBuildPrefab = ResolveNextUniqueBuildPrefab();
                }

                if (_plannedSecondBuildPrefab == null)
                {
                    _currentPhase = StrategyPhase.LateGameProduction;
                }
                else
                {
                    TryConstructPlannedBuild(_plannedSecondBuildPrefab);
                }
                break;

            case StrategyPhase.LateGameProduction:
                TryExecuteLateGameProduction();
                break;
        }

        RefreshPhaseState();
        ApplyCombatStateObjective();
    }

    private void RefreshPhaseState()
    {
        switch (_currentPhase)
        {
            case StrategyPhase.GrowOpeningEconomy:
                if (GetAlivePeasantCount() >= _openingPeasantTarget)
                {
                    _currentPhase = StrategyPhase.BuildFirstStructure;
                }
                break;

            case StrategyPhase.BuildFirstStructure:
                if (_plannedFirstBuildPrefab == null || HasBuiltEquivalent(_plannedFirstBuildPrefab))
                {
                    _currentPhase = StrategyPhase.MidGameProduction;
                }
                break;

            case StrategyPhase.MidGameProduction:
                if (GetAlivePeasantCount() >= _expansionPeasantTarget)
                {
                    _plannedSecondBuildPrefab = ResolveNextUniqueBuildPrefab();
                    _currentPhase = _plannedSecondBuildPrefab == null
                        ? StrategyPhase.LateGameProduction
                        : StrategyPhase.BuildSecondStructure;
                }
                break;

            case StrategyPhase.BuildSecondStructure:
                if (_plannedSecondBuildPrefab == null || HasBuiltEquivalent(_plannedSecondBuildPrefab))
                {
                    _currentPhase = StrategyPhase.LateGameProduction;
                }
                break;
        }
    }

    private float GetDecisionDelayForCurrentPhase()
    {
        if (_currentPhase == StrategyPhase.LateGameProduction)
        {
            float minDelay = Mathf.Min(lateGameDecisionIntervalMin, lateGameDecisionIntervalMax);
            float maxDelay = Mathf.Max(lateGameDecisionIntervalMin, lateGameDecisionIntervalMax);
            return UnityEngine.Random.Range(minDelay, maxDelay);
        }

        return Mathf.Max(0.1f, purchaseCheckInterval);
    }

    private bool TryAdvancePeasantGrowth()
    {
        bool canSpawnPeasant = CanSpawnPeasant();
        bool canUnlockField = CanUnlockField();
        if (!canSpawnPeasant && !canUnlockField)
        {
            return false;
        }

        if (GetAvailableHarvestFieldSlotCount() >= 2 || !canUnlockField)
        {
            return canSpawnPeasant && TrySpawnPeasant();
        }

        if (!canSpawnPeasant)
        {
            return TryUnlockField();
        }

        List<Func<bool>> actions = new List<Func<bool>>
        {
            TrySpawnPeasant,
            TryUnlockField
        };

        return ExecuteRandomAction(actions);
    }

    private void TryExecuteLateGameProduction()
    {
        List<Func<bool>> actions = new List<Func<bool>>();
        if (CanSpawnPeasant())
        {
            actions.Add(TrySpawnPeasant);
        }

        if (CanUnlockField())
        {
            actions.Add(TryUnlockField);
        }

        if (CanProduceMilitaryUnit())
        {
            actions.Add(TrySpawnRandomMilitaryUnit);
        }

        ExecuteRandomAction(actions);
    }

    private bool ExecuteRandomAction(List<Func<bool>> actions)
    {
        if (actions == null || actions.Count == 0)
        {
            return false;
        }

        List<Func<bool>> shuffledActions = actions.OrderBy(_ => UnityEngine.Random.value).ToList();
        for (int i = 0; i < shuffledActions.Count; i++)
        {
            if (shuffledActions[i] != null && shuffledActions[i].Invoke())
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyCombatStateObjective()
    {
        if (Team == null)
        {
            return;
        }

        int combatUnitCount = Team.GetCurrentCombatUnitCount();
        bool shouldRaid = _hasIssuedRaidOrder || combatUnitCount >= _raidCombatUnitTarget;
        Unit.UnitState desiredState = shouldRaid ? Unit.UnitState.raidEnemies : Unit.UnitState.defendBase;

        List<UnitStats> activeTypes = Team.GetActiveControllableTypes();
        for (int i = 0; i < activeTypes.Count; i++)
        {
            UnitStats unitStats = activeTypes[i];
            if (unitStats == null || Team.GetStateForStats(unitStats) == desiredState)
            {
                continue;
            }

            Team.SetStateForStats(unitStats, desiredState);
        }

        if (shouldRaid)
        {
            _hasIssuedRaidOrder = true;
        }
    }

    private bool TrySpawnPeasant()
    {
        return Team != null && Team.city != null && Team.city.TrySpawnPeasant();
    }

    private bool CanSpawnPeasant()
    {
        return Team != null &&
               Team.city != null &&
               Team.city.GetPeasantQueueBlockReason() == ProductionQueueBlockReason.None;
    }

    private bool TryUnlockField()
    {
        if (!CanUnlockField())
        {
            return false;
        }

        if (Team.city != null && Team.city.TryUnlockHarvestField())
        {
            return true;
        }

        Mill[] mills = GetOwnedMills();
        List<Mill> shuffledMills = mills.OrderBy(_ => UnityEngine.Random.value).ToList();
        for (int i = 0; i < shuffledMills.Count; i++)
        {
            Mill mill = shuffledMills[i];
            if (mill != null && mill.TryUnlockHarvestField())
            {
                return true;
            }
        }

        return false;
    }

    private bool CanUnlockField()
    {
        if (Team == null || Team.CurrentGold < 8)
        {
            return false;
        }

        if (Team.city != null && Team.city.FindFirstInactiveField() != null)
        {
            return true;
        }

        Mill[] mills = GetOwnedMills();
        for (int i = 0; i < mills.Length; i++)
        {
            Mill mill = mills[i];
            if (mill != null && mill.FindFirstInactiveField() != null)
            {
                return true;
            }
        }

        return false;
    }

    private int GetAvailableHarvestFieldSlotCount()
    {
        if (Team == null)
        {
            return 0;
        }

        int availableFieldCount = 0;
        foreach (HarvestField field in Team.EnumerateHarvestFields())
        {
            if (field != null && field.IsAvailable)
            {
                availableFieldCount++;
            }
        }

        return availableFieldCount;
    }

    private bool TrySpawnRandomMilitaryUnit()
    {
        List<(MilitaryBuild build, int unitIndex)> availableOptions = new List<(MilitaryBuild build, int unitIndex)>();
        MilitaryBuild[] militaryBuilds = GetOwnedMilitaryBuilds();
        for (int buildIndex = 0; buildIndex < militaryBuilds.Length; buildIndex++)
        {
            MilitaryBuild militaryBuild = militaryBuilds[buildIndex];
            if (militaryBuild == null || militaryBuild.unitPrefabs == null)
            {
                continue;
            }

            for (int unitIndex = 0; unitIndex < militaryBuild.unitPrefabs.Length; unitIndex++)
            {
                if (militaryBuild.GetUnitQueueBlockReason(unitIndex) == ProductionQueueBlockReason.None)
                {
                    availableOptions.Add((militaryBuild, unitIndex));
                }
            }
        }

        if (availableOptions.Count == 0)
        {
            return false;
        }

        (MilitaryBuild build, int unitIndex) option = availableOptions[UnityEngine.Random.Range(0, availableOptions.Count)];
        return option.build != null && option.build.TrySpawnUnit(option.unitIndex);
    }

    private bool CanProduceMilitaryUnit()
    {
        MilitaryBuild[] militaryBuilds = GetOwnedMilitaryBuilds();
        for (int buildIndex = 0; buildIndex < militaryBuilds.Length; buildIndex++)
        {
            MilitaryBuild militaryBuild = militaryBuilds[buildIndex];
            if (militaryBuild == null || militaryBuild.unitPrefabs == null)
            {
                continue;
            }

            for (int unitIndex = 0; unitIndex < militaryBuild.unitPrefabs.Length; unitIndex++)
            {
                if (militaryBuild.GetUnitQueueBlockReason(unitIndex) == ProductionQueueBlockReason.None)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryConstructPlannedBuild(Build buildPrefab)
    {
        if (buildPrefab == null || Team == null || Team.city == null || Team.CurrentGold < buildPrefab.GoldPrice)
        {
            return false;
        }

        List<BuildZone> buildZones = GetManagedBuildZones();
        for (int i = 0; i < buildZones.Count; i++)
        {
            if (buildZones[i] != null && buildZones[i].TryConstruct(buildPrefab, Team))
            {
                return true;
            }
        }

        return false;
    }

    private List<BuildZone> GetManagedBuildZones()
    {
        if (Team == null || Team.city == null)
        {
            return new List<BuildZone>();
        }

        BuildZone[] allBuildZones = FindObjectsOfType<BuildZone>(true);
        Vector3 cityPosition = Team.city.transform.position;
        return allBuildZones
            .Where(buildZone => buildZone != null && !buildZone.IsOccupied)
            .OrderBy(buildZone => (buildZone.transform.position - cityPosition).sqrMagnitude)
            .Take(Mathf.Max(0, maxBuildZonesToManage))
            .ToList();
    }

    private int GetAlivePeasantCount()
    {
        if (Team == null)
        {
            return 0;
        }

        int peasantCount = 0;
        IReadOnlyList<Unit> registeredUnits = Team.RegisteredUnits;
        for (int i = 0; i < registeredUnits.Count; i++)
        {
            Unit unit = registeredUnits[i];
            if (unit == null || unit.IsDead || !unit.gameObject.activeInHierarchy || !(unit is Peasant))
            {
                continue;
            }

            peasantCount++;
        }

        return peasantCount;
    }

    private Build ResolveFirstPlannedBuildPrefab()
    {
        if (_firstPlannedCategory == PlannedBuildCategory.Economic)
        {
            return GetEconomicBuildPrefabs().FirstOrDefault();
        }

        List<Build> militaryPrefabs = GetMilitaryBuildPrefabs();
        if (militaryPrefabs.Count == 0)
        {
            return GetEconomicBuildPrefabs().FirstOrDefault();
        }

        return militaryPrefabs[UnityEngine.Random.Range(0, militaryPrefabs.Count)];
    }

    private Build ResolveNextUniqueBuildPrefab()
    {
        List<Build> candidates = new List<Build>();
        candidates.AddRange(GetMilitaryBuildPrefabs());
        candidates.AddRange(GetEconomicBuildPrefabs());

        List<Build> uniqueCandidates = candidates
            .Where(build => build != null && !HasBuiltEquivalent(build))
            .ToList();

        if (uniqueCandidates.Count == 0)
        {
            return null;
        }

        return uniqueCandidates[UnityEngine.Random.Range(0, uniqueCandidates.Count)];
    }

    private List<Build> GetMilitaryBuildPrefabs()
    {
        if (possibleBuildsPrefabs == null)
        {
            return new List<Build>();
        }

        return possibleBuildsPrefabs
            .Where(build => build is MilitaryBuild)
            .ToList();
    }

    private List<Build> GetEconomicBuildPrefabs()
    {
        if (possibleBuildsPrefabs == null)
        {
            return new List<Build>();
        }

        return possibleBuildsPrefabs
            .Where(build => build is Mill)
            .ToList();
    }

    private bool HasBuiltEquivalent(Build buildPrefab)
    {
        if (buildPrefab == null || Team == null)
        {
            return false;
        }

        IReadOnlyList<Build> registeredBuilds = Team.RegisteredBuilds;
        for (int i = 0; i < registeredBuilds.Count; i++)
        {
            Build existingBuild = registeredBuilds[i];
            if (existingBuild == null)
            {
                continue;
            }

            if (AreEquivalentBuilds(existingBuild, buildPrefab))
            {
                return true;
            }
        }

        return false;
    }

    private bool AreEquivalentBuilds(Build left, Build right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        if (left is Mill && right is Mill)
        {
            return true;
        }

        if (left is MilitaryBuild leftMilitary && right is MilitaryBuild rightMilitary)
        {
            return leftMilitary.buildType == rightMilitary.buildType;
        }

        return left.GetType() == right.GetType();
    }

    private MilitaryBuild[] GetOwnedMilitaryBuilds()
    {
        return Team == null
            ? Array.Empty<MilitaryBuild>()
            : Team.RegisteredBuilds.OfType<MilitaryBuild>().Where(build => build != null && !build.Hitbox.IsDead).ToArray();
    }

    private Mill[] GetOwnedMills()
    {
        return Team == null
            ? Array.Empty<Mill>()
            : Team.RegisteredBuilds.OfType<Mill>().Where(build => build != null && !build.Hitbox.IsDead).ToArray();
    }

    private void ConfigureHeroPersistence()
    {
        if (ControlledUnit != null && ControlledUnit.Hitbox != null)
        {
            ControlledUnit.Hitbox.destroyOnDeath = false;
        }
    }
}
