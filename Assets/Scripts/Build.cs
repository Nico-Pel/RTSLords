using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class Build : GameBehaviour
{
    private const string ToonShaderName = "RTSLords/Simple Toon Outline";

    public string buildName;
    public Sprite buildSprite;
    public PlayerActivator playerActivator;
    public Transform spawnPos;

    [HideInInspector] public BuildZone buildZone;

    private static readonly int BlueReplaceColorID = Shader.PropertyToID("_BlueReplaceColor");
    private MaterialPropertyBlock _propertyBlock;

    public TeamManager Team { get; private set; }
    public Hitbox Hitbox { get; private set; }

    protected virtual void Awake()
    {
        _propertyBlock = new MaterialPropertyBlock();
        Hitbox = GetComponent<Hitbox>();
        if (Hitbox != null)
        {
            Hitbox.OnDeath += HandleHitboxDeath;
        }

        if (playerActivator == null)
        {
            playerActivator = GetComponentInChildren<PlayerActivator>(true);
        }
    }

    protected virtual void Start()
    {
        if (string.IsNullOrWhiteSpace(buildName))
        {
            buildName = gameObject.name;
        }

        TeamManager parentTeam = GetComponentInParent<TeamManager>();
        if (parentTeam != null)
        {
            parentTeam.RegisterBuild(this);
        }

        if (playerActivator != null)
        {
            playerActivator.onPlayerTriggered.AddListener(OpenBuildMenu);
            playerActivator.onPlayerExit.AddListener(CloseMenu);
        }
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

    public int GoldPrice
    {
        get
        {
            Hitbox buildHitbox = Hitbox != null ? Hitbox : GetComponent<Hitbox>();
            if (buildHitbox == null || buildHitbox.unitStats == null)
            {
                return 0;
            }

            return buildHitbox.unitStats.goldPrice;
        }
    }

    public virtual void OpenBuildMenu()
    {
        if (!CanOpenMenuForHumanPlayer())
        {
            return;
        }

        UIGame.Instance?.OpenMenuUpgrades();
    }

    protected virtual void CloseMenu()
    {
        UIGame.Instance?.CloseMenusIfOwnedBy(this);
    }

    protected bool CanOpenMenuForHumanPlayer()
    {
        return playerActivator != null &&
               playerActivator.LastTriggeringTeam == Team &&
               playerActivator.LastTriggeringTeam != null &&
               playerActivator.LastTriggeringTeam.player != null;
    }

    public virtual string GetInfoText()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine(buildName);

        if (Hitbox != null && Hitbox.unitStats != null)
        {
            if (!string.IsNullOrWhiteSpace(Hitbox.unitStats.description))
            {
                builder.AppendLine(Hitbox.unitStats.description);
            }

            builder.AppendLine($"HP: {Hitbox.unitStats.health}");
        }

        return builder.ToString().Trim();
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

    private void HandleHitboxDeath(Hitbox hitbox)
    {
        buildZone?.ClearBuild(this);
        Team?.UnregisterBuild(this);
    }
}
