using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UnitJoystick : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private RectTransform joystickHandle;
    [SerializeField] private Image unitImage;
    [SerializeField] private Image stateBackground;
    [SerializeField] private float maxVerticalOffset = 42f;
    [SerializeField] private float triggerThreshold = 18f;
    [SerializeField] private Color defendColor = new Color(0.12f, 0.5f, 1f, 1f);
    [SerializeField] private Color followColor = new Color(1f, 0.62f, 0.12f, 1f);
    [SerializeField] private Color raidColor = new Color(0.95f, 0.22f, 0.22f, 1f);

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

        if (stateBackground == null && joystickHandle != null)
        {
            stateBackground = joystickHandle.GetComponent<Image>();
        }

        if (joystickHandle != null)
        {
            _restPosition = joystickHandle.anchoredPosition;
        }
    }

    public void Setup(TeamManager team, UnitStats unitStats, Sprite fallbackSprite, Unit.UnitState state)
    {
        _team = team;
        _unitStats = unitStats;
        _currentState = state;

        if (unitImage != null)
        {
            Sprite resolvedSprite = ResolveUnitSprite(fallbackSprite);
            unitImage.sprite = resolvedSprite;
            unitImage.enabled = resolvedSprite != null;
        }

        if (joystickHandle != null)
        {
            joystickHandle.anchoredPosition = _restPosition;
        }

        RefreshStateVisual();

        gameObject.SetActive(true);
    }

    private Sprite ResolveUnitSprite(Sprite fallbackSprite)
    {
        if (fallbackSprite != null)
        {
            return fallbackSprite;
        }

        if (_unitStats != null && _unitStats.sprite != null)
        {
            return _unitStats.sprite;
        }

        return null;
    }

    public void SetState(Unit.UnitState state)
    {
        _currentState = state;
        RefreshStateVisual();
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
        else if (deltaY >= triggerThreshold && _currentState == Unit.UnitState.raidEnemies)
        {
            _team.ForceRetargetForStats(_unitStats);
        }
        else if (deltaY <= -triggerThreshold && _currentState == Unit.UnitState.defendBase)
        {
            _team.ForceRetreatForStats(_unitStats);
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

    private void RefreshStateVisual()
    {
        if (stateBackground == null)
        {
            return;
        }

        switch (_currentState)
        {
            case Unit.UnitState.raidEnemies:
                stateBackground.color = raidColor;
                break;
            case Unit.UnitState.followPlayer:
                stateBackground.color = followColor;
                break;
            default:
                stateBackground.color = defendColor;
                break;
        }
    }

    private Unit.UnitState ResolveStateFromUpInput(Unit.UnitState currentState)
    {
        switch (currentState)
        {
            case Unit.UnitState.defendBase:
                return Unit.UnitState.followPlayer;
            case Unit.UnitState.followPlayer:
                return SupportsRaidState() ? Unit.UnitState.raidEnemies : Unit.UnitState.followPlayer;
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

    private bool SupportsRaidState()
    {
        return _unitStats == null || !_unitStats.isSupportHealer;
    }
}
