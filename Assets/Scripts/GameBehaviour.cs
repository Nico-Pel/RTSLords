using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.AI;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class GameBehaviour : MonoBehaviour
{
    private Dictionary<string, Coroutine> _invokes = new Dictionary<string, Coroutine>();

    private Dictionary<string, Coroutine> Invokes
    {
        get
        {
            if (_invokes == null)
                _invokes = new Dictionary<string, Coroutine>();

            return _invokes;
        }
    }

    // Invoke avec nom optionnel
    public void Invoke(float delay, Action action)
    {
        // Générer un nom unique pour cet invoke interne (on ne pourra pas l'annuler de l'extérieur)
        string uniqueName = Guid.NewGuid().ToString();
        Invoke(uniqueName, delay, action);
    }

    // Invoke avec nom fourni
    public void Invoke(string name, float delay, Action action)
    {
        // Si un invoke du même nom existe, on l'annule
        CancelInvoke(name);

        Coroutine coroutine = StartCoroutine(InvokeCoroutine(delay, action, name));
        Invokes[name] = coroutine;
    }

    private IEnumerator InvokeCoroutine(float delay, Action action, string name)
    {
        yield return new WaitForSeconds(delay);
        Invokes.Remove(name); // Supprime après exécution
        action?.Invoke();
    }

    public void CancelInvoke(string name)
    {
        if (Invokes.TryGetValue(name, out Coroutine coroutine))
        {
            StopCoroutine(coroutine);
            Invokes.Remove(name);
        }
    }
    
    protected float DivideByPercentage(float value, float percentage)
    {
        percentage = Mathf.Clamp(percentage, 0f, 100f);

        float t = percentage / 100f;

        // Courbe exponentielle : 1 → 4
        float divisor = Mathf.Pow(4f, t);

        return value / divisor;
    }
    
    public static int LayerMaskToLayer(LayerMask mask)
    {
        int value = mask.value;

        if (value == 0 || (value & (value - 1)) != 0)
        {
            Debug.LogError("LayerMask must contain exactly ONE layer");
            return -1;
        }

        return Mathf.RoundToInt(Mathf.Log(value, 2));
    }
    
    public void ChangeLayer(LayerMask mask, int ignoredLayerIndex = -1)
    {
        int layer = LayerMaskToLayer(mask);
        if (layer == -1) return;

        ApplyLayerRecursively(gameObject, layer, ignoredLayerIndex);
    }
    
    public void ChangeLayer(int layerIndex, int ignoredLayerIndex = -1)
    {
        if (layerIndex == -1) return;

        ApplyLayerRecursively(gameObject, layerIndex, ignoredLayerIndex);
    }
    
    public void ChangeLayer(GameObject g, int layerIndex, int ignoredLayerIndex = -1)
    {
        if (layerIndex == -1) return;

        ApplyLayerRecursively(g, layerIndex, ignoredLayerIndex);
    }
    
    private void ApplyLayerRecursively(GameObject obj, int layer, int ignoreLayerIndex = -1)
    {
        if (layer < 0 || layer > 31)
        {
            Debug.LogError($"Invalid layer index: {layer}");
            return;
        }

        if ((int)obj.layer != ignoreLayerIndex)
        {
            obj.layer = layer;
        }

        foreach (Transform child in obj.transform)
        {
            ApplyLayerRecursively(child.gameObject, layer, ignoreLayerIndex);
        }
    }
    
    protected bool IsNearFromMyAgent(
        NavMeshAgent mAgent,
        Transform mTransform,
        float maxPathDistance = 10f,
        float sampleRadius = 5f)
    {
        // 1. Projection sur le NavMesh
        if (!NavMesh.SamplePosition(
                mTransform.position,
                out NavMeshHit hit,
                sampleRadius,
                NavMesh.AllAreas))
        {
            return false;
        }

        // 2. Calcul du path
        NavMeshPath path = new NavMeshPath();
        if (!mAgent.CalculatePath(hit.position, path))
            return false;

        if (path.status != NavMeshPathStatus.PathComplete)
            return false;

        // 3. Calcul de la longueur du chemin
        float pathLength = 0f;
        for (int i = 1; i < path.corners.Length; i++)
        {
            pathLength += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        }

        // 4. Comparaison
        return pathLength <= maxPathDistance;
    }

    protected float PathDistanceForAnAgent(NavMeshAgent mAgent,
        Vector3 positionToTarget,
        float sampleRadius = 0.1f)
    {
        if (mAgent == null || !mAgent.isOnNavMesh)
            return -1f;

        // 1. Projection de la position cible sur le NavMesh
        if (!NavMesh.SamplePosition(
                positionToTarget,
                out NavMeshHit hit,
                sampleRadius,
                NavMesh.AllAreas))
        {
            return -1f;
        }

        // 2. Calcul du path
        NavMeshPath path = new NavMeshPath();
        if (!mAgent.CalculatePath(hit.position, path))
            return -1f;

        if (path.status != NavMeshPathStatus.PathComplete)
            return -1f;

        // 3. Calcul de la longueur du chemin
        float pathLength = 0f;
        Vector3[] corners = path.corners;

        for (int i = 1; i < corners.Length; i++)
        {
            pathLength += Vector3.Distance(corners[i - 1], corners[i]);
        }

        return pathLength;
    }
    
    protected float PathDistance(
        Vector3 startPosition,
        Vector3 targetPosition,
        float sampleRadius = 0.1f)
    {
        // 1️⃣ Projection du point de départ sur le NavMesh
        if (!NavMesh.SamplePosition(startPosition, out NavMeshHit startHit, sampleRadius, NavMesh.AllAreas))
            return -1f;

        // 2️⃣ Projection de la cible sur le NavMesh
        if (!NavMesh.SamplePosition(targetPosition, out NavMeshHit targetHit, sampleRadius, NavMesh.AllAreas))
            return -1f;

        // 3️⃣ Calcul du path
        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.CalculatePath(startHit.position, targetHit.position, NavMesh.AllAreas, path))
            return -1f;

        if (path.status != NavMeshPathStatus.PathComplete)
            return -1f;

        // 4️⃣ Calcul de la longueur du chemin
        float pathLength = 0f;
        Vector3[] corners = path.corners;

        for (int i = 1; i < corners.Length; i++)
        {
            pathLength += Vector3.Distance(corners[i - 1], corners[i]);
        }

        return pathLength;
    }
    
    protected bool AlmostEquals(float f, int i, float minimumDistance = 0.01f)
    {
        return Mathf.Abs(f - i) < minimumDistance;
    }

    public float ConvertToFahrenheit(float celsciusValue)
    {
        return (celsciusValue * 9f / 5f) + 32f;
    }
}

#region ReadOnly Attribute

// Attribut à mettre sur n'importe quelle variable pour la rendre non modifiable dans l'Inspector
public class ReadOnlyAttribute : PropertyAttribute { }

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
public class ReadOnlyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        GUI.enabled = false; // désactive le champ
        EditorGUI.PropertyField(position, property, label, true);
        GUI.enabled = true;  // réactive pour les autres champs
    }
}
#endif

#endregion
