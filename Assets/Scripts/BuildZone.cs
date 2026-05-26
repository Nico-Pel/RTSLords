using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BuildZone : GameBehaviour
{
    public PlayerActivator playerActivator;

    [SerializeField, ReadOnly] private Build _currentBuild;
    [SerializeField] private GameObject[] occupiedVisualsToHide;

    public bool IsOccupied => _currentBuild != null;

    private void Awake()
    {
        if (playerActivator == null)
        {
            playerActivator = GetComponentInChildren<PlayerActivator>(true);
        }

        DisableDecorativeTreeColliders();
    }

    private void Start()
    {
        SetOccupiedState(_currentBuild != null);

        if (playerActivator != null)
        {
            playerActivator.onPlayerTriggered.AddListener(OpenBuildMenu);
            playerActivator.onPlayerExit.AddListener(CloseMenu);
        }
    }

    private void OpenBuildMenu()
    {
        if (IsOccupied)
        {
            CloseMenu();
            return;
        }

        if (!CanOpenMenuForHumanPlayer())
        {
            return;
        }

        TeamManager team = playerActivator.LastTriggeringTeam;
        List<Build> builds = new List<Build>();

        if (team.player != null && team.player.possibleBuildsPrefabs != null)
        {
            builds.AddRange(team.player.possibleBuildsPrefabs);
        }
        else if (team.playerAI != null && team.playerAI.possibleBuildsPrefabs != null)
        {
            builds.AddRange(team.playerAI.possibleBuildsPrefabs);
        }

        UIGame.Instance?.OpenMenuBuilds(this, builds);
    }

    private bool CanOpenMenuForHumanPlayer()
    {
        return playerActivator != null &&
               playerActivator.LastTriggeringTeam != null &&
               playerActivator.LastTriggeringTeam.player != null;
    }

    private void CloseMenu()
    {
        UIGame.Instance?.CloseMenusIfOwnedBy(this);
    }

    public bool TryConstruct(Build buildPrefab, TeamManager team)
    {
        if (IsOccupied || buildPrefab == null || team == null)
        {
            CloseMenu();
            return false;
        }

        int goldPrice = buildPrefab.GoldPrice;
        if (!team.SpendGold(goldPrice))
        {
            return false;
        }

        float baseYaw = buildPrefab.transform.eulerAngles.y;
        Quaternion rotation = Quaternion.Euler(0f, baseYaw + team.BuildFacingY, 0f);
        Build build = Instantiate(buildPrefab, transform.position, rotation, team.BuildsRoot);
        build.buildZone = this;
        team.RegisterBuild(build);
        _currentBuild = build;
        SetOccupiedState(true);
        UIGame.Instance?.CloseMenusIfOwnedBy(this);
        PlayerActivator.RefreshInteractionsFor(team.GetHeroUnit());
        return true;
    }

    public void ClearBuild(Build build)
    {
        if (_currentBuild == build)
        {
            _currentBuild = null;
            SetOccupiedState(false);
        }
    }

    private void SetOccupiedState(bool occupied)
    {
        if (playerActivator != null)
        {
            SphereCollider sphereCollider = playerActivator.GetComponent<SphereCollider>();
            if (sphereCollider != null)
            {
                sphereCollider.enabled = !occupied;
            }

            SpriteRenderer[] sprites = playerActivator.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < sprites.Length; i++)
            {
                sprites[i].enabled = !occupied;
            }
        }

        if (occupiedVisualsToHide == null)
        {
            return;
        }

        for (int i = 0; i < occupiedVisualsToHide.Length; i++)
        {
            if (occupiedVisualsToHide[i] != null)
            {
                occupiedVisualsToHide[i].SetActive(!occupied);
            }
        }
    }

    private void DisableDecorativeTreeColliders()
    {
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null)
            {
                continue;
            }

            if (playerActivator != null && collider.transform.IsChildOf(playerActivator.transform))
            {
                continue;
            }

            collider.enabled = false;
        }

        Tree[] decorativeTrees = GetComponentsInChildren<Tree>(true);
        for (int i = 0; i < decorativeTrees.Length; i++)
        {
            if (decorativeTrees[i] != null)
            {
                decorativeTrees[i].enabled = false;
            }
        }
    }
}
