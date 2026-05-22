using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Tree : MonoBehaviour
{
    public int woodQuantity = 10;
    public float hitShakeAngle = 1.5f;
    public float hitShakeDuration = 0.12f;

    public bool IsDepleted => woodQuantity <= 0;

    private Coroutine _shakeRoutine;
    private Quaternion _baseRotation;

    private void Awake()
    {
        _baseRotation = transform.localRotation;
    }

    public bool HarvestOneWood()
    {
        if (woodQuantity <= 0)
        {
            return false;
        }

        woodQuantity--;
        return true;
    }

    public void PlayHitShake()
    {
        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        if (_shakeRoutine != null)
        {
            StopCoroutine(_shakeRoutine);
        }

        _shakeRoutine = StartCoroutine(HitShakeRoutine());
    }

    private IEnumerator HitShakeRoutine()
    {
        _baseRotation = transform.localRotation;
        Vector3 randomTilt = new Vector3(
            Random.Range(-hitShakeAngle, hitShakeAngle),
            0f,
            Random.Range(-hitShakeAngle, hitShakeAngle));

        Quaternion targetRotation = _baseRotation * Quaternion.Euler(randomTilt);
        float halfDuration = Mathf.Max(0.01f, hitShakeDuration * 0.5f);
        float timer = 0f;

        while (timer < halfDuration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / halfDuration);
            transform.localRotation = Quaternion.Slerp(_baseRotation, targetRotation, t);
            yield return null;
        }

        timer = 0f;
        while (timer < halfDuration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / halfDuration);
            transform.localRotation = Quaternion.Slerp(targetRotation, _baseRotation, t);
            yield return null;
        }

        transform.localRotation = _baseRotation;
        _shakeRoutine = null;
    }
}
