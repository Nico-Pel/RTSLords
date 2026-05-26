using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using System;

public class Unit : GameBehaviour
{
    private const string ToonShaderName = "RTSLords/Simple Toon Outline";

    public enum UnitState
    {
        defendBase,
        followPlayer,
        raidEnemies
    }

    public string unitName;
    public Sprite unitSprite;
    public bool isHero;
    public Transform hpBarPos;

    [Header("Animation")]
    public Animator moveAnimator;
    public Animator attackAnimator;
    public Transform projectileSpawnPos;
    public string moveBoolName = "Move";
    public string attackTriggerName = "Attack";
    public bool useAnimationEventForAttackImpact = true;
    public float attackImpactFallbackDelay = 0.2f;

    [Header("Combat Feel")]
    public float attackStopDuration = 1.5f;

    public enum UnitType
    {
        infantryman,
        archer,
        spearman,
        cavalier,
        build,
        other,
        wolf
    }
    
    public enum ArmorType
    {
        lightArmor,
        heavyArmor,
        building,
    }

    private static readonly int BlueReplaceColorID = Shader.PropertyToID("_BlueReplaceColor");

    [SerializeField] private UnitState _currentUnitState = UnitState.defendBase;

    private MaterialPropertyBlock _propertyBlock;
    private Vector3 _controllerMoveInput;
    protected float _attackCooldownTimer;
    private float _attackStopTimer;
    private CapsuleCollider _collisionCollider;
    private Rigidbody _rigidbody;
    private Hitbox _pendingAttackTarget;
    private int _pendingAttackDamages;
    private Hitbox.DamageTypes _pendingAttackDamageType;
    private GameObject _pendingProjectilePrefab;
    private float _pendingProjectileSpeed;
    private int _pendingAttackVersion;
    private bool _hasPendingAttackImpact;
    private readonly List<Vector3> _pathWaypoints = new List<Vector3>();
    private Vector3 _currentPathTarget;
    private int _currentWaypointIndex;
    private float _nextPathRefreshTime;
    protected Hitbox _currentCombatTarget;
    protected float _combatElapsedWithoutHit;
    private float _timeSinceLastReceivedDamage;
    private float _timeSinceLastAttackPerformed;
    private float _regenTickTimer;
    private Renderer[] _cachedRenderers;
    private Collider[] _cachedColliders;
    private Animator[] _cachedAnimators;

    public TeamManager Team { get; private set; }
    public Hitbox Hitbox { get; private set; }
    public UnitState CurrentUnitState => _currentUnitState;
    public bool IsHero => isHero;
    public UnitType PrimaryUnitType => ResolveStatsAsset() != null ? ResolveStatsAsset().PrimaryUnitType : UnitType.other;
    public UnitStats StatsAsset => ResolveStatsAsset();
    public bool IsDead => Hitbox != null && Hitbox.IsDead;
    public Collider CollisionCollider => _collisionCollider;
    protected UnitStats Stats => Hitbox != null ? Hitbox.unitStats : null;
    public event Action<Unit> OnUnitDied;
    public event Action<Unit> OnUnitRespawned;

    protected virtual void Awake()
    {
        _propertyBlock = new MaterialPropertyBlock();
        Hitbox = GetComponent<Hitbox>();
        if (Hitbox != null)
        {
            Hitbox.OnDeath += HandleHitboxDeath;
            Hitbox.OnDamaged += HandleHitboxDamaged;
        }

        EnsureCollisionSetup();
        ResolveAnimators();
        CachePresentationComponents();
    }

    private UnitStats ResolveStatsAsset()
    {
        if (Stats != null)
        {
            return Stats;
        }

        Hitbox localHitbox = Hitbox != null ? Hitbox : GetComponent<Hitbox>();
        return localHitbox != null ? localHitbox.unitStats : null;
    }

    protected virtual void Start()
    {
        if (string.IsNullOrWhiteSpace(unitName))
        {
            unitName = gameObject.name;
        }

        if (unitSprite == null && Stats != null)
        {
            unitSprite = Stats.sprite;
        }

        TeamManager teamManager = GetComponentInParent<TeamManager>();
        if (teamManager != null)
        {
            teamManager.RegisterUnit(this);
        }
    }

    protected virtual void Update()
    {
        if (IsDead)
        {
            return;
        }

        if (_attackCooldownTimer > 0f)
        {
            _attackCooldownTimer -= Time.deltaTime;
        }

        if (_attackStopTimer > 0f)
        {
            _attackStopTimer -= Time.deltaTime;
        }

        _combatElapsedWithoutHit += Time.deltaTime;
        _timeSinceLastReceivedDamage += Time.deltaTime;
        _timeSinceLastAttackPerformed += Time.deltaTime;

        Vector3 moveDirection = GetMoveDirection();
        TickCombat();
        TickHeroRegeneration();

        if (ShouldPauseMovementForAttack())
        {
            moveDirection = Vector3.zero;
        }

        Move(moveDirection, Stats == null ? 1f : Stats.speed);
        UpdateAnimators(moveDirection);
        _controllerMoveInput = Vector3.zero;
    }

    public virtual void AssignTeam(TeamManager team)
    {
        Team = team;
        if (Hitbox != null)
        {
            Hitbox.AssignTeam(team);
        }

        ApplyTeamColor();
    }

    public void SetState(UnitState state)
    {
        if (_currentUnitState == state)
        {
            return;
        }

        _currentUnitState = state;
        CancelCombatIntent();
    }

    public virtual void SetControllerMoveInput(Vector3 moveDirection)
    {
        _controllerMoveInput = Vector3.ClampMagnitude(new Vector3(moveDirection.x, 0f, moveDirection.z), 1f);
    }

