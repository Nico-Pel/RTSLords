using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Unit))]
public class AIController : MonoBehaviour
{
    public TeamManager explicitTeam;

    private Unit _unit;

    private void Awake()
    {
        _unit = GetComponent<Unit>();
    }

    private void Start()
    {
        if (explicitTeam != null && _unit != null)
        {
            explicitTeam.RegisterUnit(_unit);
        }
    }
}
