using UnityEngine;

public enum BuildProductionVisualType
{
    Unit,
    Hero,
    Building,
    Upgrade
}

public interface IBuildProductionSource
{
    bool HasProductionInProgress { get; }
    float ProductionProgress01 { get; }
    int GetProductionPreviewCount();
    Sprite GetProductionPreviewSprite(int index);
    BuildProductionVisualType GetProductionPreviewVisualType(int index);
}
