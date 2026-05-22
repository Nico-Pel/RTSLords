using UnityEngine;

public class Projectile : MonoBehaviour
{
    [SerializeField] private float hitDistance = 0.1f;

    private Hitbox _target;
    private int _damage;
    private Hitbox.DamageTypes _damageType;
    private float _speed;
    private UnitStats _sourceStats;

    public void Setup(Hitbox target, int damage, Hitbox.DamageTypes damageType, float speed, UnitStats sourceStats = null)
    {
        _target = target;
        _damage = damage;
        _damageType = damageType;
        _speed = Mathf.Max(0.1f, speed);
        _sourceStats = sourceStats;
    }

    private void Update()
    {
        if (_target == null || _target.IsDead)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 targetPosition = GetTargetPosition();
        Vector3 toTarget = targetPosition - transform.position;
        float distance = toTarget.magnitude;

        if (distance <= hitDistance)
        {
            Impact();
            return;
        }

        float step = _speed * Time.deltaTime;
        if (step >= distance)
        {
            transform.position = targetPosition;
            Impact();
            return;
        }

        Vector3 direction = toTarget / distance;
        transform.position += direction * step;
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    private Vector3 GetTargetPosition()
    {
        Collider targetCollider = _target.GetComponent<Collider>();
        if (targetCollider != null)
        {
            return targetCollider.bounds.center;
        }

        return _target.transform.position;
    }

    private void Impact()
    {
        if (_target != null && !_target.IsDead)
        {
            _target.TakeDamage(_damage, _damageType, _sourceStats);
        }

        Destroy(gameObject);
    }
}
