using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HarvestField : MonoBehaviour
{
    public Transform wayPoint;
    public float radius = 1.5f;
    public float waypointArrivalDistance = 0.2f;

    public Peasant OccupiedBy { get; private set; }
    public bool IsAvailable => OccupiedBy == null;

    public bool TryAssign(Peasant peasant)
    {
        if (!IsAvailable && OccupiedBy != peasant)
        {
            return false;
        }

        OccupiedBy = peasant;
        return true;
    }

    public void Release(Peasant peasant)
    {
        if (OccupiedBy == peasant)
        {
            OccupiedBy = null;
        }
    }

    public Vector3 GetWorkPosition()
    {
        return wayPoint == null ? transform.position : wayPoint.position;
    }

    public float GetArrivalDistance()
    {
        if (wayPoint != null)
        {
            return Mathf.Max(0.05f, waypointArrivalDistance);
        }

        return Mathf.Max(0.05f, radius);
    }
}
