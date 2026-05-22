using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TeamManager : MonoBehaviour
{
    private static readonly List<TeamManager> Teams = new List<TeamManager>();

    [SerializeField] private int teamId;
    [SerializeField] private int startingGold = 50;
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
    private readonly Dictionary<Unit.UnitType, Unit.UnitState> _typeStates = new Dictionary<Unit.UnitType, Unit.UnitState>();

    public int TeamId => teamId;
    public int CurrentGold => currentGold;
    public TeamManager EnemyTeam { get; private set; }
    public IReadOnlyList<Unit> RegisteredUnits => _units;
    public IReadOnlyList<Build> RegisteredBuilds => _builds;
    public Vector3 BasePosition => city != null ? city.transform.position : transform.position;
    public float BuildFacingY => buildFacingY;
    public Transform UnitsRoot => unitsRoot != null ? unitsRoot : transform;
    public Transform BuildsRoot => buildsRoot != null ? buildsRoot : transform;

    public event System.Action<int> OnGoldChanged;
    public event System.Action OnUnitsChanged;
    public event System.Action<Unit.UnitType, Unit.UnitState> OnUnitTypeStateChanged;

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
            return player.ControlledUnit;
        }

        if (playerAI != null)
        {
            return playerAI.ControlledUnit;
        }

        return _units.FirstOrDefault(unit => unit != null && unit.IsHero);
    }

    public Unit.UnitState ResolveInheritedState(Unit unit)
    {
        if (unit == null)
        {
            return Unit.UnitState.defendBase;
        }

        return GetStateForType(unit.PrimaryUnitType);
    }

    public void SetStateForType(Unit.UnitType unitType, Unit.UnitState newState)
    {
        _typeStates[unitType] = newState;

        foreach (Unit unit in _units)
        {
            if (unit == null || unit.IsHero || unit is Peasant)
            {
                continue;
            }

            if (unit.PrimaryUnitType == unitType)
            {
                unit.SetState(newState);
            }
        }

        OnUnitTypeStateChanged?.Invoke(unitType, newState);
    }

    public Unit.UnitState GetStateForType(Unit.UnitType unitType)
    {
        if (_typeStates.TryGetValue(unitType, out Unit.UnitState savedState))
        {
            return savedState;
        }

        return Unit.UnitState.defendBase;
    }

    public bool HasActiveControllableType(Unit.UnitType unitType)
    {
        return GetRepresentativeControllableUnit(unitType) != null;
    }

    public int GetActiveControllableTypeCount()
    {
        HashSet<Unit.UnitType> activeTypes = new HashSet<Unit.UnitType>();
        for (int i = 0; i < _units.Count; i++)
        {
            Unit unit = _units[i];
            if (!IsControllableTypedUnit(unit))
            {
                continue;
            }

            activeTypes.Add(unit.PrimaryUnitType);
        }

        return activeTypes.Count;
    }

    public List<Unit.UnitType> GetActiveControllableTypes()
    {
        List<Unit.UnitType> activeTypes = new List<Unit.UnitType>();
        HashSet<Unit.UnitType> seenTypes = new HashSet<Unit.UnitType>();
        for (int i = 0; i < _units.Count; i++)
        {
            Unit unit = _units[i];
            if (!IsControllableTypedUnit(unit) || seenTypes.Contains(unit.PrimaryUnitType))
            {
                continue;
            }

            seenTypes.Add(unit.PrimaryUnitType);
            activeTypes.Add(unit.PrimaryUnitType);
        }

        return activeTypes;
    }

    public Unit GetRepresentativeControllableUnit(Unit.UnitType unitType)
    {
        return _units.FirstOrDefault(unit =>
            IsControllableTypedUnit(unit) &&
            unit.PrimaryUnitType == unitType);
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

            if (tree.GetComponentInParent<BuildZone>() != null || tree.GetComponentInParent<HarvestField>() != null)
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

    private void EnsureTrackedStateForUnit(Unit unit)
    {
        if (!IsControllableTypedUnit(unit))
        {
            return;
        }

        if (!_typeStates.ContainsKey(unit.PrimaryUnitType))
        {
            _typeStates[unit.PrimaryUnitType] = unit.CurrentUnitState;
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
}
