using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UnitJoystick : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private RectTransform joystickHandle;
    [SerializeField] private Image unitImage;
    [SerializeField] private float maxVerticalOffset = 42f;
    [SerializeField] private float triggerThreshold = 18f;

    private RectTransform _rootRectTransform;
    private TeamManager _team;
    private UnitStats _unitStats;
    private Unit.UnitState _currentState;
    private Vector2 _restPosition;

    private void Awake()
    {
        _rootRectTransform = GetComponent<RectTransform>();
        if (joystickHandle == null)
        {
            Transform handleTransform = transform.Find("iJoystick");
            if (handleTransform != null)
            {
                joystickHandle = handleTransform as RectTransform;
            }
        }

        if (unitImage == null && joystickHandle != null)
        {
            Transform imageTransform = joystickHandle.Find("iUnit");
            if (imageTransform != null)
            {
                unitImage = imageTransform.GetComponent<Image>();
            }
        }

        if (joystickHandle != null)
        {
            _restPosition = joystickHandle.anchoredPosition;
        }
    }

    public void Setup(TeamManager team, UnitStats unitStats, Sprite sprite, Unit.UnitState state)
    {
        _team = team;
        _unitStats = unitStats;
        _currentState = state;

        if (unitImage != null)
        {
            unitImage.sprite = sprite;
            unitImage.enabled = sprite != null;
        }

        if (joystickHandle != null)
        {
            joystickHandle.anchoredPosition = _restPosition;
        }

        gameObject.SetActive(true);
    }

    public void SetState(Unit.UnitState state)
    {
        _currentState = state;
        ResetHandle();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_rootRectTransform == null || joystickHandle == null)
        {
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRectTransform, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
        {
            return;
        }

        float verticalOffset = Mathf.Clamp(localPoint.y, -maxVerticalOffset, maxVerticalOffset);
        joystickHandle.anchoredPosition = new Vector2(_restPosition.x, _restPosition.y + verticalOffset);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_team == null || joystickHandle == null)
        {
            ResetHandle();
            return;
        }

        float deltaY = joystickHandle.anchoredPosition.y - _restPosition.y;
        Unit.UnitState nextState = _currentState;

        if (deltaY >= triggerThreshold)
        {
            nextState = ResolveStateFromUpInput(_currentState);
        }
        else if (deltaY <= -triggerThreshold)
        {
            nextState = ResolveStateFromDownInput(_currentState);
        }

        if (nextState != _currentState)
        {
            _currentState = nextState;
            _team.SetStateForStats(_unitStats, nextState);
        }

        ResetHandle();
    }

    private void ResetHandle()
    {
        if (joystickHandle != null)
        {
            joystickHandle.anchoredPosition = _restPosition;
        }
    }

    private Unit.UnitState ResolveStateFromUpInput(Unit.UnitState currentState)
    {
        switch (currentState)
        {
            case Unit.UnitState.defendBase:
                return Unit.UnitState.followPlayer;
            case Unit.UnitState.followPlayer:
                return Unit.UnitState.raidEnemies;
            default:
                return currentState;
        }
    }

    private Unit.UnitState ResolveStateFromDownInput(Unit.UnitState currentState)
    {
        switch (currentState)
        {
            case Unit.UnitState.raidEnemies:
                return Unit.UnitState.followPlayer;
            case Unit.UnitState.followPlayer:
                return Unit.UnitState.defendBase;
            default:
                return currentState;
        }
    }
}
