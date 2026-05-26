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
    public TextMeshProUGUI tPeasantCount;
    public TextMeshProUGUI tUnitCount;

    private MilitaryBuild _currentMilitaryBuild;
    private BuildZone _currentBuildZone;
    private City _currentCity;
    private Mill _currentMill;
    private TeamManager _playerTeam;
    private UnityEngine.Object _currentMenuOwner;
    private readonly List<Build> _currentBuildOptions = new List<Build>();
    private readonly Dictionary<UnitStats, UnitJoystick> _joystickByType = new Dictionary<UnitStats, UnitJoystick>();
    private readonly Dictionary<TextMeshProUGUI, Color> _defaultButtonPriceColors = new Dictionary<TextMeshProUGUI, Color>();

    protected void Awake()
    {
        Instance = this;
        CacheDefaultButtonPriceColors();
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
            RefreshPopulationCounters();
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
        _currentCity = null;
        _currentMill = null;
        _currentBuildZone = null;
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
        List<Build> buildOptions = buildsToSetup != null
            ? new List<Build>(buildsToSetup)
            : null;

        _currentMenuOwner = buildZone;
        _currentBuildZone = buildZone;
        _currentMilitaryBuild = null;
        _currentCity = null;
        _currentMill = null;
        _currentBuildOptions.Clear();
        if (buildOptions != null)
        {
            _currentBuildOptions.AddRange(buildOptions);
        }
        CloseAllMenus();
        menuBuilds.SetActive(true);

        TeamManager activeTeam = buildZone != null && buildZone.playerActivator != null
            ? buildZone.playerActivator.LastTriggeringTeam
            : _playerTeam;

        for (int i = 0; i < bBuilds.Length; i++)
        {
            Button button = bBuilds[i];
            if (button == null)
            {
                continue;
            }

            bool isAvailable = buildOptions != null && i < buildOptions.Count && buildOptions[i] != null;
            button.gameObject.SetActive(isAvailable);
            button.onClick.RemoveAllListeners();

            if (isAvailable)
            {
                Build buildPrefab = buildOptions[i];
                bool hasEnoughGold = activeTeam != null && activeTeam.CurrentGold >= buildPrefab.GoldPrice;
                SetButtonPrice(button, buildPrefab.GoldPrice, ResolveBuildButtonPriceColor(activeTeam, buildPrefab, button));
                SetButtonSprite(button, "iBuild", buildPrefab.buildSprite);
                button.interactable = hasEnoughGold;
                RestoreButtonVisualState(button, "iBuild");
                button.onClick.AddListener(() =>
                {
                    TeamManager team = _currentBuildZone != null && _currentBuildZone.playerActivator != null
                        ? _currentBuildZone.playerActivator.LastTriggeringTeam
                        : null;
                    _currentBuildZone?.TryConstruct(buildPrefab, team);
                    RefreshOpenMenuState();
                });
            }
            else
            {
                SetButtonPrice(button, 0, DisabledPriceColor);
                SetButtonSprite(button, "iBuild", null);
                button.interactable = false;
                RestoreButtonVisualState(button, "iBuild");
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
        _currentMilitaryBuild = null;
        _currentBuildZone = null;
        _currentCity = null;
        _currentMill = null;
        CloseAllMenus();
        menuUpgrades.SetActive(true);
    }

    public void OpenMenuMill(Mill mill)
    {
        _currentMenuOwner = mill;
        _currentMill = mill;
        _currentCity = null;
        _currentMilitaryBuild = null;
        _currentBuildZone = null;
        CloseAllMenus();
        menuMill.SetActive(true);

        if (tMillCount != null && mill != null && mill.harvestFields != null && mill.harvestFields.Length > 0)
        {
            tMillCount.text = $"{mill.HarvestCount()}/{mill.harvestFields.Length}";
        }

        if (bAddMill != null)
        {
            bAddMill.onClick.RemoveAllListeners();
            bool canUnlock = CanAffordMillUpgrade(_currentMill, 8);
            SetButtonPrice(bAddMill, 8, canUnlock ? GetDefaultButtonPriceColor(bAddMill) : ResolveMillButtonPriceColor(_currentMill, 8));
            bAddMill.interactable = canUnlock;
            RestoreButtonVisualState(bAddMill);
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
        _currentMilitaryBuild = null;
        _currentBuildZone = null;
        CloseAllMenus();
        menuMill.SetActive(true);

        if (tMillCount != null && city != null && city.harvestFields != null && city.harvestFields.Length > 0)
        {
            tMillCount.text = $"{city.HarvestCount()}/{city.harvestFields.Length}";
        }

        if (bAddMill != null)
        {
            bAddMill.onClick.RemoveAllListeners();
            bool canUnlock = CanAffordMillUpgrade(_currentCity, 8);
            SetButtonPrice(bAddMill, 8, canUnlock ? GetDefaultButtonPriceColor(bAddMill) : ResolveMillButtonPriceColor(_currentCity, 8));
            bAddMill.interactable = canUnlock;
            RestoreButtonVisualState(bAddMill);
            bAddMill.onClick.AddListener(() =>
            {
                _currentCity?.TryUnlockHarvestField();
                RefreshOpenMenuState();
            });
        }
    }

    public void OpenCityMenu(City city)
    {
        _currentMenuOwner = city;
        _currentCity = city;
        _currentMill = null;
        _currentMilitaryBuild = null;
        _currentBuildZone = null;
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
            bool canUnlock = CanAffordMillUpgrade(_currentCity, 8);
            SetButtonPrice(bAddMill, 8, canUnlock ? GetDefaultButtonPriceColor(bAddMill) : ResolveMillButtonPriceColor(_currentCity, 8));
            bAddMill.interactable = canUnlock;
            RestoreButtonVisualState(bAddMill);
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
                _currentMilitaryBuild = null;
                _currentBuildZone = null;
                _currentCity = null;
                _currentMill = null;
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
        RefreshPopulationCounters();
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

    private void RefreshPopulationCounters()
    {
        if (_playerTeam == null)
        {
            return;
        }

        if (tPeasantCount != null)
        {
            tPeasantCount.text = $"{_playerTeam.GetCurrentPeasantCount()}/{_playerTeam.MaxPeasantCount}";
        }

        if (tUnitCount != null)
        {
            tUnitCount.text = $"{_playerTeam.GetCurrentCombatUnitCount()}/{_playerTeam.MaxCombatUnitCount}";
        }
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
        Sprite joystickSprite = representativeUnit != null
            ? GetUnitSprite(representativeUnit)
            : unitStats.sprite;
        joystick.Setup(_playerTeam, unitStats, joystickSprite, _playerTeam.GetStateForStats(unitStats));
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

        if (_currentBuildZone != null && menuBuilds != null && menuBuilds.activeSelf)
        {
            OpenMenuBuilds(_currentBuildZone, _currentBuildOptions);
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

    private void CacheDefaultButtonPriceColors()
    {
        CacheDefaultButtonPriceColor(bAddMill);

        if (bUnits != null)
        {
            for (int i = 0; i < bUnits.Length; i++)
            {
                CacheDefaultButtonPriceColor(bUnits[i]);
            }
        }

        if (bBuilds != null)
        {
            for (int i = 0; i < bBuilds.Length; i++)
            {
                CacheDefaultButtonPriceColor(bBuilds[i]);
            }
        }

        if (bUpgrades != null)
        {
            for (int i = 0; i < bUpgrades.Length; i++)
            {
                CacheDefaultButtonPriceColor(bUpgrades[i]);
            }
        }
    }

    private void CacheDefaultButtonPriceColor(Button button)
    {
        if (button == null)
        {
            return;
        }

        TextMeshProUGUI[] texts = button.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TextMeshProUGUI text = texts[i];
            if (text == null || text.gameObject.name != "tCoins" || _defaultButtonPriceColors.ContainsKey(text))
            {
                continue;
            }

            _defaultButtonPriceColors[text] = text.color;
        }
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
        SetButtonVisualState(button, "iUnit", isEnabled);
    }

    private void RestoreButtonVisualState(Button button, string imageObjectName = null)
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

            images[i].color = Color.white;
        }
    }

    private void SetButtonVisualState(Button button, string imageObjectName, bool isEnabled)
    {
        if (button == null)
        {
            return;
        }

        Image[] images = button.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] == null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(imageObjectName) || images[i].gameObject.name == imageObjectName)
            {
                images[i].color = isEnabled ? Color.white : DisabledImageColor;
            }
        }
    }

    private bool CanAffordMillUpgrade(Mill mill, int goldCost)
    {
        return mill != null &&
               mill.Team != null &&
               mill.FindFirstInactiveField() != null &&
               mill.Team.CurrentGold >= goldCost;
    }

    private bool CanAffordMillUpgrade(City city, int goldCost)
    {
        return city != null &&
               city.Team != null &&
               city.FindFirstInactiveField() != null &&
               city.Team.CurrentGold >= goldCost;
    }

    private Color ResolveMillButtonPriceColor(Mill mill, int goldCost)
    {
        if (mill == null || mill.Team == null || mill.FindFirstInactiveField() == null)
        {
            return DisabledPriceColor;
        }

        return mill.Team.CurrentGold >= goldCost ? GetDefaultButtonPriceColor(bAddMill) : InsufficientGoldPriceColor;
    }

    private Color ResolveMillButtonPriceColor(City city, int goldCost)
    {
        if (city == null || city.Team == null || city.FindFirstInactiveField() == null)
        {
            return DisabledPriceColor;
        }

        return city.Team.CurrentGold >= goldCost ? GetDefaultButtonPriceColor(bAddMill) : InsufficientGoldPriceColor;
    }

    private Color ResolveBuildButtonPriceColor(TeamManager team, Build buildPrefab, Button button)
    {
        if (team == null || buildPrefab == null)
        {
            return DisabledPriceColor;
        }

        return team.CurrentGold >= buildPrefab.GoldPrice ? GetDefaultButtonPriceColor(button) : InsufficientGoldPriceColor;
    }
}
