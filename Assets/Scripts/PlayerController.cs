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

    private Camera _mainCamera;
    private Vector3 _cameraOffset;
    private Quaternion _cameraRotation;
    private Vector2 _dragStartPosition;
    private bool _isDragging;
    private bool _isControlLocked;

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
        _mainCamera = Camera.main;
        if (_mainCamera != null && ControlledUnit != null)
        {
            _cameraOffset = _mainCamera.transform.position - ControlledUnit.transform.position;
            _cameraRotation = _mainCamera.transform.rotation;
        }
    }

    public void BindTeam(TeamManager team)
    {
        Team = team;
        if (ControlledUnit != null)
        {
            ControlledUnit.AssignTeam(team);
        }
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
        ControlledUnit.SetControllerMoveInput(moveInput);
    }

    private void LateUpdate()
    {
        if (_mainCamera == null)
        {
            return;
        }

        Vector3 desiredPosition = _mainCamera.transform.position;
        float lerpSpeed = cameraFollowLerp;
        if (_isControlLocked && Team != null && Team.city != null)
        {
            desiredPosition = Team.city.transform.position + _cameraOffset;
            lerpSpeed = cameraReturnToCityLerp;
        }
        else if (ControlledUnit != null)
        {
            desiredPosition = ControlledUnit.transform.position + _cameraOffset;
        }

        _mainCamera.transform.position = Vector3.Lerp(_mainCamera.transform.position, desiredPosition, Time.deltaTime * lerpSpeed);
        _mainCamera.transform.rotation = Quaternion.Lerp(_mainCamera.transform.rotation, _cameraRotation, Time.deltaTime * cameraFollowLerp);
    }

    private void HandleControlledUnitDeath(Unit unit)
    {
        _isControlLocked = true;
        _isDragging = false;
    }

    private void HandleControlledUnitRespawned(Unit unit)
    {
        _isControlLocked = false;
        _isDragging = false;
    }

    private void ConfigureHeroPersistence()
    {
        if (ControlledUnit != null && ControlledUnit.Hitbox != null)
        {
            ControlledUnit.Hitbox.destroyOnDeath = false;
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
