using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Peasant : Unit
{
    private HarvestField _assignedField;
    private Tree _assignedTree;
    private float _workTimer;
    private int _currentWoodHits;
    private bool _returningToCity;

    public void InitializeWork()
    {
        _workTimer = 0f;
        _returningToCity = false;
        _assignedTree = null;
        _currentWoodHits = 0;
        ClearMovementPath();
        RefreshHarvestAssignment();
    }

    public void InitializeWork(HarvestField preferredField)
    {
        _workTimer = 0f;
        _returningToCity = false;
        _assignedTree = null;
        _currentWoodHits = 0;
        ClearMovementPath();

        if (_assignedField != null && _assignedField != preferredField)
        {
            _assignedField.Release(this);
            _assignedField = null;
        }

        if (preferredField != null && preferredField.gameObject.activeInHierarchy && preferredField.TryAssign(this))
        {
            _assignedField = preferredField;
        }

        RefreshHarvestAssignment();
    }

    protected override Vector3 GetAutoMoveDirection()
    {
        if (Team == null || Stats == null)
        {
            return Vector3.zero;
        }

        RefreshHarvestAssignment();

        if (_assignedField != null)
        {
            return HandleFieldHarvest();
        }

        return HandleWoodHarvest();
    }

    private void RefreshHarvestAssignment()
    {
        if (_assignedField != null && !_assignedField.gameObject.activeInHierarchy)
        {
            _assignedField.Release(this);
            _assignedField = null;
        }

        if (_assignedField == null && Team != null)
        {
            HarvestField field = Team.FindAvailableHarvestField();
            if (field != null && field.TryAssign(this))
            {
                _assignedField = field;
                _returningToCity = false;
                _assignedTree = null;
                _currentWoodHits = 0;
            }
        }
    }

    private Vector3 HandleFieldHarvest()
    {
        Vector3 workPosition = _assignedField.GetWorkPosition();
        Vector3 delta = GetFlatDelta(workPosition);
        float arrivalDistance = _assignedField.GetArrivalDistance();

        if (delta.magnitude > arrivalDistance)
        {
            return GetDirectionTo(workPosition, arrivalDistance);
        }

        _workTimer -= Time.deltaTime;
        if (_workTimer <= 0f)
        {
            _workTimer = Mathf.Max(1f, Stats.harvestInterval);
            Team.AddGold(1);
        }

        return Vector3.zero;
    }

    private Vector3 HandleWoodHarvest()
    {
        if (Team == null || Team.city == null)
        {
            return Vector3.zero;
        }

        if (_returningToCity)
        {
            Vector3 deliveryPoint = GetCityDeliveryPoint();
            Vector3 deltaToCity = GetFlatDelta(deliveryPoint);
            if (deltaToCity.magnitude > 2f)
            {
                return GetDirectionTo(deliveryPoint, 2f);
            }

            Team.AddGold(1);
            _returningToCity = false;
            _currentWoodHits = 0;
            _assignedTree = null;
            _workTimer = 0f;
            ClearMovementPath();
            RefreshHarvestAssignment();

            if (_assignedField != null)
            {
                return HandleFieldHarvest();
            }

            if (_assignedTree == null || _assignedTree.IsDepleted)
            {
                _assignedTree = Team.FindClosestTree(Team.BasePosition);
            }

            return _assignedTree != null
                ? GetDirectionTo(_assignedTree.transform.position, 1.75f)
                : Vector3.zero;
        }

        if (_assignedTree == null || _assignedTree.IsDepleted)
        {
            _assignedTree = Team.FindClosestTree(Team.BasePosition);
            if (_assignedTree == null)
            {
                return GetDirectionTo(Team.BasePosition);
            }
        }

        Vector3 deltaToTree = GetFlatDelta(_assignedTree.transform.position);
        if (deltaToTree.magnitude > 1.75f)
        {
            return GetDirectionTo(_assignedTree.transform.position, 1.75f);
        }

        _workTimer -= Time.deltaTime;
        if (_workTimer <= 0f)
        {
            _workTimer = Mathf.Max(0.25f, Stats.woodHitInterval);
            if (_assignedTree.HarvestOneWood())
            {
                _currentWoodHits++;
            }

            if (_currentWoodHits >= Mathf.Max(1, Stats.woodHitsPerDelivery) || _assignedTree.IsDepleted)
            {
                _returningToCity = true;
            }
        }

        return Vector3.zero;
    }

    private Vector3 GetCityDeliveryPoint()
    {
        if (Team == null || Team.city == null)
        {
            return transform.position;
        }

        if (Team.city.spawnPos != null)
        {
            return Team.city.spawnPos.position;
        }

        return Team.city.transform.position;
    }
}
