using UnityEngine;
using UnityEngine.UI;

public class BuildProductionQueueIcon : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private Image stateTint;
    [SerializeField] private Color unitColor = new Color(0f, 0.45f, 1f, 1f);
    [SerializeField] private Color heroColor = new Color(0.33f, 0.82f, 1f, 1f);
    [SerializeField] private Color buildingColor = new Color(0.53f, 0.1f, 1f, 1f);
    [SerializeField] private Color upgradeColor = new Color(1f, 0.56f, 0.08f, 1f);
    [SerializeField] private float queuedBrightnessMultiplier = 0.7f;

    private void Awake()
    {
        if (iconImage == null)
        {
            iconImage = GetComponentInChildren<Image>(true);
        }
    }

    public void Setup(Sprite sprite, BuildProductionVisualType visualType, bool isCurrentItem)
    {
        if (iconImage != null)
        {
            iconImage.sprite = sprite;
            iconImage.enabled = sprite != null;
        }

        if (stateTint != null)
        {
            Color targetColor = ResolveColor(visualType);
            if (!isCurrentItem)
            {
                targetColor.r *= queuedBrightnessMultiplier;
                targetColor.g *= queuedBrightnessMultiplier;
                targetColor.b *= queuedBrightnessMultiplier;
            }

            stateTint.color = targetColor;
        }
    }

    private Color ResolveColor(BuildProductionVisualType visualType)
    {
        switch (visualType)
        {
            case BuildProductionVisualType.Hero:
                return heroColor;
            case BuildProductionVisualType.Building:
                return buildingColor;
            case BuildProductionVisualType.Upgrade:
                return upgradeColor;
            default:
                return unitColor;
        }
    }
}
