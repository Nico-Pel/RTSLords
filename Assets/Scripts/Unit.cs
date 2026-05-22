using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

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
        other
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
    private float _attackCooldownTimer;
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

    public TeamManager Team { get; private set; }
    public Hitbox Hitbox { get; private set; }
    public UnitState CurrentUnitState => _currentUnitState;
    public bool IsHero => isHero;
    public UnitType PrimaryUnitType => Stats != null ? Stats.PrimaryUnitType : UnitType.other;
    public bool IsDead => Hitbox != null && Hitbox.IsDead;
    public Collider CollisionCollider => _collisionCollider;
    protected UnitStats Stats => Hitbox != null ? Hitbox.unitStats : null;

    protected virtual void Awake()
    {
        _propertyBlock = new MaterialPropertyBlock();
        Hitbox = GetComponent<Hitbox>();
        if (Hitbox != null)
        {
            Hitbox.OnDeath += HandleHitboxDeath;
        }

        EnsureCollisionSetup();
        ResolveAnimators();
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

        Vector3 moveDirection = GetMoveDirection();
        TickCombat();

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
        _currentUnitState = state;
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

        Hitbox immediateTarget = FindNearestEnemyTarget(Stats == null ? 6f : Stats.aggroRange);
        if (immediateTarget != null)
        {
            return GetDirectionTo(immediateTarget.transform.position);
        }

        switch (_currentUnitState)
        {
            case UnitState.defendBase:
                return GetDirectionTo(GetDefendAnchor());
            case UnitState.followPlayer:
                Unit hero = Team.GetHeroUnit();
                return hero == null ? GetDirectionTo(GetDefendAnchor()) : GetDirectionTo(hero.transform.position);
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

        float detectionRange = _currentUnitState == UnitState.raidEnemies ? Stats.detectionRaidRange : Stats.detectionDefenseRange;
        if (isHero)
        {
            detectionRange = Mathf.Max(detectionRange, Stats.aggroRange);
        }

        Hitbox target = FindNearestEnemyTarget(detectionRange);
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

            projectile.Setup(target, damages, damageType, projectileSpeed);
            return;
        }

        target.TakeDamage(damages, damageType);
    }

    public Hitbox FindNearestEnemyTarget(float maxRange)
    {
        if (Team == null || Hitbox == null)
        {
            return null;
        }

        float bestDistance = maxRange * maxRange;
        Hitbox bestTarget = null;

        foreach (Hitbox target in Team.EnumerateEnemyTargets())
        {
            if (target == null || target.IsDead || target.teamID == Hitbox.teamID)
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

        if (collider.bounds.max.y <= 0.35f)
        {
            return true;
        }

        if (collider.GetComponentInParent<BuildZone>() != null || collider.GetComponentInParent<HarvestField>() != null)
        {
            return true;
        }

        return collider.GetComponentInParent<Tree>() == null &&
               collider.GetComponentInParent<Build>() == null &&
               collider.GetComponentInParent<Hitbox>() == null &&
               collider.GetComponentInParent<Unit>() == null;
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

            float weight = other.GetComponentInParent<Unit>() != null ? 0.55f : 1f;
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

    private void HandleHitboxDeath(Hitbox hitbox)
    {
        if (Team != null)
        {
            Team.UnregisterUnit(this);
        }
    }
}
