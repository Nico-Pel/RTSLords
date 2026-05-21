using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class UIGame : GameBehaviour
{
    public static UIGame Instance;
    
    [Header("Menus")]
    public GameObject menuUnits;
    public GameObject menuBuilds;
    public GameObject menuUpgrades;
    public GameObject menuMill;

    [Header("Buttons")] 
    public Button[] bUnits;
    public Button[] bBuilds;
    public Button[] bUpgrades;
    public Button bAddMill;

    [Header("Texts")] 
    public TextMeshProUGUI tMillCount;

    protected void Awake()
    {
        Instance = this;
    }

    public void OpenMenuUnits(List<Unit> unitsToSetup)
    {
        menuUnits.SetActive(true);
    }
    
    public void OpenMenuBuilds(List<Build> buildsToSetup)
    {
        menuBuilds.SetActive(true);
    }
    
    public void OpenMenuUpgrades(/*List Upgrades*/)
    {
        menuUpgrades.SetActive(true);
    }
    
    public void OpenMenuMill(Mill mill)
    {
        menuMill.SetActive(true);
        tMillCount.text = "" + mill.HarvestCount() / mill.harvestFields.Length;
    }
    
    public void OpenMenuMill(City city)
    {
        menuMill.SetActive(true);
        tMillCount.text = "" + city.HarvestCount() / city.harvestFields.Length;
    }
}
