using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public Build[] possibleBuildsPrefabs;

    [Header("Movement")]
    public float dragSensitivity = 3f;
    public bool allowKeyboardInEditor = true;

    [Header("Camera")]
    public float cameraFollowLerp = 10f;
    public float cameraReturnToCityLerp = 5f;
    public float cameraIdleDezoomDelay = 2f;
    public float cameraIdleDezoomDistance = 2f;
    public float cameraIdleDezoomLerp = 3f;
    public float cameraIdleMovementThreshold = 0.05f;
    public float cameraNormalFov = 20f;
    public float cameraIdleDezoomFov = 25f;
    public float cameraPostAttackNoDezoomDuration = 2f;

    private Camera _mainCamera;
    private Vector3 _cameraOffset;
    private Quaternion _cameraRotation;
    private Vector2 _dragStartPosition;
    private bool _isDragging;
    private bool _isControlLocked;
    private float _idleTimeWithoutMovement;
    private Vector3 _lastControlledUnitPosition;
    private const float CameraFollowZOffset = -28f;

    public TeamManager Team { get; private set; }
    public Unit ControlledUnit { get; private set; }

    private void Awake()
    {
        gameObject.tag = "Player";
        ControlledUnit = GetComponent<Unit>();
        if (ControlledUnit != null)
        {
            ControlledUnit.isHero = true;
            ControlledUnit.OnUnitDied += HandleControlledUnitDeath;
            ControlledUnit.OnUnitRespawned += HandleControlledUnitRespawned;
            ConfigureHeroPersistence();
        }
    }

    private void OnDestroy()
    {
        if (ControlledUnit != null)
        {
            ControlledUnit.OnUnitDied -= HandleControlledUnitDeath;
            ControlledUnit.OnUnitRespawned -= HandleControlledUnitRespawned;
        }
    }

    private void Start()
    {
        ConfigureHeroPersistence();
        CacheCameraReferences();
        CacheControlledUnitPosition();
        ForceMainCameraToControlledUnit();
    }

    public void BindTeam(TeamManager team)
    {
        Team = team;
        if (ControlledUnit != null)
        {
            ControlledUnit.AssignTeam(team);
        }

        CacheControlledUnitPosition();
        ForceMainCameraToControlledUnit();
    }

    public void SnapCameraToControlledUnit()
    {
        ForceMainCameraToControlledUnit();
    }

    private void Update()
    {
        if (ControlledUnit == null)
        {
            return;
        }

        if (_isControlLocked || ControlledUnit.IsDead)
        {
            ControlledUnit.SetControllerMoveInput(Vector3.zero);
            return;
        }

        Vector3 moveInput = ReadMovementInput();
        if (moveInput.sqrMagnitude > 0.0001f)
        {
            _idleTimeWithoutMovement = 0f;
        }

        ControlledUnit.SetControllerMoveInput(moveInput);
    }

    private void LateUpdate()
    {
        if (_mainCamera == null)
        {
            CacheCameraReferences();
        }

        if (_mainCamera == null)
        {
            return;
        }

        UpdateIdleCameraTimer();

        Vector3 desiredPosition = _mainCamera.transform.position;
        float lerpSpeed = cameraFollowLerp;
        if (_isControlLocked && Team != null && Team.city != null)
        {
            desiredPosition = Team.city.transform.position + _cameraOffset;
            desiredPosition.x = Team.city.transform.position.x;
            desiredPosition.z = Team.city.transform.position.z + CameraFollowZOffset;
            lerpSpeed = cameraReturnToCityLerp;
        }
        else if (ControlledUnit != null)
        {
            desiredPosition = GetDesiredCameraPositionForControlledUnit();
            if (_idleTimeWithoutMovement >= cameraIdleDezoomDelay)
            {
                lerpSpeed = Mathf.Min(lerpSpeed, cameraIdleDezoomLerp);
            }
        }

        _mainCamera.transform.position = Vector3.Lerp(_mainCamera.transform.position, desiredPosition, Time.deltaTime * lerpSpeed);
        _mainCamera.transform.rotation = Quaternion.Lerp(_mainCamera.transform.rotation, _cameraRotation, Time.deltaTime * cameraFollowLerp);
        _mainCamera.fieldOfView = Mathf.Lerp(_mainCamera.fieldOfView, GetDesiredCameraFov(), Time.deltaTime * lerpSpeed);
    }

    private void HandleControlledUnitDeath(Unit unit)
    {
        _isControlLocked = true;
        _isDragging = false;
        _idleTimeWithoutMovement = 0f;
    }

    private void HandleControlledUnitRespawned(Unit unit)
    {
        _isControlLocked = false;
        _isDragging = false;
        _idleTimeWithoutMovement = 0f;
        CacheControlledUnitPosition();
        ForceMainCameraToControlledUnit();
    }

    private void ConfigureHeroPersistence()
    {
        if (ControlledUnit != null && ControlledUnit.Hitbox != null)
        {
            ControlledUnit.Hitbox.destroyOnDeath = false;
        }
    }

    private void CacheCameraReferences()
    {
        _mainCamera = Camera.main;
        if (_mainCamera != null && ControlledUnit != null)
        {
            _cameraOffset = _mainCamera.transform.position - ControlledUnit.transform.position;
            _cameraRotation = _mainCamera.transform.rotation;
        }
    }

    public void ForceMainCameraToControlledUnit()
    {
        if (ControlledUnit == null)
        {
            return;
        }

        CacheCameraReferences();
        if (_mainCamera == null)
        {
            return;
        }

        Vector3 snappedPosition = GetDesiredCameraPositionForControlledUnit();
        _mainCamera.transform.position = snappedPosition;
        _mainCamera.transform.rotation = _cameraRotation;
        _mainCamera.fieldOfView = GetDesiredCameraFov();
    }

    private Vector3 GetDesiredCameraPositionForControlledUnit()
    {
        if (ControlledUnit == null)
        {
            return _mainCamera != null ? _mainCamera.transform.position : Vector3.zero;
        }

        Vector3 desiredPosition = ControlledUnit.transform.position + _cameraOffset;
        desiredPosition.x = ControlledUnit.transform.position.x;
        desiredPosition.z = ControlledUnit.transform.position.z + CameraFollowZOffset;

        float dezoom01 = cameraIdleDezoomDelay <= 0f || _idleTimeWithoutMovement >= cameraIdleDezoomDelay
            ? 1f
            : 0f;

        if (IsHeroUsingPlayerActivator())
        {
            dezoom01 = 0f;
        }

        if (IsHeroActivelyAttacking())
        {
            dezoom01 = 0f;
        }

        if (cameraIdleDezoomDistance > 0f && dezoom01 > 0f)
        {
            Vector3 heroToCamera = desiredPosition - ControlledUnit.transform.position;
            if (heroToCamera.sqrMagnitude > 0.0001f)
            {
                desiredPosition += heroToCamera.normalized * (cameraIdleDezoomDistance * dezoom01);
            }
        }

        return desiredPosition;
    }

    private float GetDesiredCameraFov()
    {
        bool shouldUseDezoomFov = !_isControlLocked &&
                                  ControlledUnit != null &&
                                  !ControlledUnit.IsDead &&
                                  !IsHeroUsingPlayerActivator() &&
                                  !IsHeroActivelyAttacking() &&
                                  _idleTimeWithoutMovement >= cameraIdleDezoomDelay;

        return shouldUseDezoomFov ? cameraIdleDezoomFov : cameraNormalFov;
    }

    private bool IsHeroUsingPlayerActivator()
    {
        return ControlledUnit != null && PlayerActivator.GetActiveActivatorFor(ControlledUnit) != null;
    }

    private bool IsHeroActivelyAttacking()
    {
        return ControlledUnit != null &&
               (ControlledUnit.IsInAttackSequence || ControlledUnit.HasAttackedRecently(cameraPostAttackNoDezoomDuration));
    }

    private void UpdateIdleCameraTimer()
    {
        if (ControlledUnit == null || ControlledUnit.IsDead || _isControlLocked)
        {
            _idleTimeWithoutMovement = 0f;
            CacheControlledUnitPosition();
            return;
        }

        Vector3 currentPosition = ControlledUnit.transform.position;
        Vector3 delta = currentPosition - _lastControlledUnitPosition;
        delta.y = 0f;

        float movementThreshold = Mathf.Max(0.001f, cameraIdleMovementThreshold);
        if (delta.sqrMagnitude <= movementThreshold * movementThreshold)
        {
            _idleTimeWithoutMovement += Time.deltaTime;
        }
        else
        {
            _idleTimeWithoutMovement = 0f;
        }

        _lastControlledUnitPosition = currentPosition;
    }

    private void CacheControlledUnitPosition()
    {
        if (ControlledUnit != null)
        {
            _lastControlledUnitPosition = ControlledUnit.transform.position;
        }
    }

    private Vector3 ReadMovementInput()
    {
        Vector2 screenInput = Vector2.zero;

        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
            {
                _dragStartPosition = touch.position;
                _isDragging = true;
            }
            else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                _isDragging = false;
            }

            if (_isDragging)
            {
                screenInput = Vector2.ClampMagnitude((touch.position - _dragStartPosition) / (Screen.height * 0.2f), 1f);
            }
        }
        else if (Input.GetMouseButtonDown(0))
        {
            _dragStartPosition = Input.mousePosition;
            _isDragging = true;
        }
        else if (Input.GetMouseButtonUp(0))
        {
            _isDragging = false;
        }

        if (_isDragging)
        {
            screenInput = Vector2.ClampMagnitude(((Vector2)Input.mousePosition - _dragStartPosition) / (Screen.height * 0.2f), 1f);
        }

        if (allowKeyboardInEditor)
        {
            Vector2 keyboardInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (keyboardInput.sqrMagnitude > screenInput.sqrMagnitude)
            {
                screenInput = Vector2.ClampMagnitude(keyboardInput, 1f);
            }
        }

        return new Vector3(screenInput.x, 0f, screenInput.y) * dragSensitivity;
    }
}
