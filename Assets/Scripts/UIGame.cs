using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Linq;

public class UIGame : GameBehaviour
{
    private static readonly Color DisabledPriceColor = new Color(0.75f, 0.75f, 0.75f, 1f);
    private static readonly Color InsufficientGoldPriceColor = new Color(1f, 0.35f, 0.35f, 1f);
    private static readonly Color NormalImageColor = Color.white;
    private static readonly Color DisabledImageColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    public static UIGame Instance;

    [Header("Menus")]
    public GameObject menuUnits;
    public GameObject menuBuilds;
    public GameObject menuUpgrades;
    public GameObject menuMill;

    [Header("Units Joysticks")] 
    public UnitJoystick[] unitJoysticks;

    [Header("Buttons")] 
    public Button[] bUnits;
    public Button[] bBuilds;
    public Button[] bUpgrades;
    public Button bAddMill;

    [Header("Texts")] 
    public TextMeshProUGUI tMillCount;
    public TextMeshProUGUI[] tCoins;

    private MilitaryBuild _currentMilitaryBuild;
    private BuildZone _currentBuildZone;
    private City _currentCity;
    private Mill _currentMill;
    private TeamManager _playerTeam;
    private UnityEngine.Object _currentMenuOwner;
    private readonly Dictionary<UnitStats, UnitJoystick> _joystickByType = new Dictionary<UnitStats, UnitJoystick>();
    private readonly Dictionary<TextMeshProUGUI, Color> _defaultButtonPriceColors = new Dictionary<TextMeshProUGUI, Color>();

    protected void Awake()
    {
        Instance = this;
        RefreshHudCoinTexts();
    }

    private void Start()
    {
        PlayerController playerController = FindObjectOfType<PlayerController>();
        if (playerController != null)
        {
            _playerTeam = playerController.Team;
        }

        if (_playerTeam == null)
        {
            _playerTeam = FindObjectsOfType<TeamManager>().FirstOrDefault(team => team != null && team.player != null);
        }

        if (_playerTeam != null)
        {
            _playerTeam.OnGoldChanged += UpdateGoldDisplay;
            _playerTeam.OnUnitsChanged += HandlePlayerUnitsChanged;
            _playerTeam.OnUnitTypeStateChanged += HandleUnitTypeStateChanged;
            UpdateGoldDisplay(_playerTeam.CurrentGold);
            RefreshUnitJoysticks();
        }
    }

    private void Update()
    {
#if UNITY_EDITOR
        if (_playerTeam != null && Input.GetKeyDown(KeyCode.Y))
        {
            _playerTeam.AddGold(10);
        }
#endif
    }

    public void OpenMenuUnits(MilitaryBuild militaryBuild)
    {
        _currentMenuOwner = militaryBuild;
        _currentMilitaryBuild = militaryBuild;
        CloseAllMenus();
        menuUnits.SetActive(true);

        for (int i = 0; i < bUnits.Length; i++)
        {
            Button button = bUnits[i];
            if (button == null)
            {
                continue;
            }

            int capturedIndex = i;
            button.gameObject.SetActive(militaryBuild != null && militaryBuild.unitPrefabs != null && capturedIndex < militaryBuild.unitPrefabs.Length);
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                _currentMilitaryBuild?.TrySpawnUnit(capturedIndex);
                RefreshOpenMenuState();
            });