    protected virtual Vector3 GetMoveDirection()
    {
        if (_controllerMoveInput.sqrMagnitude > 0.0001f)
        {
            return _controllerMoveInput;
        }

        if (isHero)
        {
            return Vector3.zero;
        }

        return GetAutoMoveDirection();
    }

    protected virtual Vector3 GetAutoMoveDirection()
    {
        if (Team == null)
        {
            return Vector3.zero;
        }

        if (ShouldRetreatForCurrentState())
        {
            CancelCombatIntent();
            return GetRetreatMoveDirection();
        }

        if (_currentUnitState == UnitState.followPlayer && HasValidCombatTarget() && !IsFollowCombatTargetAllowed(_currentCombatTarget))
        {
            CancelCombatIntent();
            return GetRetreatMoveDirection();
        }

        if (HasValidCombatTarget())
        {
            return GetDirectionTo(_currentCombatTarget.transform.position);
        }

        switch (_currentUnitState)
        {
            case UnitState.defendBase:
                return GetDirectionTo(GetDefendAnchor());
            case UnitState.followPlayer:
                Unit hero = Team.GetHeroUnit();
                if (hero == null)
                {
                    return GetDirectionTo(GetDefendAnchor());
                }

                return GetDirectionTo(GetFollowFormationAnchor(hero), 0.35f);
            case UnitState.raidEnemies:
                if (Team.EnemyTeam != null && Team.EnemyTeam.city != null)
                {
                    return GetDirectionTo(Team.EnemyTeam.city.transform.position);
                }

                break;
        }

        return Vector3.zero;
    }

    protected virtual void TickCombat()
    {
        if (Team == null || Stats == null)
        {
            return;
        }

        if (ShouldRetreatForCurrentState())
        {
            CancelCombatIntent();
            return;
        }

        if (_currentUnitState == UnitState.followPlayer && HasValidCombatTarget() && !IsFollowCombatTargetAllowed(_currentCombatTarget))
        {
            CancelCombatIntent();
            return;
        }

        float detectionRange = _currentUnitState == UnitState.raidEnemies ? Stats.detectionRaidRange : Stats.detectionDefenseRange;
        if (isHero)
        {
            detectionRange = Mathf.Max(detectionRange, Stats.aggroRange);
        }

        if (!HasValidCombatTarget())
        {
            if (CanAcquireCombatTargetForCurrentState())
            {
                Hitbox nextTarget = FindNearestEnemyTarget(detectionRange);
                if (_currentUnitState != UnitState.followPlayer || IsFollowCombatTargetAllowed(nextTarget))
                {
                    _currentCombatTarget = nextTarget;
                }
            }
        }

        Hitbox target = _currentCombatTarget;
        if (target == null)
        {
            return;
        }

        float distanceToSurface = GetDistanceToTargetSurface(target);
        if (distanceToSurface > Stats.range)
        {
            return;
        }

        if (_attackCooldownTimer > 0f)
        {
            return;
        }

        _attackCooldownTimer = Mathf.Max(0.1f, Stats.attackCooldown);
        PerformAttack(target);
    }

    protected virtual void PerformAttack(Hitbox target)
    {
        if (target == null || Stats == null)
        {
            return;
        }

        if (Stats.faceTargetOnAttack)
        {
            FaceAttackTarget(target);
        }
        _timeSinceLastAttackPerformed = 0f;
        _regenTickTimer = 0f;
        TriggerAttackAnimation();

        if (!isHero)
        {
            _attackStopTimer = Mathf.Max(0f, attackStopDuration);
        }

        if (useAnimationEventForAttackImpact && attackAnimator != null)
        {
            QueuePendingAttackImpact(target);
            return;
        }

        ApplyAttackImpact(target, Stats.damages, Stats.damageType, Stats.useProjectile ? Stats.projectilePrefab : null, Stats.projectileSpeed);
    }

    public void AnimationAttackImpact()
    {
        if (!_hasPendingAttackImpact)
        {
            return;
        }

        ResolvePendingAttackImpact();
    }

    public virtual void AnimationHarvestImpact()
    {
    }

    private void QueuePendingAttackImpact(Hitbox target)
    {
        _pendingAttackTarget = target;
        _pendingAttackDamages = Stats.damages;
        _pendingAttackDamageType = Stats.damageType;
        _pendingProjectilePrefab = Stats.useProjectile ? Stats.projectilePrefab : null;
        _pendingProjectileSpeed = Stats.projectileSpeed;
        _hasPendingAttackImpact = true;
        _pendingAttackVersion++;

        StartCoroutine(ResolvePendingAttackImpactAfterDelay(_pendingAttackVersion));
    }

    private IEnumerator ResolvePendingAttackImpactAfterDelay(int attackVersion)
    {
        yield return new WaitForSeconds(Mathf.Max(0.01f, attackImpactFallbackDelay));

        if (!_hasPendingAttackImpact || attackVersion != _pendingAttackVersion)
        {
            yield break;
        }

        ResolvePendingAttackImpact();
    }

    private void ResolvePendingAttackImpact()
    {
        Hitbox target = _pendingAttackTarget;
        int damages = _pendingAttackDamages;
        Hitbox.DamageTypes damageType = _pendingAttackDamageType;
        GameObject projectilePrefab = _pendingProjectilePrefab;
        float projectileSpeed = _pendingProjectileSpeed;

        _hasPendingAttackImpact = false;
        _pendingAttackTarget = null;
        _pendingProjectilePrefab = null;

        ApplyAttackImpact(target, damages, damageType, projectilePrefab, projectileSpeed);
    }

