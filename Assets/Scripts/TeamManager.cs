using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TeamManager : MonoBehaviour
{
    private static readonly List<TeamManager> Teams = new List<TeamManager>();

    [SerializeField] private int teamId;
    [SerializeField] private int startingGold = 50;
    [SerializeField] private int maxPeasantCount = 20;
    [SerializeField] private int maxCombatUnitCount = 20;
    [SerializeField] private float buildFacingY;
    [SerializeField] private Transform unitsRoot;
    [SerializeField] private Transform buildsRoot;
    [SerializeField, ReadOnly] private int currentGold;

    public City city;
    public Color teamColor;
    public PlayerController player;
    public AIHeroController playerAI;

    private readonly List<Unit> _units = new List<Unit>();
    private readonly List<Build> _builds = new List<Build>();
    private readonly Dictionary<UnitStats, Unit.UnitState> _typeStates = new Dictionary<UnitStats, Unit.UnitState>();

    public int TeamId => teamId;
    public int CurrentGold => currentGold;
    public int MaxPeasantCount => maxPeasantCount;
    public int MaxCombatUnitCount => maxCombatUnitCount;
    public TeamManager EnemyTeam { get; private set; }
    public IReadOnlyList<Unit> RegisteredUnits => _units;
    public IReadOnlyList<Build> RegisteredBuilds => _builds;
    public Vector3 BasePosition => city != null ? city.transform.position : transform.position;
    public float BuildFacingY => buildFacingY;
    public Transform UnitsRoot => unitsRoot != null ? unitsRoot : transform;
    public Transform BuildsRoot => buildsRoot != null ? buildsRoot : transform;

    public event System.Action<int> OnGoldChanged;
    public event System.Action OnUnitsChanged;
    public event System.Action<UnitStats, Unit.UnitState> OnUnitTypeStateChanged;

    public static TeamManager GetLocalPlayerTeam()
    {
        return Teams.FirstOrDefault(team => team != null && team.player != null);
    }

    public void ConfigureGeneratedTeam(int id, bool isHumanControlled, Color color, PlayerController playerController, AIHeroController aiHeroController, City teamCity)
    {
        teamId = Mathf.Max(1, id);
        teamColor = color;
        player = isHumanControlled ? playerController : null;
        playerAI = isHumanControlled ? null : aiHeroController;
        city = teamCity;
        buildFacingY = isHumanControlled ? 180f : 0f;

        if (playerController != null)
        {
            playerController.gameObject.SetActive(isHumanControlled);
        }

        if (aiHeroController != null)
        {
            aiHeroController.gameObject.SetActive(!isHumanControlled);
        }
    }

    private void Awake()
    {
        if (teamId <= 0)
        {
            teamId = ParseTeamIdFromName();
        }

        if (player != null)
        {
            buildFacingY = 180f;
        }
        else if (playerAI != null)
        {
            buildFacingY = 0f;
        }

        currentGold = startingGold;
        Teams.Add(this);
    }

    private void Start()
    {
        if (unitsRoot == null)
        {
            Transform unitsChild = transform.Find("Units");
            if (unitsChild != null)
            {
                unitsRoot = unitsChild;
            }
        }

        if (buildsRoot == null)
        {
            Transform buildsChild = transform.Find("Builds");
            if (buildsChild != null)
            {
                buildsRoot = buildsChild;
            }
        }

        ResolveEnemyTeam();
        BindControllers();
        RegisterSceneChildren();
    }

    private void OnDestroy()
    {
        Teams.Remove(this);
    }

    private int ParseTeamIdFromName()
    {
        string digits = string.Empty;
        foreach (char c in gameObject.name)
        {
            if (char.IsDigit(c))
            {
                digits += c;
            }
        }

        if (int.TryParse(digits, out int parsed))
        {
            return parsed;
        }

        return Teams.Count + 1;
    }

    private void ResolveEnemyTeam()
    {
        EnemyTeam = Teams.FirstOrDefault(team => team != this);
    }

    private void BindControllers()
    {
        if (player != null)
        {
            player.BindTeam(this);
        }

        if (playerAI != null)
        {
            playerAI.BindTeam(this);
        }
    }

    private void RegisterSceneChildren()
    {
        foreach (Unit unit in GetComponentsInChildren<Unit>(true))
        {
            RegisterUnit(unit);
        }

        foreach (Build build in GetComponentsInChildren<Build>(true))
        {
            RegisterBuild(build);
        }
    }

    public bool SpendGold(int amount)
    {
        if (amount <= 0)
        {
            return true;
        }

        if (currentGold < amount)
        {
            return false;
        }

        currentGold -= amount;
        OnGoldChanged?.Invoke(currentGold);
        return true;
    }

    public void AddGold(int amount)
    {
        currentGold += Mathf.Max(0, amount);
        OnGoldChanged?.Invoke(currentGold);
    }

    public void RegisterUnit(Unit unit)
    {
        if (unit == null)
        {
            return;
        }

        if (!_units.Contains(unit))
        {
            IgnoreCollisionsWithAlliedUnits(unit);
            _units.Add(unit);
        }

        EnsureTrackedStateForUnit(unit);

        unit.AssignTeam(this);
        OnUnitsChanged?.Invoke();
    }

    public void UnregisterUnit(Unit unit)
    {
        _units.Remove(unit);
        OnUnitsChanged?.Invoke();
    }

    public void HandleHeroUnitDeath(Unit heroUnit)
    {
        if (heroUnit == null)
        {
            return;
        }

        UnregisterUnit(heroUnit);
        ReassignFollowPlayerUnitsAfterHeroDeath();
        if (city != null)
        {
            city.QueueHeroRespawn(heroUnit, 15f);
        }
    }

    public int GetCurrentUnitCount()
    {
        int count = 0;
        for (int i = 0; i < _units.Count; i++)
        {
            Unit unit = _units[i];
            if (unit == null || unit.IsDead || !unit.gameObject.activeInHierarchy)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    public int GetCurrentPeasantCount()
    {
        int count = 0;
        for (int i = 0; i < _units.Count; i++)
        {
            Unit unit = _units[i];
            if (!IsAliveTrackedUnit(unit) || !(unit is Peasant))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    public int GetCurrentCombatUnitCount()
    {
        int count = 0;
        for (int i = 0; i < _units.Count; i++)
        {
            Unit unit = _units[i];
            if (!IsAliveTrackedUnit(unit) || unit.IsHero || unit is Peasant)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    public bool HasReachedPeasantCap(int additionalUnits = 1)
    {
        return GetCurrentPeasantCount() + Mathf.Max(0, additionalUnits) > maxPeasantCount;
    }

    public bool HasReachedCombatUnitCap(int additionalUnits = 1)
    {
        return GetCurrentCombatUnitCount() + Mathf.Max(0, additionalUnits) > maxCombatUnitCount;
    }

    private void IgnoreCollisionsWithAlliedUnits(Unit newUnit)
    {
        if (newUnit == null || newUnit.CollisionCollider == null)
        {
            return;
        }

        for (int i = 0; i < _units.Count; i++)
        {
            Unit alliedUnit = _units[i];
            if (alliedUnit == null || alliedUnit == newUnit || alliedUnit.CollisionCollider == null)
            {
                continue;
            }

            Physics.IgnoreCollision(newUnit.CollisionCollider, alliedUnit.CollisionCollider, true);
        }
    }

    private bool IsAliveTrackedUnit(Unit unit)
    {
        return unit != null && !unit.IsDead && unit.gameObject.activeInHierarchy;
    }

    public void RegisterBuild(Build build)
    {
        if (build == null)
        {
            return;
        }

        if (!_builds.Contains(build))
        {
            _builds.Add(build);
        }

        build.AssignTeam(this);
        if (build is City teamCity)
        {
            city = teamCity;
        }
    }

    public void UnregisterBuild(Build build)
    {
        _builds.Remove(build);
    }

    public Unit GetHeroUnit()
    {
        if (player != null)
        {
            return player.ControlledUnit != null && !player.ControlledUnit.IsDead ? player.ControlledUnit : null;
        }

        if (playerAI != null)
        {
            return playerAI.ControlledUnit != null && !playerAI.ControlledUnit.IsDead ? playerAI.ControlledUnit : null;
        }

        return _units.FirstOrDefault(unit => unit != null && unit.IsHero && !unit.IsDead);
    }

    public Unit.UnitState ResolveInheritedState(Unit unit)
    {
        if (unit == null)
        {
            return Unit.UnitState.defendBase;
        }

        return GetStateForStats(unit.StatsAsset);
    }

    public void SetStateForStats(UnitStats unitStats, Unit.UnitState newState)
    {
        if (unitStats == null)
        {
            return;
        }

        _typeStates[unitStats] = newState;

        foreach (Unit unit in _units)
        {
            if (unit == null || unit.IsHero || unit is Peasant)
            {
                continue;
            }

            if (unit.StatsAsset == unitStats)
            {
                unit.SetState(newState);
            }
        }

        OnUnitTypeStateChanged?.Invoke(unitStats, newState);
    }

    public Unit.UnitState GetStateForStats(UnitStats unitStats)
    {
        if (unitStats != null && _typeStates.TryGetValue(unitStats, out Unit.UnitState savedState))
        {
            return savedState;
        }

        return Unit.UnitState.defendBase;
    }

    public bool HasActiveControllableType(UnitStats unitStats)
    {
        return GetRepresentativeControllableUnit(unitStats) != null;
    }

    public int GetActiveControllableTypeCount()
    {
        HashSet<UnitStats> activeTypes = new HashSet<UnitStats>();
        for (int i = 0; i < _units.Count; i++)
        {
            Unit unit = _units[i];
            if (!IsControllableTypedUnit(unit))
            {
                continue;
            }

            activeTypes.Add(unit.StatsAsset);
        }

        return activeTypes.Count;
    }

    public List<UnitStats> GetActiveControllableTypes()
    {
        List<UnitStats> activeTypes = new List<UnitStats>();
        HashSet<UnitStats> seenTypes = new HashSet<UnitStats>();
        for (int i = 0; i < _units.Count; i++)
        {
            Unit unit = _units[i];
            if (!IsControllableTypedUnit(unit) || seenTypes.Contains(unit.StatsAsset))
            {
                continue;
            }

            seenTypes.Add(unit.StatsAsset);
            activeTypes.Add(unit.StatsAsset);
        }

        return activeTypes;
    }

    public Unit GetRepresentativeControllableUnit(UnitStats unitStats)
    {
        return _units.FirstOrDefault(unit =>
            IsControllableTypedUnit(unit) &&
            unit.StatsAsset == unitStats);
    }

    public int GetFollowPlayerUnitCount()
    {
        return GetOrderedFollowPlayerUnits().Count;
    }

    public Vector3 GetFollowFormationPoint(Unit targetUnit, Vector3 heroPosition)
    {
        if (!IsFollowPlayerFormationUnit(targetUnit))
        {
            return heroPosition;
        }

        List<Unit> followers = GetOrderedFollowPlayerUnits();
        int followerIndex = followers.IndexOf(targetUnit);
        if (followerIndex < 0)
        {
            return heroPosition;
        }

        float baseRadius = targetUnit.StatsAsset == null ? 1.2f : Mathf.Max(0.55f, targetUnit.StatsAsset.followDistance * 0.65f);
        float spacing = targetUnit.StatsAsset == null ? 0.85f : Mathf.Max(0.35f, targetUnit.StatsAsset.separationDistance * 0.8f);

        if (followerIndex == 0)
        {
            return heroPosition + new Vector3(baseRadius, 0f, 0f);
        }

        int slotCounter = 0;
        int ring = 0;
        while (true)
        {
            float ringRadius = baseRadius + (ring * spacing);
            int slotsInRing = Mathf.Max(6, Mathf.CeilToInt((Mathf.PI * 2f * ringRadius) / spacing));
            for (int slot = 0; slot < slotsInRing; slot++)
            {
                if (slotCounter == followerIndex)
                {
                    float angle = (slot / (float)slotsInRing) * Mathf.PI * 2f;
                    Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ringRadius;
                    return heroPosition + offset;
                }

                slotCounter++;
            }

            ring++;
        }
    }

    public IEnumerable<Hitbox> EnumerateEnemyTargets()
    {
        if (EnemyTeam == null)
        {
            yield break;
        }

        foreach (Unit unit in EnemyTeam._units)
        {
            if (unit == null || unit.Hitbox == null || unit.Hitbox.IsDead)
            {
                continue;
            }

            yield return unit.Hitbox;
        }

        foreach (Build build in EnemyTeam._builds)
        {
            if (build == null || build.Hitbox == null || build.Hitbox.IsDead)
            {
                continue;
            }

            yield return build.Hitbox;
        }
    }

    public HarvestField FindAvailableHarvestField()
    {
        foreach (HarvestField harvestField in EnumerateHarvestFields())
        {
            if (harvestField != null && harvestField.IsAvailable)
            {
                return harvestField;
            }
        }

        return null;
    }

    public IEnumerable<HarvestField> EnumerateHarvestFields()
    {
        if (city != null && city.harvestFields != null)
        {
            foreach (HarvestField field in city.harvestFields)
            {
                if (field != null && field.gameObject.activeInHierarchy)
                {
                    yield return field;
                }
            }
        }

        foreach (Mill mill in _builds.OfType<Mill>())
        {
            if (mill.harvestFields == null)
            {
                continue;
            }

            foreach (HarvestField field in mill.harvestFields)
            {
                if (field != null && field.gameObject.activeInHierarchy)
                {
                    yield return field;
                }
            }
        }
    }

    public Tree FindClosestTree(Vector3 fromPosition)
    {
        Tree[] trees = FindObjectsOfType<Tree>();
        Tree bestTree = null;
        float bestDistance = float.MaxValue;

        foreach (Tree tree in trees)
        {
            if (tree == null || tree.IsDepleted)
            {
                continue;
            }

            if (tree.GetComponentInParent<BuildZone>() != null ||
                tree.GetComponentInParent<HarvestField>() != null ||
                IsNamedBuildZoneHierarchy(tree.transform))
            {
                continue;
            }

            float distance = Vector3.SqrMagnitude(tree.transform.position - fromPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestTree = tree;
            }
        }

        return bestTree;
    }

    private bool IsNamedBuildZoneHierarchy(Transform current)
    {
        while (current != null)
        {
            if (current.name.StartsWith("BuildZone"))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private void EnsureTrackedStateForUnit(Unit unit)
    {
        if (!IsControllableTypedUnit(unit))
        {
            return;
        }

        if (unit.StatsAsset != null && !_typeStates.ContainsKey(unit.StatsAsset))
        {
            _typeStates[unit.StatsAsset] = unit.CurrentUnitState;
        }
    }

    private bool IsControllableTypedUnit(Unit unit)
    {
        return unit != null &&
               !unit.IsDead &&
               unit.gameObject.activeInHierarchy &&
               !unit.IsHero &&
               !(unit is Peasant) &&
               unit.PrimaryUnitType != Unit.UnitType.other;
    }

    private bool IsFollowPlayerFormationUnit(Unit unit)
    {
        return IsControllableTypedUnit(unit) && unit.CurrentUnitState == Unit.UnitState.followPlayer;
    }

    private List<Unit> GetOrderedFollowPlayerUnits()
    {
        List<Unit> followers = new List<Unit>();
        for (int i = 0; i < _units.Count; i++)
        {
            Unit unit = _units[i];
            if (IsFollowPlayerFormationUnit(unit))
            {
                followers.Add(unit);
            }
        }

        followers.Sort((left, right) => left.GetInstanceID().CompareTo(right.GetInstanceID()));
        return followers;
    }

    private void ReassignFollowPlayerUnitsAfterHeroDeath()
    {
        HashSet<UnitStats> processedTypes = new HashSet<UnitStats>();
        for (int i = 0; i < _units.Count; i++)
        {
            Unit unit = _units[i];
            if (!IsFollowPlayerFormationUnit(unit))
            {
                continue;
            }

            Unit.UnitState replacementState = ResolveStateAfterHeroDeath(unit);
            if (unit.StatsAsset != null)
            {
                if (processedTypes.Add(unit.StatsAsset))
                {
                    SetStateForStats(unit.StatsAsset, replacementState);
                }
            }
            else
            {
                unit.SetState(replacementState);
            }
        }
    }

    private Unit.UnitState ResolveStateAfterHeroDeath(Unit unit)
    {
        if (unit == null || city == null || EnemyTeam == null || EnemyTeam.city == null)
        {
            return Unit.UnitState.defendBase;
        }

        Vector3 allyPosition = city.transform.position;
        Vector3 enemyPosition = EnemyTeam.city.transform.position;
        Vector3 axis = enemyPosition - allyPosition;
        axis.y = 0f;
        float lengthSqr = axis.sqrMagnitude;
        if (lengthSqr <= 0.001f)
        {
            return Unit.UnitState.defendBase;
        }

        Vector3 unitDelta = unit.transform.position - allyPosition;
        unitDelta.y = 0f;
        float normalizedProgress = Mathf.Clamp01(Vector3.Dot(unitDelta, axis) / lengthSqr);
        return normalizedProgress >= (2f / 3f)
            ? Unit.UnitState.raidEnemies
            : Unit.UnitState.defendBase;
    }
}
