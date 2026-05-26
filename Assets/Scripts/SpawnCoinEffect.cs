using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public static class SpawnCoinEffect
{
    private const string ResourcePath = "SpawnCoinPrefab";

    private static GameObject _cachedPrefab;

    public static void SpawnAt(Vector3 worldPosition, float verticalOffset = 0.9f)
    {
        GameObject prefab = LoadPrefab();
        if (prefab == null)
        {
            return;
        }

        Vector3 spawnPosition = worldPosition + Vector3.up * verticalOffset;
        GameObject instance = Object.Instantiate(prefab, spawnPosition, Quaternion.identity);
        if (instance != null)
        {
            Object.Destroy(instance, 1.2f);
        }
    }

    public static void SpawnAbove(Transform anchor, float verticalOffset = 0.9f)
    {
        if (anchor == null)
        {
            return;
        }

        SpawnAt(anchor.position, verticalOffset);
    }

    private static GameObject LoadPrefab()
    {
        if (_cachedPrefab != null)
        {
            return _cachedPrefab;
        }

        _cachedPrefab = Resources.Load<GameObject>(ResourcePath);

#if UNITY_EDITOR
        if (_cachedPrefab == null)
        {
            _cachedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/SpawnCoinPrefab.prefab");
        }
#endif

        return _cachedPrefab;
    }
}