    private void ApplyAttackImpact(Hitbox target, int damages, Hitbox.DamageTypes damageType, GameObject projectilePrefab, float projectileSpeed)
    {
        if (target == null || target.IsDead)
        {
            return;
        }

        if (Stats.faceTargetOnAttack)
        {
            FaceAttackTarget(target);
        }
        _currentCombatTarget = target;
        _combatElapsedWithoutHit = 0f;

        if (projectilePrefab != null)
        {
            GameObject projectileObject = Instantiate(
                projectilePrefab,
                GetProjectileSpawnPosition(),
                Quaternion.identity);

            Projectile projectile = projectileObject.GetComponent<Projectile>();
            if (projectile == null)
            {
                projectile = projectileObject.AddComponent<Projectile>();
            }

            projectile.Setup(target, damages, damageType, projectileSpeed, Stats, Hitbox);
            return;
        }

        target.TakeDamage(damages, damageType, Stats, Hitbox);
    }

    private void FaceAttackTarget(Hitbox target)
    {
        if (target == null)
        {
            return;
        }

        Vector3 lookDirection = GetFlatDelta(target.transform.position);
        if (lookDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
    }

    public virtual Hitbox FindNearestEnemyTarget(float maxRange)
    {
        if (Hitbox == null)
        {
            return null;
        }

        float bestDistance = maxRange * maxRange;
        Hitbox bestTarget = null;

        Hitbox[] hitboxes = FindObjectsOfType<Hitbox>(true);
        for (int i = 0; i < hitboxes.Length; i++)
        {
            Hitbox target = hitboxes[i];
            if (target == null || target.IsDead || target.teamID == Hitbox.teamID)
            {
                continue;
            }

            if (!target.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (target.OwnerUnit != null && !target.OwnerUnit.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (target.OwnerBuild != null && !target.OwnerBuild.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector3 delta = GetFlatDelta(target.transform.position);
            float sqrDistance = delta.sqrMagnitude;
            if (sqrDistance < bestDistance)
            {
                bestDistance = sqrDistance;
                bestTarget = target;
            }
        }

        return bestTarget;
    }

    protected virtual Vector3 GetDefendAnchor()
    {
        if (Team == null)
        {
            return transform.position;
        }

        float radius = Stats == null ? 3f : Stats.defendAnchorDistance;
        float angle = Mathf.Abs(GetInstanceID() % 360) * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
        return Team.BasePosition + offset;
    }

    protected Vector3 GetDirectionTo(Vector3 worldPosition)
    {
        return GetDirectionTo(worldPosition, GetStoppingDistance());
    }

    protected Vector3 GetDirectionTo(Vector3 worldPosition, float stoppingDistance)
    {
        if (!isHero)
        {
            return GetPathDirectionTo(worldPosition, stoppingDistance);
        }

        Vector3 delta = GetFlatDelta(worldPosition);
        if (delta.sqrMagnitude <= stoppingDistance * stoppingDistance)
        {
            return Vector3.zero;
        }

        return delta.normalized;
    }

    protected Vector3 GetFlatDelta(Vector3 worldPosition)
    {
        Vector3 delta = worldPosition - transform.position;
        delta.y = 0f;
        return delta;
    }

    protected virtual void Move(Vector3 moveDirection, float speed)
    {
        if (moveDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Vector3 normalizedDirection = moveDirection.normalized;
        Vector3 avoidanceDirection = GetAvoidanceDirection();
        if (avoidanceDirection.sqrMagnitude > 0.0001f)
        {
            normalizedDirection = (normalizedDirection + (avoidanceDirection * 1.15f)).normalized;
        }

        float moveDistance = speed * Time.deltaTime;
        Vector3 displacement = normalizedDirection * moveDistance;
        if (_collisionCollider != null)
        {
            displacement = ResolveBlockedMovement(displacement);
        }

        transform.position += displacement;
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(normalizedDirection, Vector3.up),
            Time.deltaTime * 12f);
    }

    protected virtual Vector3 GetProjectileSpawnPosition()
    {
        if (projectileSpawnPos != null)
        {
            return projectileSpawnPos.position;
        }

        return transform.position + transform.forward * 0.9f + Vector3.up * 1.15f;
    }

    protected virtual float GetStoppingDistance()
    {
        return Stats == null ? 0.9f : Stats.attackMoveStopDistance;
    }

    protected virtual float GetFollowPlayerStoppingDistance()
    {
        return Stats == null ? 1.2f : Mathf.Max(0.35f, Stats.followDistance * 0.6f);
    }

    protected virtual bool ShouldPauseMovementForAttack()
    {
        return !isHero && _attackStopTimer > 0f;
    }

    protected virtual void ApplyTeamColor()
    {
        Color tint = Team == null ? Color.white : Team.teamColor;
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.materials;
            bool hasToonMaterial = false;
            bool updatedAnyMaterial = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null)
                {
                    continue;
                }

                if (materials[i].shader != null && materials[i].shader.name == ToonShaderName)
                {
                    hasToonMaterial = true;
                }

                if (materials[i].shader != null &&
                    materials[i].shader.name == ToonShaderName &&
                    materials[i].HasProperty(BlueReplaceColorID))
                {
                    materials[i].SetColor(BlueReplaceColorID, tint);
                    updatedAnyMaterial = true;
                }
            }

            if (hasToonMaterial)
            {
                renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BlueReplaceColorID, tint);
                renderer.SetPropertyBlock(_propertyBlock);
            }

            if (updatedAnyMaterial)
            {
                renderer.materials = materials;
            }
        }
    }

