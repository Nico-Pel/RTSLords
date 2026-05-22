using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class AIHeroController : MonoBehaviour
{
    public Build[] possibleBuildsPrefabs;

    public float patrolDistanceFromEnemyCity = 8f;
    public float recruitInterval = 8f;
    public int desiredPeasantCount = 2;

    private float _recruitTimer;

    public TeamManager Team { get; private set; }
    public Unit ControlledUnit { get; private set; }

    private void Awake()
    {
        ControlledUnit = GetComponent<Unit>();
        if (ControlledUnit != null)
        {
            ControlledUnit.isHero = true;
            ConfigureHeroPersistence();
        }
    }

    private void Start()
    {
        ConfigureHeroPersistence();
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
        if (ControlledUnit == null || Team == null)
        {
            return;
        }

        if (Team.player != null || Team.playerAI != this)
        {
            return;
        }

        Vector3 direction = ResolveMovementDirection();
        ControlledUnit.SetControllerMoveInput(direction);

        _recruitTimer -= Time.deltaTime;
        if (_recruitTimer <= 0f)
        {
            _recruitTimer = recruitInterval;
            RunEconomyAndRecruitment();
        }
    }

    private Vector3 ResolveMovementDirection()
    {
        if (Team.EnemyTeam == null || Team.EnemyTeam.city == null)
        {
            return Vector3.zero;
        }

        Hitbox nearbyTarget = ControlledUnit.FindNearestEnemyTarget(8f);
        if (nearbyTarget != null)
        {
            return (nearbyTarget.transform.position - transform.position).normalized;
        }

        Vector3 enemyCityPosition = Team.EnemyTeam.city.transform.position;
        Vector3 flatDelta = enemyCityPosition - transform.position;
        flatDelta.y = 0f;

        if (flatDelta.magnitude <= patrolDistanceFromEnemyCity)
        {
            return Vector3.zero;
        }

        return flatDelta.normalized;
    }

    private void RunEconomyAndRecruitment()
    {
        if (Team.city != null)
        {
            int peasantCount = Team.RegisteredUnits.OfType<Peasant>().Count(unit => unit != null && !unit.IsDead);
            if (peasantCount < desiredPeasantCount)
            {
                Team.city.TrySpawnPeasant();
            }
        }

        foreach (MilitaryBuild militaryBuild in Team.RegisteredBuilds.OfType<MilitaryBuild>())
        {
            if (militaryBuild == null)
            {
                continue;
            }

            militaryBuild.TrySpawnUnit(0);
        }
    }

    private void ConfigureHeroPersistence()
    {
        if (ControlledUnit != null && ControlledUnit.Hitbox != null)
        {
            ControlledUnit.Hitbox.destroyOnDeath = false;
        }
    }
}
