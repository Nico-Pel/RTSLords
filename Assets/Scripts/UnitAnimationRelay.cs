using UnityEngine;

public class UnitAnimationRelay : MonoBehaviour
{
    [SerializeField] private Unit owner;

    public void Bind(Unit unit)
    {
        owner = unit;
    }

    public void AttackImpact()
    {
        owner?.AnimationAttackImpact();
    }

    public void OnAttackImpact()
    {
        owner?.AnimationAttackImpact();
    }
}