    protected float GetDistanceToTargetSurface(Hitbox target)
    {
        if (target == null)
        {
            return float.MaxValue;
        }

        Vector3 origin = transform.position + Vector3.up * 0.9f;
        Collider targetCollider = target.GetComponent<Collider>();
        if (targetCollider == null)
        {
            return Vector3.Distance(transform.position, target.transform.position);
        }

        Vector3 closestPoint = targetCollider.ClosestPoint(origin);
        return Vector3.Distance(origin, closestPoint);
    }

    private void EnsureCollisionSetup()
    {
        _collisionCollider = GetComponent<CapsuleCollider>();
        if (_collisionCollider == null)
        {
            _collisionCollider = gameObject.AddComponent<CapsuleCollider>();
            _collisionCollider.radius = 0.4f;
            _collisionCollider.height = 1.8f;
            _collisionCollider.center = new Vector3(0f, 0.9f, 0f);
        }

        _collisionCollider.isTrigger = false;

        _rigidbody = GetComponent<Rigidbody>();
        if (_rigidbody == null)
        {
            _rigidbody = gameObject.AddComponent<Rigidbody>();
        }

        _rigidbody.isKinematic = true;
        _rigidbody.useGravity = false;
        _rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void CachePresentationComponents()
    {
        _cachedRenderers = GetComponentsInChildren<Renderer>(true);
        _cachedColliders = GetComponentsInChildren<Collider>(true);
        _cachedAnimators = GetComponentsInChildren<Animator>(true);
    }

    private void ResolveAnimators()
    {
        Animator[] animators = GetComponentsInChildren<Animator>(true);
        if (animators == null || animators.Length == 0)
        {
            return;
        }

        if (moveAnimator == null)
        {
            moveAnimator = FindAnimator(animators, "horse", "move") ?? animators[0];
        }

        if (attackAnimator == null)
        {
            attackAnimator = FindAnimator(animators, "cavalier", "rider", "attack");
            if (attackAnimator == null)
            {
                attackAnimator = animators.Length > 1 ? animators[1] : moveAnimator;
            }
        }

        if (attackAnimator == null)
        {
            attackAnimator = moveAnimator;
        }

        AttachAnimationRelay(moveAnimator);
        if (attackAnimator != moveAnimator)
        {
            AttachAnimationRelay(attackAnimator);
        }
    }

    private Animator FindAnimator(Animator[] animators, params string[] keywords)
    {
        for (int i = 0; i < animators.Length; i++)
        {
            Animator animator = animators[i];
            if (animator == null)
            {
                continue;
            }

            string animatorName = animator.gameObject.name.ToLowerInvariant();
            string controllerName = animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.name.ToLowerInvariant()
                : string.Empty;

            for (int keywordIndex = 0; keywordIndex < keywords.Length; keywordIndex++)
            {
                string keyword = keywords[keywordIndex];
                if (animatorName.Contains(keyword) || controllerName.Contains(keyword))
                {
                    return animator;
                }
            }
        }

        return null;
    }

    protected virtual void UpdateAnimators(Vector3 moveDirection)
    {
        bool isMoving = moveDirection.sqrMagnitude > 0.0001f;

        if (moveAnimator != null && !string.IsNullOrWhiteSpace(moveBoolName))
        {
            TrySetAnimatorBool(moveAnimator, moveBoolName, isMoving);
        }
    }

    private void TriggerAttackAnimation()
    {
        if (attackAnimator == null || string.IsNullOrWhiteSpace(attackTriggerName))
        {
            return;
        }

        TryResetAnimatorTrigger(attackAnimator, attackTriggerName);
        TrySetAnimatorTrigger(attackAnimator, attackTriggerName);
    }

    protected void TrySetAnimatorBool(Animator animator, string parameterName, bool value)
    {
        if (!HasAnimatorParameter(animator, parameterName, AnimatorControllerParameterType.Bool))
        {
            return;
        }

        animator.SetBool(parameterName, value);
    }

    private void TrySetAnimatorTrigger(Animator animator, string parameterName)
    {
        if (!HasAnimatorParameter(animator, parameterName, AnimatorControllerParameterType.Trigger))
        {
            return;
        }

        animator.SetTrigger(parameterName);
    }

    private void TryResetAnimatorTrigger(Animator animator, string parameterName)
    {
        if (!HasAnimatorParameter(animator, parameterName, AnimatorControllerParameterType.Trigger))
        {
            return;
        }

        animator.ResetTrigger(parameterName);
    }

    protected bool HasAnimatorParameter(Animator animator, string parameterName, AnimatorControllerParameterType parameterType)
    {
        if (animator == null || string.IsNullOrWhiteSpace(parameterName))
        {
            return false;
        }

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].type == parameterType && parameters[i].name == parameterName)
            {
                return true;
            }
        }

        return false;
    }

    private void AttachAnimationRelay(Animator animator)
    {
        if (animator == null)
        {
            return;
        }

        UnitAnimationRelay relay = animator.GetComponent<UnitAnimationRelay>();
        if (relay == null)
        {
            relay = animator.gameObject.AddComponent<UnitAnimationRelay>();
        }

        relay.Bind(this);
    }

    private Vector3 ResolveBlockedMovement(Vector3 desiredDisplacement)
    {
        Vector3 start = transform.position;
        Vector3 direction = desiredDisplacement.normalized;
        float distance = desiredDisplacement.magnitude;
        if (distance <= 0.0001f)
        {
            return Vector3.zero;
        }

        GetCapsuleWorldPoints(start, out Vector3 point1, out Vector3 point2, out float radius);
        if (Physics.CapsuleCast(point1, point2, radius, direction, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null && !hit.collider.transform.IsChildOf(transform))
            {
                if (ShouldIgnoreBlockingCollider(hit.collider))
                {
                    return desiredDisplacement;
                }

                Unit blockingUnit = hit.collider.GetComponentInParent<Unit>();
                if (blockingUnit != null && blockingUnit != this && blockingUnit.Team == Team)
                {
                    return desiredDisplacement;
                }

                float allowedDistance = Mathf.Max(0f, hit.distance - 0.02f);
                return direction * allowedDistance;
            }
        }

        return desiredDisplacement;
    }

    private bool ShouldIgnoreBlockingCollider(Collider collider)
    {
        if (collider == null)
        {
            return true;
        }

        if (IsGeneratedMapBlocker(collider.transform))
        {
            return false;
        }

        if (collider.bounds.max.y <= 0.35f)
        {
            return true;
        }

        if (collider.GetComponentInParent<BuildZone>() != null ||
            collider.GetComponentInParent<HarvestField>() != null ||
            IsNamedBuildZoneHierarchy(collider.transform))
        {
            return true;
        }

        return collider.GetComponentInParent<Tree>() == null &&
               collider.GetComponentInParent<Build>() == null &&
               collider.GetComponentInParent<Hitbox>() == null &&
               collider.GetComponentInParent<Unit>() == null;
    }

    private bool IsGeneratedMapBlocker(Transform transformToCheck)
    {
        Transform current = transformToCheck;
        while (current != null)
        {
            if (current.name == "Blockers" || current.name.StartsWith("Blocker_"))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private Vector3 GetPathDirectionTo(Vector3 worldPosition, float stoppingDistance)
    {
        Vector3 delta = GetFlatDelta(worldPosition);
        if (delta.sqrMagnitude <= stoppingDistance * stoppingDistance)
        {
            ClearPath();
            return Vector3.zero;
        }

        if (ShouldRefreshPath(worldPosition))
        {
            RefreshPath(worldPosition);
        }

        if (_pathWaypoints.Count == 0)
        {
            if (IsDirectPathBlocked(worldPosition, stoppingDistance))
            {
                return Vector3.zero;
            }

            return delta.normalized;
        }

        while (_currentWaypointIndex < _pathWaypoints.Count)
        {
            Vector3 waypointDelta = GetFlatDelta(_pathWaypoints[_currentWaypointIndex]);
            if (waypointDelta.sqrMagnitude > 0.18f * 0.18f)
            {
                return waypointDelta.normalized;
            }

            _currentWaypointIndex++;
        }

        ClearPath();
        return delta.normalized;
    }

    private bool ShouldRefreshPath(Vector3 worldPosition)
    {
        if (_pathWaypoints.Count == 0)
        {
            return true;
        }

        if (Time.time >= _nextPathRefreshTime)
        {
            return true;
        }

        Vector3 targetDelta = new Vector3(worldPosition.x - _currentPathTarget.x, 0f, worldPosition.z - _currentPathTarget.z);
        return targetDelta.sqrMagnitude >= 1.25f * 1.25f;
    }

    private void RefreshPath(Vector3 worldPosition)
    {
        _currentPathTarget = worldPosition;
        _nextPathRefreshTime = Time.time + 0.35f;
        _currentWaypointIndex = 0;
        _pathWaypoints.Clear();

        if (RTSGridPathfinder.Instance == null)
        {
            return;
        }

        if (RTSGridPathfinder.Instance.TryFindPath(transform.position, worldPosition, _collisionCollider, out List<Vector3> path))
        {
            _pathWaypoints.AddRange(path);
        }
    }

    private bool IsDirectPathBlocked(Vector3 worldPosition, float stoppingDistance)
    {
        if (_collisionCollider == null)
        {
            return false;
        }

        Vector3 flatDelta = GetFlatDelta(worldPosition);
        float distance = Mathf.Max(0f, flatDelta.magnitude - stoppingDistance);
        if (distance <= 0.05f)
        {
            return false;
        }

        Vector3 direction = flatDelta.normalized;
        GetCapsuleWorldPoints(transform.position, out Vector3 point1, out Vector3 point2, out float radius);
        if (!Physics.CapsuleCast(point1, point2, radius, direction, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        return hit.collider != null &&
               !hit.collider.transform.IsChildOf(transform) &&
               !ShouldIgnoreBlockingCollider(hit.collider);
    }

    private void ClearPath()
    {
        _pathWaypoints.Clear();
        _currentWaypointIndex = 0;
        _nextPathRefreshTime = 0f;
    }

    protected void ClearMovementPath()
    {
        ClearPath();
    }

    private void TickHeroRegeneration()
    {
        if (!isHero || Hitbox == null || Stats == null || Stats.regen <= 0 || Stats.regenCD <= 0f)
        {
            return;
        }

        if (Hitbox.CurrentHp >= Stats.health)
        {
            _regenTickTimer = 0f;
            return;
        }

        const float outOfCombatDelay = 5f;
        if (_timeSinceLastReceivedDamage < outOfCombatDelay || _timeSinceLastAttackPerformed < outOfCombatDelay)
        {
            _regenTickTimer = 0f;
            return;
        }

        _regenTickTimer += Time.deltaTime;
        if (_regenTickTimer < Stats.regenCD)
        {
            return;
        }

        _regenTickTimer = 0f;
        Hitbox.Heal(Stats.regen);
    }

    public virtual void RespawnAt(Vector3 worldPosition, Quaternion worldRotation)
    {
        transform.SetPositionAndRotation(worldPosition, worldRotation);
        _controllerMoveInput = Vector3.zero;
        _attackCooldownTimer = 0f;
        _attackStopTimer = 0f;
        _pendingAttackTarget = null;
        _pendingProjectilePrefab = null;
        _hasPendingAttackImpact = false;
        _pendingAttackVersion++;
        _timeSinceLastAttackPerformed = 0f;
        _timeSinceLastReceivedDamage = 0f;
        _regenTickTimer = 0f;
        ClearMovementPath();
        SetPresentationVisible(true);

        if (Hitbox != null)
        {
            Hitbox.Revive();
        }

        if (Team != null)
        {
            Team.RegisterUnit(this);
        }

        OnUnitRespawned?.Invoke(this);
    }

    private void SetPresentationVisible(bool isVisible)
    {
        if (_cachedRenderers != null)
        {
            for (int i = 0; i < _cachedRenderers.Length; i++)
            {
                if (_cachedRenderers[i] != null)
                {
                    _cachedRenderers[i].enabled = isVisible;
                }
            }
        }

        if (_cachedColliders != null)
        {
            for (int i = 0; i < _cachedColliders.Length; i++)
            {
                if (_cachedColliders[i] != null)
                {
                    _cachedColliders[i].enabled = isVisible;
                }
            }
        }

        if (_cachedAnimators != null)
        {
            for (int i = 0; i < _cachedAnimators.Length; i++)
            {
                if (_cachedAnimators[i] != null)
                {
                    _cachedAnimators[i].enabled = isVisible;
                }
            }
        }

        if (_rigidbody != null)
        {
            _rigidbody.detectCollisions = isVisible;
        }
    }

    private Vector3 GetAvoidanceDirection()
    {
        if (_collisionCollider == null)
        {
            return Vector3.zero;
        }

        GetCapsuleWorldPoints(transform.position, out Vector3 point1, out Vector3 point2, out float radius);
        Collider[] overlaps = Physics.OverlapCapsule(point1, point2, radius * 1.8f, ~0, QueryTriggerInteraction.Ignore);
        Vector3 avoidance = Vector3.zero;

        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider other = overlaps[i];
            if (other == null || other == _collisionCollider || other.transform.IsChildOf(transform))
            {
                continue;
            }

            if (ShouldIgnoreBlockingCollider(other))
            {
                continue;
            }

            Unit otherUnit = other.GetComponentInParent<Unit>();
            if (otherUnit != null && otherUnit != this && otherUnit.Team == Team)
            {
                continue;
            }

            Vector3 closestPoint = other.ClosestPoint(transform.position);
            Vector3 away = transform.position - closestPoint;
            away.y = 0f;

            float sqrDistance = away.sqrMagnitude;
            if (sqrDistance <= 0.0001f)
            {
                Vector3 fallback = transform.position - other.bounds.center;
                fallback.y = 0f;
                away = fallback;
                sqrDistance = away.sqrMagnitude;
            }

            if (sqrDistance <= 0.0001f)
            {
                continue;
            }

            float weight = otherUnit != null ? 0.55f : 1f;
            avoidance += away.normalized * (weight / Mathf.Max(0.15f, Mathf.Sqrt(sqrDistance)));
        }

        return Vector3.ClampMagnitude(avoidance, 1f);
    }

    private void GetCapsuleWorldPoints(Vector3 basePosition, out Vector3 point1, out Vector3 point2, out float radius)
    {
        radius = _collisionCollider.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
        float height = Mathf.Max(_collisionCollider.height * transform.lossyScale.y, radius * 2f);
        Vector3 center = basePosition + _collisionCollider.center;
        float halfSegment = (height * 0.5f) - radius;
        point1 = center + Vector3.up * halfSegment;
        point2 = center - Vector3.up * halfSegment;
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

    public virtual string GetInfoText()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine(unitName);

        if (Stats != null)
        {
            if (!string.IsNullOrWhiteSpace(Stats.description))
            {
                builder.AppendLine(Stats.description);
            }

            builder.AppendLine($"HP: {Stats.health}");
            builder.AppendLine($"Damage: {Stats.damages}");
            builder.AppendLine($"MoveSpeed: {Stats.speed}");
            builder.AppendLine($"Armor: {Stats.armor}");
            builder.AppendLine($"Ranged Armor: {Stats.distanceArmor}");
            builder.AppendLine($"Armor Type: {Stats.armorType}");
        }

        return builder.ToString().Trim();
    }

    protected virtual void HandleHitboxDeath(Hitbox hitbox)
    {
        ClearMovementPath();
        ClearCombatTarget();
        _controllerMoveInput = Vector3.zero;
        _hasPendingAttackImpact = false;
        OnUnitDied?.Invoke(this);

        if (isHero)
        {
            SetPresentationVisible(false);
            Team?.HandleHeroUnitDeath(this);
            return;
        }

        if (Team != null)
        {
            Team.UnregisterUnit(this);
        }
    }

    protected bool HasValidCombatTarget()
    {
        return _currentCombatTarget != null &&
               !_currentCombatTarget.IsDead &&
               _currentCombatTarget.teamID != Hitbox.teamID;
    }

    protected void CancelCombatIntent()
    {
        ClearCombatTarget();
        _attackStopTimer = 0f;
        _hasPendingAttackImpact = false;
        _pendingAttackTarget = null;
        _pendingProjectilePrefab = null;
        _pendingAttackVersion++;
    }

    protected void ClearCombatTarget()
    {
        _currentCombatTarget = null;
        _combatElapsedWithoutHit = 0f;
    }

    private Vector3 GetFollowFormationAnchor(Unit hero)
    {
        if (hero == null || Team == null)
        {
            return transform.position;
        }

        return Team.GetFollowFormationPoint(this, hero.transform.position);
    }

    private Vector3 GetRetreatMoveDirection()
    {
        switch (_currentUnitState)
        {
            case UnitState.followPlayer:
                Unit hero = Team != null ? Team.GetHeroUnit() : null;
                if (hero != null)
                {
                    return GetDirectionTo(GetFollowFormationAnchor(hero), GetFollowPlayerStoppingDistance());
                }

                return GetDirectionTo(GetDefendAnchor());
            case UnitState.defendBase:
                return GetDirectionTo(GetDefendAnchor());
            default:
                return Vector3.zero;
        }
    }

    private bool CanAcquireCombatTargetForCurrentState()
    {
        if (Team == null || Stats == null)
        {
            return false;
        }

        if (isHero)
        {
            return true;
        }

        if (_currentUnitState == UnitState.followPlayer)
        {
            Unit hero = Team.GetHeroUnit();
            if (hero == null)
            {
                return false;
            }

            float leashDistance = Mathf.Max(GetFollowPlayerStoppingDistance() + 1f, Stats.followCombatLeashDistance);
            return GetFlatDelta(hero.transform.position).sqrMagnitude <= leashDistance * leashDistance;
        }

        if (_currentUnitState == UnitState.defendBase)
        {
            float leashDistance = Mathf.Max(Stats.defendAnchorDistance + 1.5f, Stats.defendLeashDistance);
            return GetFlatDelta(Team.BasePosition).sqrMagnitude <= leashDistance * leashDistance;
        }

        return true;
    }

    private bool IsFollowCombatTargetAllowed(Hitbox target)
    {
        if (target == null || Team == null || _currentUnitState != UnitState.followPlayer)
        {
            return false;
        }

        Unit hero = Team.GetHeroUnit();
        UnitStats heroStats = hero != null ? hero.StatsAsset : null;
        if (hero == null || heroStats == null)
        {
            return false;
        }

        float heroAggroRange = Mathf.Max(0.1f, heroStats.aggroRange);
        Vector3 targetDelta = hero.GetFlatDelta(target.transform.position);
        return targetDelta.sqrMagnitude <= heroAggroRange * heroAggroRange;
    }

    private bool ShouldRetreatForCurrentState()
    {
        if (isHero || Team == null || Stats == null)
        {
            return false;
        }

        if (_currentUnitState == UnitState.followPlayer)
        {
            Unit hero = Team.GetHeroUnit();
            if (hero == null)
            {
                return true;
            }

            float leashDistance = Mathf.Max(GetFollowPlayerStoppingDistance() + 1.5f, Stats.followCombatLeashDistance);
            return GetFlatDelta(hero.transform.position).sqrMagnitude > leashDistance * leashDistance;
        }

        if (_currentUnitState == UnitState.defendBase)
        {
            float leashDistance = Mathf.Max(Stats.defendAnchorDistance + 2f, Stats.defendLeashDistance);
            return GetFlatDelta(Team.BasePosition).sqrMagnitude > leashDistance * leashDistance;
        }

        return false;
    }

    protected virtual void HandleHitboxDamaged(Hitbox.DamageContext damageContext)
    {
        _timeSinceLastReceivedDamage = 0f;

        Hitbox attacker = damageContext.sourceHitbox;
        if (attacker == null || attacker == Hitbox || attacker.IsDead || attacker.teamID == Hitbox.teamID)
        {
            return;
        }

        if (_currentUnitState == UnitState.followPlayer && !IsFollowCombatTargetAllowed(attacker))
        {
            return;
        }

        if (!HasValidCombatTarget())
        {
            _currentCombatTarget = attacker;
            return;
        }

        if (_currentCombatTarget.OwnerBuild != null && attacker.OwnerUnit != null)
        {
            _currentCombatTarget = attacker;
            _combatElapsedWithoutHit = 0f;
            return;
        }

        if (_currentCombatTarget.OwnerUnit != null && attacker.OwnerUnit != null)
        {
            if (_combatElapsedWithoutHit >= Mathf.Max(0.5f, Stats == null ? 3f : Stats.retargetTimeout))
            {
                _currentCombatTarget = attacker;
                _combatElapsedWithoutHit = 0f;
            }

            return;
        }

        _currentCombatTarget = attacker;
        _combatElapsedWithoutHit = 0f;
    }
}

public class WolfUnit : Unit
{
    private const int NeutralTeamId = 100;

    [Header("Wolf")]
    [SerializeField] private int goldReward = 10;
    [SerializeField] private float fallbackAggroRange = 8f;
    [SerializeField] private float roamRadius = 3f;
    [SerializeField] private Vector2 roamPauseDurationRange = new Vector2(0.7f, 2f);
    [SerializeField] private Vector2 roamMoveDurationRange = new Vector2(1f, 2.4f);

    private Vector3 _spawnPosition;
    private Quaternion _spawnRotation;
    private TeamManager _lastAttackerTeam;
    private Vector3 _roamTarget;
    private float _roamPauseTimer;
    private float _roamMoveTimer;
    private bool _isRoamPaused = true;
    private bool _hasRetaliationTarget;

    protected override void Start()
    {
        base.Start();
        InitializeWolf();
    }

    public void InitializeAtSpawn(Vector3 spawnPosition, Quaternion spawnRotation)
    {
        _spawnPosition = spawnPosition;
        _spawnRotation = spawnRotation;
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        InitializeWolf();
    }

    private void InitializeWolf()
    {
        _spawnPosition = _spawnPosition == Vector3.zero ? transform.position : _spawnPosition;
        _spawnRotation = _spawnRotation == Quaternion.identity ? transform.rotation : _spawnRotation;
        _hasRetaliationTarget = false;

        if (Hitbox != null)
        {
            Hitbox.teamID = NeutralTeamId;
            Hitbox.AssignTeam(null);
        }

        ResetRoamState(true);
    }

    protected override Vector3 GetAutoMoveDirection()
    {
        if (ShouldReleaseCurrentTarget())
        {
            CancelCombatIntent();
        }

        if (HasValidCombatTarget())
        {
            return GetDirectionTo(_currentCombatTarget.transform.position);
        }

        return GetRoamMoveDirection();
    }

    protected override void TickCombat()
    {
        if (Stats == null || Hitbox == null || Hitbox.IsDead)
        {
            return;
        }

        if (ShouldReleaseCurrentTarget())
        {
            CancelCombatIntent();
        }

        if (!HasValidCombatTarget())
        {
            _currentCombatTarget = FindNearestEnemyTarget(GetWolfDetectionRange());
        }

        Hitbox target = _currentCombatTarget;
        if (target == null)
        {
            return;
        }

        float distanceToSurface = GetDistanceToTargetSurface(target);
        if (distanceToSurface > Stats.range)
        {
            return;
        }

        if (_attackCooldownTimer > 0f)
        {
            return;
        }

        _attackCooldownTimer = Mathf.Max(0.1f, Stats.attackCooldown);
        PerformAttack(target);
    }

    public override Hitbox FindNearestEnemyTarget(float maxRange)
    {
        if (Hitbox == null)
        {
            return null;
        }

        Hitbox[] hitboxes = FindObjectsOfType<Hitbox>(true);
        float bestDistance = maxRange * maxRange;
        Hitbox bestTarget = null;

        for (int i = 0; i < hitboxes.Length; i++)
        {
            Hitbox candidate = hitboxes[i];
            if (candidate == null || candidate == Hitbox || candidate.IsDead || candidate.teamID == NeutralTeamId)
            {
                continue;
            }

            Vector3 fromWolf = GetFlatDelta(candidate.transform.position);
            float sqrDistance = fromWolf.sqrMagnitude;
            if (sqrDistance > bestDistance)
            {
                continue;
            }

            Vector3 fromSpawn = candidate.transform.position - _spawnPosition;
            fromSpawn.y = 0f;
            float aggroRange = GetWolfAggroRange();
            if (fromSpawn.sqrMagnitude > aggroRange * aggroRange)
            {
                continue;
            }

            bestDistance = sqrDistance;
            bestTarget = candidate;
        }

        return bestTarget;
    }

    protected override void HandleHitboxDamaged(Hitbox.DamageContext damageContext)
    {
        base.HandleHitboxDamaged(damageContext);

        Hitbox attackerHitbox = damageContext.sourceHitbox;
        if (attackerHitbox != null && attackerHitbox != Hitbox && !attackerHitbox.IsDead && attackerHitbox.teamID != NeutralTeamId)
        {
            _currentCombatTarget = attackerHitbox;
            _hasRetaliationTarget = true;
            _combatElapsedWithoutHit = 0f;
        }

        TeamManager attackerTeam = attackerHitbox != null ? attackerHitbox.OwnerTeam : null;
        if (attackerTeam != null)
        {
            _lastAttackerTeam = attackerTeam;
        }

        _isRoamPaused = true;
        _roamPauseTimer = 0f;
        _roamMoveTimer = 0f;
    }

    protected override void HandleHitboxDeath(Hitbox hitbox)
    {
        if (_lastAttackerTeam != null && goldReward > 0)
        {
            _lastAttackerTeam.AddGold(goldReward);
            SpawnCoinEffect.SpawnAbove(transform);
        }

        base.HandleHitboxDeath(hitbox);
    }

    private bool ShouldReleaseCurrentTarget()
    {
        if (!HasValidCombatTarget())
        {
            _hasRetaliationTarget = false;
            return false;
        }

        if (_hasRetaliationTarget)
        {
            return false;
        }

        Vector3 delta = _currentCombatTarget.transform.position - _spawnPosition;
        delta.y = 0f;
        float aggroRange = GetWolfAggroRange();
        return delta.sqrMagnitude > aggroRange * aggroRange;
    }

    private float GetWolfAggroRange()
    {
        return Mathf.Max(0.5f, Stats != null ? Stats.aggroRange : fallbackAggroRange);
    }

    private float GetWolfDetectionRange()
    {
        if (Stats == null)
        {
            return fallbackAggroRange;
        }

        return Mathf.Max(Stats.aggroRange, Stats.detectionDefenseRange, fallbackAggroRange);
    }

    private Vector3 GetRoamMoveDirection()
    {
        if (_isRoamPaused)
        {
            _roamPauseTimer -= Time.deltaTime;
            if (_roamPauseTimer > 0f)
            {
                return Vector3.zero;
            }

            _isRoamPaused = false;
            _roamMoveTimer = RandomRange(roamMoveDurationRange);
            _roamTarget = ChooseRoamTarget();
        }

        _roamMoveTimer -= Time.deltaTime;
        if (_roamMoveTimer <= 0f || GetFlatDelta(_roamTarget).sqrMagnitude <= 0.45f * 0.45f)
        {
            ResetRoamState(false);
            return Vector3.zero;
        }

        return GetDirectionTo(_roamTarget, 0.2f);
    }

    private void ResetRoamState(bool randomFacing)
    {
        _isRoamPaused = true;
        _roamPauseTimer = RandomRange(roamPauseDurationRange);
        _roamMoveTimer = 0f;
        _roamTarget = _spawnPosition;

        if (!randomFacing)
        {
            return;
        }

        Vector3 forward = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
        if (forward.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }
    }

    private Vector3 ChooseRoamTarget()
    {
        float radius = UnityEngine.Random.Range(0.35f, Mathf.Max(0.35f, roamRadius));
        float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
        return _spawnPosition + offset;
    }

    private float RandomRange(Vector2 range)
    {
        float min = Mathf.Min(range.x, range.y);
        float max = Mathf.Max(range.x, range.y);
        return UnityEngine.Random.Range(min, max);
    }
}
