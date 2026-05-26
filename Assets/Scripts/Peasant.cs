using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Peasant : Unit
{
    [Header("Peasant Animation")]
    public string lumberBoolName = "Lumber";
    public string harvestBoolName = "Harvest";
    public string woodBoolName = "Wood";
    public string woodDropTriggerName = "WoodDrop";
    public float woodDropDuration = 1f;
    public float lumberImpactFallbackDelay = 0.18f;

    private HarvestField _assignedField;
    private Tree _assignedTree;
    private float _workTimer;
    private int _currentWoodHits;
    private bool _returningToCity;
    private bool _isDroppingWood;
    private bool _waitingForWoodImpact;
    private int _pendingWoodImpactVersion;

    public void InitializeWork()
    {
        _workTimer = 0f;
        _returningToCity = false;
        _assignedTree = null;
        _currentWoodHits = 0;
        _isDroppingWood = false;
        _waitingForWoodImpact = false;
        ClearMovementPath();
        RefreshHarvestAssignment();
    }

    public void InitializeWork(HarvestField preferredField)
    {
        _workTimer = 0f;
        _returningToCity = false;
        _assignedTree = null;
        _currentWoodHits = 0;
        _isDroppingWood = false;
        _waitingForWoodImpact = false;
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
                _isDroppingWood = false;
                _waitingForWoodImpact = false;
            }
        }
    }

    private Vector3 HandleFieldHarvest()
    {
        if (_assignedField == null || !_assignedField.gameObject.activeInHierarchy)
        {
            if (_assignedField != null)
            {
                _assignedField.Release(this);
                _assignedField = null;
            }

            RefreshHarvestAssignment();
            if (_assignedField == null)
            {
                return HandleWoodHarvest();
            }
        }

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
            SpawnCoinEffect.SpawnAbove(transform);
        }

        return Vector3.zero;
    }

    private Vector3 HandleWoodHarvest()
    {
        if (Team == null || Team.city == null)
        {
            return Vector3.zero;
        }

        if (_isDroppingWood)
        {
            return Vector3.zero;
        }

        if (_returningToCity)
        {
            Vector3 deliveryPoint = GetCityDeliveryPoint();
            float deliveryRadius = GetCityDeliveryRadius();
            Vector3 deltaToDelivery = GetFlatDelta(deliveryPoint);
            if (deltaToDelivery.magnitude > deliveryRadius)
            {
                return GetDirectionTo(deliveryPoint, deliveryRadius);
            }

            BeginWoodDrop();
            return Vector3.zero;
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
            _waitingForWoodImpact = false;
            return GetDirectionTo(_assignedTree.transform.position, 1.75f);
        }

        _workTimer -= Time.deltaTime;
        if (_workTimer <= 0f && !_waitingForWoodImpact)
        {
            _workTimer = Mathf.Max(0.25f, Stats.woodHitInterval);
            _waitingForWoodImpact = true;
            _pendingWoodImpactVersion++;
            StartCoroutine(ResolveWoodImpactAfterDelay(_pendingWoodImpactVersion));
        }

        return Vector3.zero;
    }

    public override void AnimationHarvestImpact()
    {
        if (!_waitingForWoodImpact)
        {
            return;
        }

        ResolveWoodImpact();
    }

    protected override void UpdateAnimators(Vector3 moveDirection)
    {
        base.UpdateAnimators(moveDirection);

        if (moveAnimator == null)
        {
            return;
        }

        bool isMoving = moveDirection.sqrMagnitude > 0.0001f;
        bool isHarvestingField = _assignedField != null && !isMoving;
        bool isLumbering = _assignedField == null && _assignedTree != null && !_returningToCity && !isMoving;
        bool isCarryingWood = _returningToCity || _isDroppingWood;

        if (!string.IsNullOrWhiteSpace(moveBoolName))
        {
            TrySetAnimatorBool(moveAnimator, moveBoolName, isMoving);
        }

        if (!string.IsNullOrWhiteSpace(harvestBoolName))
        {
            TrySetAnimatorBool(moveAnimator, harvestBoolName, isHarvestingField);
        }

        if (!string.IsNullOrWhiteSpace(lumberBoolName))
        {
            TrySetAnimatorBool(moveAnimator, lumberBoolName, isLumbering);
        }

        if (!string.IsNullOrWhiteSpace(woodBoolName))
        {
            TrySetAnimatorBool(moveAnimator, woodBoolName, isCarryingWood);
        }
    }

    private Vector3 GetCityDeliveryPoint()
    {
        if (Team == null || Team.city == null)
        {
            return transform.position;
        }

        Collider cityCollider = Team.city.GetComponent<Collider>();
        if (cityCollider != null)
        {
            Vector3 closestPoint = cityCollider.ClosestPoint(transform.position);
            Vector3 flatOffset = closestPoint - Team.city.transform.position;
            flatOffset.y = 0f;
            if (flatOffset.sqrMagnitude > 0.0001f)
            {
                return closestPoint;
            }
        }

        if (Team.city.spawnPos != null)
        {
            return Team.city.spawnPos.position;
        }

        return Team.city.transform.position;
    }

    private float GetCityDeliveryRadius()
    {
        if (Team == null || Team.city == null)
        {
            return 0.45f;
        }

        Collider cityCollider = Team.city.GetComponent<Collider>();
        if (cityCollider != null)
        {
            return Mathf.Max(0.3f, Stats == null ? 0.45f : Mathf.Min(0.55f, Mathf.Max(0.3f, Stats.attackMoveStopDistance * 0.5f)));
        }

        return 0.45f;
    }

    private IEnumerator ResolveWoodImpactAfterDelay(int impactVersion)
    {
        yield return new WaitForSeconds(Mathf.Max(0.01f, lumberImpactFallbackDelay));

        if (!_waitingForWoodImpact || impactVersion != _pendingWoodImpactVersion)
        {
            yield break;
        }

        ResolveWoodImpact();
    }

    private void ResolveWoodImpact()
    {
        _waitingForWoodImpact = false;

        if (_assignedTree == null)
        {
            return;
        }

        if (_assignedTree.HarvestOneWood())
        {
            _currentWoodHits++;
            _assignedTree.PlayHitShake();
        }

        if (_currentWoodHits >= Mathf.Max(1, Stats.woodHitsPerDelivery) || _assignedTree.IsDepleted)
        {
            _returningToCity = true;
        }
    }

    private void BeginWoodDrop()
    {
        _isDroppingWood = true;
        _returningToCity = false;
        _waitingForWoodImpact = false;
        _assignedTree = null;
        _workTimer = 0f;
        ClearMovementPath();

        if (moveAnimator != null &&
            !string.IsNullOrWhiteSpace(woodDropTriggerName) &&
            HasAnimatorParameter(moveAnimator, woodDropTriggerName, AnimatorControllerParameterType.Trigger))
        {
            moveAnimator.ResetTrigger(woodDropTriggerName);
            moveAnimator.SetTrigger(woodDropTriggerName);
        }

        _currentWoodHits = 0;
        Team.AddGold(1);
        SpawnCoinEffect.SpawnAbove(transform);
        StartCoroutine(FinishWoodDropAfterDelay());
    }

    private IEnumerator FinishWoodDropAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0.01f, woodDropDuration));

        _isDroppingWood = false;
        RefreshHarvestAssignment();

        if (_assignedField != null)
        {
            yield break;
        }

        if (_assignedTree == null || _assignedTree.IsDepleted)
        {
            _assignedTree = Team != null ? Team.FindClosestTree(Team.BasePosition) : null;
        }
    }
}
