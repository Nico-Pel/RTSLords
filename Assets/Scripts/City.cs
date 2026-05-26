using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class City : Build, IBuildProductionSource
{
    private const int MaxProductionQueueSize = 10;

    private enum ProductionEntryType
    {
        Peasant,
        HeroRespawn
    }

    private struct ProductionEntry
    {
        public ProductionEntryType type;
        public Peasant peasantPrefab;
        public Unit heroUnit;
        public float duration;
    }

    public int harvestFieldActiveOnStart = 2;
    public int peasantsSpawnedOnStart = 2;
    public Peasant peasantPrefab;
    public HarvestField[] harvestFields;

    private readonly Queue<ProductionEntry> _productionQueue = new Queue<ProductionEntry>();
    private Coroutine _productionRoutine;
    private ProductionEntry? _currentProduction;
    private float _currentProductionStartTime;
    private float _currentProductionDuration;

    protected override void Awake()
    {
        base.Awake();
        ActivateHarvestFields(harvestFieldActiveOnStart);
    }

    protected override void Start()
    {
        base.Start();
        ActivateHarvestFields(harvestFieldActiveOnStart);
        SpawnStartingPeasants();
    }

    public override void OpenBuildMenu()
    {
        if (!CanOpenMenuForHumanPlayer())
        {
            return;
        }

        UIGame.Instance?.OpenCityMenu(this);
    }

    public int HarvestCount()
    {
        int harvestActive = 0;
        foreach (HarvestField h in harvestFields)
        {
            if (h != null && h.gameObject.activeInHierarchy)
            {
                harvestActive++;
            }
        }

        return harvestActive;
    }

    public bool TrySpawnPeasant()
    {
        ProductionQueueBlockReason blockReason = GetPeasantQueueBlockReason();
        if (blockReason != ProductionQueueBlockReason.None)
        {
            return false;
        }

        CanQueuePeasant(out Hitbox peasantHitbox);
        if (!Team.SpendGold(peasantHitbox.unitStats.goldPrice))
        {
            return false;
        }

        _productionQueue.Enqueue(new ProductionEntry
        {
            type = ProductionEntryType.Peasant,
            peasantPrefab = peasantPrefab
        });
        if (_productionRoutine == null)
        {
            _productionRoutine = StartCoroutine(ProductionRoutine());
        }

        return true;
    }

    public bool QueueHeroRespawn(Unit heroUnit, float respawnDuration)
    {
        if (heroUnit == null || Team == null || GetProductionPreviewCount() >= MaxProductionQueueSize)
        {
            return false;
        }

        _productionQueue.Enqueue(new ProductionEntry
        {
            type = ProductionEntryType.HeroRespawn,
            heroUnit = heroUnit,
            duration = Mathf.Max(0.01f, respawnDuration)
        });

        if (_productionRoutine == null)
        {
            _productionRoutine = StartCoroutine(ProductionRoutine());
        }

        return true;
    }

    public ProductionQueueBlockReason GetPeasantQueueBlockReason()
    {
        if (!CanQueuePeasant(out Hitbox peasantHitbox))
        {
            return ProductionQueueBlockReason.Invalid;
        }

        if (GetProductionPreviewCount() >= MaxProductionQueueSize)
        {
            return ProductionQueueBlockReason.QueueFull;
        }

        if (Team == null)
        {
            return ProductionQueueBlockReason.Invalid;
        }

        if (Team.HasReachedPeasantCap())
        {
            return ProductionQueueBlockReason.UnitCapReached;
        }

        if (Team.CurrentGold < peasantHitbox.unitStats.goldPrice)
        {
            return ProductionQueueBlockReason.InsufficientGold;
        }

        return ProductionQueueBlockReason.None;
    }

    private bool TrySpawnPeasantInternal(bool payGold, int spawnIndex)
    {
        if (!CanQueuePeasant(out Hitbox peasantHitbox))
        {
            return false;
        }

        int goldPrice = peasantHitbox.unitStats.goldPrice;
        if (payGold && !Team.SpendGold(goldPrice))
        {
            return false;
        }

        Vector3 spawnPoint = GetPeasantSpawnPoint(spawnIndex);
        Peasant peasant = Instantiate(peasantPrefab, spawnPoint, Quaternion.Euler(0f, Team.BuildFacingY, 0f), Team.UnitsRoot);
        Team.RegisterUnit(peasant);
        peasant.InitializeWork(GetAvailableHarvestFieldForStart());
        return true;
    }

    private bool CanQueuePeasant(out Hitbox peasantHitbox)
    {
        peasantHitbox = null;
        if (Team == null || peasantPrefab == null)
        {
            return false;
        }

        peasantHitbox = peasantPrefab.GetComponent<Hitbox>();
        return peasantHitbox != null && peasantHitbox.unitStats != null;
    }

    private IEnumerator ProductionRoutine()
    {
        while (_productionQueue.Count > 0)
        {
            ProductionEntry nextEntry = _productionQueue.Dequeue();
            if (!CanResolveProductionDuration(nextEntry, out float duration))
            {
                continue;
            }

            _currentProduction = nextEntry;
            _currentProductionStartTime = Time.time;
            _currentProductionDuration = duration;
            if (duration > 0f)
            {
                yield return new WaitForSeconds(duration);
            }

            CompleteProduction(nextEntry);
            ClearCurrentProduction();
        }

        _productionRoutine = null;
    }

    public bool HasProductionInProgress => _currentProduction.HasValue;

    public float ProductionProgress01
    {
        get
        {
            if (!_currentProduction.HasValue || _currentProductionDuration <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01((Time.time - _currentProductionStartTime) / _currentProductionDuration);
        }
    }

    public int GetProductionPreviewCount()
    {
        return (_currentProduction.HasValue ? 1 : 0) + _productionQueue.Count;
    }

    public Sprite GetProductionPreviewSprite(int index)
    {
        if (index < 0)
        {
            return null;
        }

        if (_currentProduction.HasValue)
        {
            if (index == 0)
            {
                return ResolveProductionSprite(_currentProduction.Value);
            }

            index--;
        }

        if (index >= _productionQueue.Count)
        {
            return null;
        }

        int currentIndex = 0;
        foreach (ProductionEntry queuedEntry in _productionQueue)
        {
            if (currentIndex == index)
            {
                return ResolveProductionSprite(queuedEntry);
            }

            currentIndex++;
        }

        return null;
    }

    public BuildProductionVisualType GetProductionPreviewVisualType(int index)
    {
        if (index < 0)
        {
            return BuildProductionVisualType.Unit;
        }

        if (_currentProduction.HasValue)
        {
            if (index == 0)
            {
                return ResolveProductionVisualType(_currentProduction.Value);
            }

            index--;
        }

        if (index >= _productionQueue.Count)
        {
            return BuildProductionVisualType.Unit;
        }

        int currentIndex = 0;
        foreach (ProductionEntry queuedEntry in _productionQueue)
        {
            if (currentIndex == index)
            {
                return ResolveProductionVisualType(queuedEntry);
            }

            currentIndex++;
        }

        return BuildProductionVisualType.Unit;
    }

    public bool TryUnlockHarvestField(int goldCost = 8)
    {
        if (harvestFields == null || Team == null)
        {
            return false;
        }

        HarvestField nextField = FindFirstInactiveField();
        if (nextField == null)
        {
            return false;
        }

        if (!Team.SpendGold(goldCost))
        {
            return false;
        }

        nextField.gameObject.SetActive(true);
        return true;
    }

    public HarvestField FindFirstInactiveField()
    {
        foreach (HarvestField field in harvestFields)
        {
            if (field != null && !field.gameObject.activeSelf)
            {
                return field;
            }
        }

        return null;
    }

    private void ActivateHarvestFields(int count)
    {
        if (harvestFields == null)
        {
            return;
        }

        for (int i = 0; i < harvestFields.Length; i++)
        {
            if (harvestFields[i] == null)
            {
                continue;
            }

            harvestFields[i].gameObject.SetActive(i < count);
        }
    }

    private void SpawnStartingPeasants()
    {
        if (peasantsSpawnedOnStart <= 0)
        {
            return;
        }

        for (int i = 0; i < peasantsSpawnedOnStart; i++)
        {
            TrySpawnPeasantInternal(false, i);
        }
    }

    private HarvestField GetAvailableHarvestFieldForStart()
    {
        if (harvestFields == null)
        {
            return null;
        }

        for (int i = 0; i < harvestFields.Length; i++)
        {
            HarvestField field = harvestFields[i];
            if (field != null && field.gameObject.activeInHierarchy && field.IsAvailable)
            {
                return field;
            }
        }

        return null;
    }

    private Vector3 GetPeasantSpawnPoint(int spawnIndex)
    {
        Vector3 baseSpawnPoint = spawnPos == null ? transform.position + transform.forward * 2f : spawnPos.position;
        if (spawnIndex <= 0)
        {
            return baseSpawnPoint;
        }

        float laneOffset = 0.9f;
        int side = (spawnIndex % 2 == 0) ? 1 : -1;
        int row = (spawnIndex + 1) / 2;
        Vector3 rightOffset = transform.right * (side * laneOffset * row);
        Vector3 forwardOffset = transform.forward * (0.6f * row);
        return baseSpawnPoint + rightOffset + forwardOffset;
    }

    private void ClearCurrentProduction()
    {
        _currentProduction = null;
        _currentProductionStartTime = 0f;
        _currentProductionDuration = 0f;
    }

    private bool CanResolveProductionDuration(ProductionEntry entry, out float duration)
    {
        duration = 0f;
        switch (entry.type)
        {
            case ProductionEntryType.HeroRespawn:
                duration = Mathf.Max(0.01f, entry.duration);
                return entry.heroUnit != null;
            case ProductionEntryType.Peasant:
                if (!CanQueuePeasant(out Hitbox peasantHitbox))
                {
                    return false;
                }

                duration = peasantHitbox.unitStats.GetResolvedCreationTime(true);
                return true;
            default:
                return false;
        }
    }

    private void CompleteProduction(ProductionEntry entry)
    {
        switch (entry.type)
        {
            case ProductionEntryType.HeroRespawn:
                RespawnHero(entry.heroUnit);
                break;
            case ProductionEntryType.Peasant:
                TrySpawnPeasantInternal(false, 0);
                break;
        }
    }

    private void RespawnHero(Unit heroUnit)
    {
        if (heroUnit == null || Team == null)
        {
            return;
        }

        Vector3 spawnPoint = spawnPos == null ? transform.position + transform.forward * 2f : spawnPos.position;
        heroUnit.RespawnAt(spawnPoint, Quaternion.Euler(0f, Team.BuildFacingY, 0f));
    }

    private Sprite ResolveProductionSprite(ProductionEntry entry)
    {
        switch (entry.type)
        {
            case ProductionEntryType.HeroRespawn:
                return ResolveUnitSprite(entry.heroUnit);
            case ProductionEntryType.Peasant:
                return ResolvePeasantSprite(entry.peasantPrefab);
            default:
                return null;
        }
    }

    private BuildProductionVisualType ResolveProductionVisualType(ProductionEntry entry)
    {
        return entry.type == ProductionEntryType.HeroRespawn
            ? BuildProductionVisualType.Hero
            : BuildProductionVisualType.Unit;
    }

    private Sprite ResolvePeasantSprite(Peasant peasant)
    {
        if (peasant == null)
        {
            return null;
        }

        Hitbox hitbox = peasant.GetComponent<Hitbox>();
        if (hitbox != null && hitbox.unitStats != null)
        {
            return hitbox.unitStats.sprite;
        }

        return null;
    }

    private Sprite ResolveUnitSprite(Unit unit)
    {
        if (unit == null)
        {
            return null;
        }

        if (unit.unitSprite != null)
        {
            return unit.unitSprite;
        }

        Hitbox hitbox = unit.GetComponent<Hitbox>();
        return hitbox != null && hitbox.unitStats != null ? hitbox.unitStats.sprite : null;
    }
}
