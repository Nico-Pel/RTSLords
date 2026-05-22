using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mill : Build
{
    public int harvestFieldActiveOnStart = 0;
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
    }

    public override void OpenBuildMenu()
    {
        if (playerActivator == null || playerActivator.LastTriggeringTeam != Team)
        {
            return;
        }

        UIGame.Instance?.OpenMenuMill(this);
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
}
