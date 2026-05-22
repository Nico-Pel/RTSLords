using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class City : Build
{
    public int harvestFieldActiveOnStart = 2;
    public int peasantsSpawnedOnStart = 2;
    public Peasant peasantPrefab;
    public HarvestField[] harvestFields;

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
        if (playerActivator == null || playerActivator.LastTriggeringTeam != Team)
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
        return TrySpawnPeasantInternal(true, 0);
    }

    private bool TrySpawnPeasantInternal(bool payGold, int spawnIndex)
    {
        if (Team == null || peasantPrefab == null)
        {
            return false;
        }

        Hitbox peasantHitbox = peasantPrefab.GetComponent<Hitbox>();
        if (peasantHitbox == null || peasantHitbox.unitStats == null)
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
}