            if (militaryBuild != null && militaryBuild.unitPrefabs != null && capturedIndex < militaryBuild.unitPrefabs.Length)
            {
                Unit unitPrefab = militaryBuild.unitPrefabs[capturedIndex];
                ProductionQueueBlockReason blockReason = militaryBuild.GetUnitQueueBlockReason(capturedIndex);
                SetButtonPrice(button, GetUnitGoldPrice(unitPrefab), blockReason == ProductionQueueBlockReason.InsufficientGold ? InsufficientGoldPriceColor : (blockReason == ProductionQueueBlockReason.None ? GetDefaultButtonPriceColor(button) : DisabledPriceColor));
                SetButtonSprite(button, "iUnit", GetUnitSprite(unitPrefab));
                bool canCreate = blockReason == ProductionQueueBlockReason.None;
                button.interactable = canCreate;
                SetButtonUnitVisualState(button, canCreate);
            }
            else
            {
                SetButtonPrice(button, 0, DisabledPriceColor);
                SetButtonSprite(button, "iUnit", null);
                button.interactable = false;
                SetButtonUnitVisualState(button, false);
            }
        }
    }

    public void OpenMenuUnits(List<Unit> unitsToSetup)
    {
        CloseAllMenus();
        menuUnits.SetActive(true);
    }

    public void OpenMenuBuilds(BuildZone buildZone, List<Build> buildsToSetup)
    {
        _currentMenuOwner = buildZone;
        _currentBuildZone = buildZone;
        CloseAllMenus();
        menuBuilds.SetActive(true);

        for (int i = 0; i < bBuilds.Length; i++)
        {
            Button button = bBuilds[i];
            if (button == null)
            {
                continue;
            }

            bool isAvailable = buildsToSetup != null && i < buildsToSetup.Count && buildsToSetup[i] != null;
            button.gameObject.SetActive(isAvailable);
            button.onClick.RemoveAllListeners();

            if (isAvailable)
            {
                Build buildPrefab = buildsToSetup[i];
                SetButtonPrice(button, buildPrefab.GoldPrice, GetDefaultButtonPriceColor(button));
                SetButtonSprite(button, "iBuild", buildPrefab.buildSprite);
                button.onClick.AddListener(() =>
                {
                    TeamManager team = _currentBuildZone != null && _currentBuildZone.playerActivator != null
                        ? _currentBuildZone.playerActivator.LastTriggeringTeam
                        : null;
                    _currentBuildZone?.TryConstruct(buildPrefab, team);
                });
            }
            else
            {
                SetButtonSprite(button, "iBuild", null);
            }
        }
    }

    public void OpenMenuBuilds(List<Build> buildsToSetup)
    {
        OpenMenuBuilds(null, buildsToSetup);
    }

    public void OpenMenuUpgrades(/*List Upgrades*/)
    {
        _currentMenuOwner = null;
        CloseAllMenus();
        menuUpgrades.SetActive(true);
    }

    public void OpenMenuMill(Mill mill)
    {
        _currentMenuOwner = mill;
        _currentMill = mill;
        _currentCity = null;
        CloseAllMenus();
        menuMill.SetActive(true);

        if (tMillCount != null && mill != null && mill.harvestFields != null && mill.harvestFields.Length > 0)
        {
            tMillCount.text = $"{mill.HarvestCount()}/{mill.harvestFields.Length}";
        }

        if (bAddMill != null)
        {
            bAddMill.onClick.RemoveAllListeners();
            SetButtonPrice(bAddMill, 8, GetDefaultButtonPriceColor(bAddMill));
            bAddMill.onClick.AddListener(() =>
            {
                _currentMill?.TryUnlockHarvestField();
                RefreshOpenMenuState();
            });
        }
    }

    public void OpenMenuMill(City city)
    {
        _currentMenuOwner = city;
        _currentCity = city;
        _currentMill = null;
        CloseAllMenus();
        menuMill.SetActive(true);

        if (tMillCount != null && city != null && city.harvestFields != null && city.harvestFields.Length > 0)
        {
            tMillCount.text = $"{city.HarvestCount()}/{city.harvestFields.Length}";
        }

        if (bAddMill != null)
        {
            bAddMill.onClick.RemoveAllListeners();
            SetButtonPrice(bAddMill, 8, GetDefaultButtonPriceColor(bAddMill));
            bAddMill.onClick.AddListener(() => _currentCity?.TryUnlockHarvestField());
        }
    }

    public void OpenCityMenu(City city)
    {
        _currentMenuOwner = city;
        _currentCity = city;
        _currentMill = null;
        _currentMilitaryBuild = null;
        CloseAllMenus();

        if (menuUnits != null) menuUnits.SetActive(true);
        if (menuMill != null) menuMill.SetActive(true);

        for (int i = 0; i < bUnits.Length; i++)
        {
            Button button = bUnits[i];
            if (button == null)
            {
                continue;
            }

            bool isPeasantButton = i == 0;
            button.gameObject.SetActive(isPeasantButton);
            button.onClick.RemoveAllListeners();

            if (isPeasantButton)
            {
                Unit peasantPrefab = city != null ? city.peasantPrefab : null;
                ProductionQueueBlockReason blockReason = city != null ? city.GetPeasantQueueBlockReason() : ProductionQueueBlockReason.Invalid;
                SetButtonPrice(button, GetUnitGoldPrice(peasantPrefab), blockReason == ProductionQueueBlockReason.InsufficientGold ? InsufficientGoldPriceColor : (blockReason == ProductionQueueBlockReason.None ? GetDefaultButtonPriceColor(button) : DisabledPriceColor));
                SetButtonSprite(button, "iUnit", GetUnitSprite(peasantPrefab));
                button.onClick.AddListener(() =>
                {
                    _currentCity?.TrySpawnPeasant();
                    RefreshOpenMenuState();
                });
                bool canCreate = blockReason == ProductionQueueBlockReason.None;
                button.interactable = canCreate;
                SetButtonUnitVisualState(button, canCreate);
            }
            else
            {
                SetButtonPrice(button, 0, DisabledPriceColor);
                SetButtonSprite(button, "iUnit", null);
                button.interactable = false;
                SetButtonUnitVisualState(button, false);
            }
        }

        if (tMillCount != null && city != null && city.harvestFields != null && city.harvestFields.Length > 0)
        {
            tMillCount.text = $"{city.HarvestCount()}/{city.harvestFields.Length}";
        }

        if (bAddMill != null)
        {
            bAddMill.onClick.RemoveAllListeners();
            SetButtonPrice(bAddMill, 8, GetDefaultButtonPriceColor(bAddMill));
            bAddMill.onClick.AddListener(() =>
            {
                _currentCity?.TryUnlockHarvestField();
                RefreshOpenMenuState();
            });
        }
    }

    public void CloseAllMenus()
    {
        if (menuUnits != null) menuUnits.SetActive(false);
        if (menuBuilds != null) menuBuilds.SetActive(false);
        if (menuUpgrades != null) menuUpgrades.SetActive(false);
        if (menuMill != null) menuMill.SetActive(false);
    }

    public void CloseMenusIfOwnedBy(UnityEngine.Object owner)
    {
        if (owner == null || _currentMenuOwner == owner)
        {
            CloseAllMenus();
            if (_currentMenuOwner == owner)
            {
                _currentMenuOwner = null;
            }
        }
    }

    private void OnDestroy()
    {
        if (_playerTeam != null)
        {
            _playerTeam.OnGoldChanged -= UpdateGoldDisplay;
            _playerTeam.OnUnitsChanged -= HandlePlayerUnitsChanged;
            _playerTeam.OnUnitTypeStateChanged -= HandleUnitTypeStateChanged;
        }
    }

    private void HandlePlayerUnitsChanged()
    {
        RefreshUnitJoysticks();
        RefreshOpenUnitMenuInteractivity();
    }

    private void HandleUnitTypeStateChanged(UnitStats unitStats, Unit.UnitState state)
    {
        if (_joystickByType.TryGetValue(unitStats, out UnitJoystick joystick) && joystick != null)
        {
            joystick.SetState(state);
        }
    }

    private void UpdateGoldDisplay(int goldAmount)
    {
        RefreshHudCoinTexts();

        if (tCoins == null)
        {
            return;
        }

        for (int i = 0; i < tCoins.Length; i++)
        {
            if (tCoins[i] != null)
            {
                tCoins[i].text = goldAmount.ToString();
            }
        }

        RefreshOpenMenuState();
    }

    private void RefreshHudCoinTexts()
    {
        tCoins = FindObjectsOfType<TextMeshProUGUI>(true)
            .Where(IsHudCoinText)
            .ToArray();
    }

    private bool IsHudCoinText(TextMeshProUGUI text)
    {
        if (text == null || text.gameObject.name != "tCoins")
        {
            return false;
        }

        if (text.GetComponentInParent<Button>(true) != null)
        {
            return false;
        }

        return text.transform.parent != null &&
               text.transform.parent.gameObject.name == "CoinsInfo";
    }

    private void RefreshUnitJoysticks()
    {
        if (unitJoysticks == null || unitJoysticks.Length == 0)
        {
            return;
        }

        List<UnitStats> activeTypes = _playerTeam != null
            ? _playerTeam.GetActiveControllableTypes()
            : new List<UnitStats>();

        for (int i = 0; i < unitJoysticks.Length; i++)
        {
            if (unitJoysticks[i] != null)
            {
                unitJoysticks[i].gameObject.SetActive(false);
            }
        }

        Dictionary<UnitStats, UnitJoystick> preservedAssignments = new Dictionary<UnitStats, UnitJoystick>();
        bool[] usedSlots = new bool[unitJoysticks.Length];

        foreach (UnitStats unitStats in activeTypes)
        {
            if (_joystickByType.TryGetValue(unitStats, out UnitJoystick existingJoystick) && existingJoystick != null)
            {
                int slotIndex = Array.IndexOf(unitJoysticks, existingJoystick);
                if (slotIndex >= 0 && !usedSlots[slotIndex])
                {
                    ConfigureJoystick(existingJoystick, unitStats);
                    preservedAssignments[unitStats] = existingJoystick;
                    usedSlots[slotIndex] = true;
                }
            }
        }

        foreach (UnitStats unitStats in activeTypes)
        {
            if (preservedAssignments.ContainsKey(unitStats))
            {
                continue;
            }

            for (int i = 0; i < unitJoysticks.Length; i++)
            {
                if (usedSlots[i] || unitJoysticks[i] == null)
                {
                    continue;
                }

                ConfigureJoystick(unitJoysticks[i], unitStats);
                preservedAssignments[unitStats] = unitJoysticks[i];
                usedSlots[i] = true;
                break;
            }
        }

        _joystickByType.Clear();
        foreach (KeyValuePair<UnitStats, UnitJoystick> pair in preservedAssignments)
        {
            _joystickByType[pair.Key] = pair.Value;
        }
    }

    private void ConfigureJoystick(UnitJoystick joystick, UnitStats unitStats)
    {
        if (joystick == null || _playerTeam == null || unitStats == null)
        {
            return;
        }

        Unit representativeUnit = _playerTeam.GetRepresentativeControllableUnit(unitStats);
        joystick.Setup(_playerTeam, unitStats, representativeUnit != null ? GetUnitSprite(representativeUnit) : null, _playerTeam.GetStateForStats(unitStats));
    }

    private void RefreshOpenUnitMenuInteractivity()
    {
        RefreshOpenMenuState();
    }

    private void RefreshOpenMenuState()
    {
        if (_currentCity != null && ((menuUnits != null && menuUnits.activeSelf) || (menuMill != null && menuMill.activeSelf)))
        {
            OpenCityMenu(_currentCity);
            return;
        }

        if (_currentMilitaryBuild != null && menuUnits != null && menuUnits.activeSelf)
        {
            OpenMenuUnits(_currentMilitaryBuild);
            return;
        }

        if (_currentMill != null && menuMill != null && menuMill.activeSelf)
        {
            OpenMenuMill(_currentMill);
        }
    }

    private int GetUnitGoldPrice(Unit unitPrefab)
    {
        if (unitPrefab == null)
        {
            return 0;
        }

        Hitbox hitbox = unitPrefab.GetComponent<Hitbox>();
        if (hitbox == null || hitbox.unitStats == null)
        {
            return 0;
        }

        return hitbox.unitStats.goldPrice;
    }

    private Sprite GetUnitSprite(Unit unitPrefab)
    {
        if (unitPrefab == null)
        {
            return null;
        }

        if (unitPrefab.unitSprite != null)
        {
            return unitPrefab.unitSprite;
        }

        Hitbox hitbox = unitPrefab.GetComponent<Hitbox>();
        return hitbox != null && hitbox.unitStats != null ? hitbox.unitStats.sprite : null;
    }

    private void SetButtonPrice(Button button, int goldPrice, Color textColor)
    {
        if (button == null)
        {
            return;
        }

        TextMeshProUGUI[] texts = button.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null && texts[i].gameObject.name == "tCoins")
            {
                texts[i].text = goldPrice.ToString();
                texts[i].color = textColor;
            }
        }
    }

    private Color GetDefaultButtonPriceColor(Button button)
    {
        if (button == null)
        {
            return Color.white;
        }

        TextMeshProUGUI[] texts = button.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TextMeshProUGUI text = texts[i];
            if (text == null || text.gameObject.name != "tCoins")
            {
                continue;
            }

            if (!_defaultButtonPriceColors.TryGetValue(text, out Color defaultColor))
            {
                defaultColor = text.color;
                _defaultButtonPriceColors[text] = defaultColor;
            }

            return defaultColor;
        }

        return Color.white;
    }

    private void SetButtonSprite(Button button, string imageObjectName, Sprite sprite)
    {
        if (button == null || string.IsNullOrWhiteSpace(imageObjectName))
        {
            return;
        }

        Image[] images = button.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] == null || images[i].gameObject.name != imageObjectName)
            {
                continue;
            }

            images[i].sprite = sprite;
            images[i].enabled = sprite != null;
        }
    }

    private void SetButtonUnitVisualState(Button button, bool isEnabled)
    {
        if (button == null)
        {
            return;
        }

        if (button.targetGraphic != null)
        {
            button.targetGraphic.color = isEnabled ? NormalImageColor : DisabledImageColor;
        }

        Image[] images = button.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] == null)
            {
                continue;
            }

            if (images[i].gameObject.name == "iUnit")
            {
                images[i].color = isEnabled ? NormalImageColor : DisabledImageColor;
            }
        }
    }
}
