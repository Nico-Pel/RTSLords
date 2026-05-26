using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class BuildStatusWidget : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Build targetBuild;

    [Header("Health")]
    [SerializeField] private GameObject healthRoot;
    [SerializeField] private Image healthFill;
    [SerializeField] private bool hideHealthAtFull = true;

    [Header("Production")]
    [SerializeField] private GameObject productionRoot;
    [SerializeField] private Image productionFill;
    [SerializeField] private bool hideProductionWhenIdle = true;

    [Header("Queue")]
    [SerializeField] private Transform queueRoot;
    [SerializeField] private BuildProductionQueueIcon queueIconPrefab;
    [SerializeField] private int maxVisibleQueueItems = 10;
    [SerializeField] private float queueSpacing = 6f;
    [SerializeField] private float queueLeftPadding = 28f;

    private readonly List<BuildProductionQueueIcon> _queueIcons = new List<BuildProductionQueueIcon>();
    private Vector3 _defaultLocalEulerAngles;

    private void Awake()
    {
        if (targetBuild == null)
        {
            targetBuild = GetComponentInParent<Build>();
        }

        _defaultLocalEulerAngles = transform.localEulerAngles;
    }

    private void LateUpdate()
    {
        if (targetBuild == null)
        {
            return;
        }

        UpdateTeamPresentation();
        UpdateHealth();
        UpdateProduction();
    }

    private void UpdateHealth()
    {
        if (healthRoot == null || healthFill == null || targetBuild.Hitbox == null || targetBuild.Hitbox.unitStats == null)
        {
            return;
        }

        int maxHp = Mathf.Max(1, targetBuild.Hitbox.unitStats.health);
        float health01 = Mathf.Clamp01(targetBuild.Hitbox.CurrentHp / (float)maxHp);
        healthFill.fillAmount = health01;
        healthRoot.SetActive(!hideHealthAtFull || health01 < 0.999f);
    }

    private void UpdateProduction()
    {
        if (IsEnemyBuild())
        {
            if (productionRoot != null)
            {
                productionRoot.SetActive(false);
            }

            SetQueueIconCount(0);
            if (productionFill != null)
            {
                productionFill.fillAmount = 0f;
            }

            return;
        }

        IBuildProductionSource productionSource = targetBuild as IBuildProductionSource;
        bool hasProduction = productionSource != null && productionSource.GetProductionPreviewCount() > 0;

        if (productionRoot != null)
        {
            productionRoot.SetActive(!hideProductionWhenIdle || hasProduction);
        }

        if (!hasProduction)
        {
            SetQueueIconCount(0);
            if (productionFill != null)
            {
                productionFill.fillAmount = 0f;
            }

            return;
        }

        if (productionFill != null)
        {
            productionFill.fillAmount = productionSource.ProductionProgress01;
        }

        int previewCount = Mathf.Min(maxVisibleQueueItems, productionSource.GetProductionPreviewCount());
        SetQueueIconCount(previewCount);
        for (int i = 0; i < previewCount; i++)
        {
            _queueIcons[i].Setup(
                productionSource.GetProductionPreviewSprite(i),
                productionSource.GetProductionPreviewVisualType(i),
                i == 0 && productionSource.HasProductionInProgress);
        }

        LayoutQueueIcons(previewCount);
    }

    private void SetQueueIconCount(int targetCount)
    {
        if (queueRoot == null || queueIconPrefab == null)
        {
            return;
        }

        while (_queueIcons.Count < targetCount)
        {
            BuildProductionQueueIcon iconInstance = Instantiate(queueIconPrefab, queueRoot);
            _queueIcons.Add(iconInstance);
        }

        for (int i = 0; i < _queueIcons.Count; i++)
        {
            bool shouldBeActive = i < targetCount;
            if (_queueIcons[i] != null)
            {
                _queueIcons[i].gameObject.SetActive(shouldBeActive);
            }
        }
    }

    private void LayoutQueueIcons(int iconCount)
    {
        RectTransform queueRect = queueRoot as RectTransform;
        if (queueRect == null)
        {
            return;
        }

        float startX = queueLeftPadding;
        float availableWidth = Mathf.Max(0f, queueRect.rect.width - queueLeftPadding);
        float spacing = queueSpacing;
        if (iconCount > 1 && _queueIcons.Count > 0 && _queueIcons[0] != null)
        {
            RectTransform firstIconRect = _queueIcons[0].transform as RectTransform;
            float iconWidth = firstIconRect == null ? 0f : ResolveIconWidth(firstIconRect);
            float requiredWidth = (iconWidth * iconCount) + (queueSpacing * (iconCount - 1));
            if (requiredWidth > availableWidth && iconWidth > 0f)
            {
                spacing = (availableWidth - (iconWidth * iconCount)) / (iconCount - 1);
            }
        }

        float currentX = startX;
        for (int i = 0; i < iconCount; i++)
        {
            if (_queueIcons[i] == null)
            {
                continue;
            }

            RectTransform iconRect = _queueIcons[i].transform as RectTransform;
            if (iconRect == null)
            {
                continue;
            }

            float iconWidth = ResolveIconWidth(iconRect);
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(currentX, 0f);
            currentX += iconWidth + spacing;
        }
    }

    private float ResolveIconWidth(RectTransform iconRect)
    {
        return iconRect.rect.width * iconRect.localScale.x;
    }

    private void UpdateTeamPresentation()
    {
        Vector3 targetEulerAngles = _defaultLocalEulerAngles;
        if (IsEnemyBuild())
        {
            targetEulerAngles.y = Mathf.Repeat(_defaultLocalEulerAngles.y + 180f, 360f);
        }

        transform.localEulerAngles = targetEulerAngles;
    }

    private bool IsEnemyBuild()
    {
        TeamManager localPlayerTeam = TeamManager.GetLocalPlayerTeam();
        return targetBuild != null &&
               targetBuild.Team != null &&
               localPlayerTeam != null &&
               targetBuild.Team != localPlayerTeam;
    }
}
