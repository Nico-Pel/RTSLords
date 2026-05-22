using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Tree : MonoBehaviour
{
    public int woodQuantity = 10;

    public bool IsDepleted => woodQuantity <= 0;

    public bool HarvestOneWood()
    {
        if (woodQuantity <= 0)
        {
            return false;
        }

        woodQuantity--;
        return true;
    }
}
