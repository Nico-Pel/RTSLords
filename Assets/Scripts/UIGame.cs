using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Linq;

public class UIGame : GameBehaviour
{
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
    private readonly Dictionary<Unit.UnitType, UnitJoystick> _joystickByType = new Dictionary<Unit.UnitType, UnitJoystick>();

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
            button.onClick.AddListener(() => _currentMilitaryBuild?.TrySpawnUnit(capturedIndex));

            if (militaryBuild != null && militaryBuild.unitPrefabs != null && capturedIndex < militaryBuild.unitPrefabs.Length)
            {
                Unit unitPrefab = militaryBuild.unitPrefabs[capturedIndex];
                SetButtonPrice(button, GetUnitGoldPrice(unitPrefab));
                SetButtonSprite(button, "iUnit", GetUnitSprite(unitPrefab));
                button.interactable = CanCreateUnitType(unitPrefab);
            }
            else
            {
                SetButtonSprite(button, "iUnit", null);
                button.interactable = false;
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
                SetButtonPrice(button, buildPrefab.GoldPrice);
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
            SetButtonPrice(bAddMill, 8);
            bAddMill.onClick.AddListener(() => _currentMill?.TryUnlockHarvestField());
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
            SetButtonPrice(bAddMill, 8);
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
                SetButtonPrice(button, GetUnitGoldPrice(peasantPrefab));
                SetButtonSprite(button, "iUnit", GetUnitSprite(peasantPrefab));
                button.onClick.AddListener(() => _currentCity?.TrySpawnPeasant());
                button.interactable = true;
            }
            else
            {
                SetButtonSprite(button, "iUnit", null);
                button.interactable = false;
            }
        }

        if (tMillCount != null && city != null && city.harvestFields != null && city.harvestFields.Length > 0)
        {
            tMillCount.text = $"{city.HarvestCount()}/{city.harvestFields.Length}";
        }

        if (bAddMill != null)
        {
            bAddMill.onClick.RemoveAllListeners();
            SetButtonPrice(bAddMill, 8);
            bAddMill.onClick.AddListener(() => _currentCity?.TryUnlockHarvestField());
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

    private void HandleUnitTypeStateChanged(Unit.UnitType unitType, Unit.UnitState state)
    {
        if (_joystickByType.TryGetValue(unitType, out UnitJoystick joystick) && joystick != null)
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

        List<Unit.UnitType> activeTypes = _playerTeam != null
            ? _playerTeam.GetActiveControllableTypes()
            : new List<Unit.UnitType>();

        for (int i = 0; i < unitJoysticks.Length; i++)
        {
            if (unitJoysticks[i] != null)
            {
                unitJoysticks[i].gameObject.SetActive(false);
            }
        }

        Dictionary<Unit.UnitType, UnitJoystick> preservedAssignments = new Dictionary<Unit.UnitType, UnitJoystick>();
        bool[] usedSlots = new bool[unitJoysticks.Length];

        foreach (Unit.UnitType unitType in activeTypes)
        {
            if (_joystickByType.TryGetValue(unitType, out UnitJoystick existingJoystick) && existingJoystick != null)
            {
                int slotIndex = Array.IndexOf(unitJoysticks, existingJoystick);
                if (slotIndex >= 0 && !usedSlots[slotIndex])
                {
                    ConfigureJoystick(existingJoystick, unitType);
                    preservedAssignments[unitType] = existingJoystick;
                    usedSlots[slotIndex] = true;
                }
            }
        }

        foreach (Unit.UnitType unitType in activeTypes)
        {
            if (preservedAssignments.ContainsKey(unitType))
            {
                continue;
            }

            for (int i = 0; i < unitJoysticks.Length; i++)
            {
                if (usedSlots[i] || unitJoysticks[i] == null)
                {
                    continue;
                }

                ConfigureJoystick(unitJoysticks[i], unitType);
                preservedAssignments[unitType] = unitJoysticks[i];
                usedSlots[i] = true;
                break;
            }
        }

        _joystickByType.Clear();
        foreach (KeyValuePair<Unit.UnitType, UnitJoystick> pair in preservedAssignments)
        {
            _joystickByType[pair.Key] = pair.Value;
        }
    }

    private void ConfigureJoystick(UnitJoystick joystick, Unit.UnitType unitType)
    {
        if (joystick == null || _playerTeam == null)
        {
            return;
        }

        Unit representativeUnit = _playerTeam.GetRepresentativeControllableUnit(unitType);
        joystick.Setup(_playerTeam, unitType, representativeUnit != null ? GetUnitSprite(representativeUnit) : null, _playerTeam.GetStateForType(unitType));
    }

    private void RefreshOpenUnitMenuInteractivity()
    {
        if (_currentMilitaryBuild != null && menuUnits != null && menuUnits.activeSelf)
        {
            OpenMenuUnits(_currentMilitaryBuild);
        }
    }

    private bool CanCreateUnitType(Unit unitPrefab)
    {
        if (_playerTeam == null || unitPrefab == null)
        {
            return false;
        }

        if (unitPrefab is Peasant || unitPrefab.IsHero || unitPrefab.PrimaryUnitType == Unit.UnitType.other)
        {
            return true;
        }

        if (_playerTeam.HasActiveControllableType(unitPrefab.PrimaryUnitType))
        {
            return true;
        }

        return _playerTeam.GetActiveControllableTypeCount() < 5;
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

    private void SetButtonPrice(Button button, int goldPrice)
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
            }
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
}
