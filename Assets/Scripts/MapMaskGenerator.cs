using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(-1000)]
public class MapMaskGenerator : MonoBehaviour
{
    public const string DefaultMaskAssetPath = "Assets/Textures/TexturesMap/MapTest.png";
    private const string DefaultGrassTexturePath = "Assets/Textures/TexturesMap/Grass.png";
    private const string DefaultDirtTexturePath = "Assets/Textures/TexturesMap/Dirt.png";
    private const string DefaultCliffTexturePath = "Assets/Textures/TexturesMap/Cliff.png";
    private const string DefaultGrassMaterialPath = "Assets/Materials/Grass.mat";
    private const string DefaultCityPrefabPath = "Assets/Prefabs/BuildPrefabs/Building-City.prefab";
    private const string DefaultBuildZonePrefabPath = "Assets/Prefabs/BuildZone.prefab";
    private const string DefaultTeamPrefabPath = "Assets/Prefabs/TeamPrefab.prefab";
    private const string DefaultTreePrefabsFolder = "Assets/Prefabs/Trees";
    private const string DefaultWolfPrefabPath = "Assets/Prefabs/UnitPrefabs/0Wolf.prefab";
    private const string DefaultRockPrefabsFolder = "Assets/Prefabs/Rocks";
    private const string DefaultFlowersSpritePath = "Assets/Textures/Flowers.png";
    private const string GeneratedRootName = "GeneratedMap";
    private static readonly Color HumanTeamColor = new Color(0f, 0.35255194f, 1f, 1f);
    private static readonly Color AiTeamColor = new Color(1f, 0f, 0f, 1f);

    private enum MaskRole
    {
        White,
        Black,
        Green,
        DenseForest,
        Yellow,
        Blue,
        Red,
        Cyan
    }

    public enum OrientationVariant
    {
        Random,
        Identity,
        Rotate180,
        MirrorX
    }

    [Header("Source")]
    public Texture2D maskTexture;
    public OrientationVariant orientation = OrientationVariant.Random;
    public bool generateOnAwake = true;
    public bool clearExistingCities = true;
    public bool clearExistingBuildZones = true;

    [Header("Terrain")]
    public float worldWidth = 52f;
    public float worldDepth = 52f;
    public float mapSizeMultiplier = 1.5f;
    public int terrainResolution = 120;
    public float plateauHeight = 3.8f;
    public float visualCliffHeightMultiplier = 0.5f;
    public float slopeWidth = 2.4f;
    public float outerBlackBorderWorldSize = 10f;
    public float terrainMeshExtensionWorld = 5f;
    public float plateauNoiseHeight = 0.22f;
    public float slopeNoiseStrength = 0.4f;
    public int blockerResolution = 56;
    public int groundTextureResolution = 768;
    public float textureTiling = 10f;
    public float groundBlendWorldDistance = 1.45f;

    [Header("Terrain Assets")]
    public Material groundMaterialTemplate;
    public Texture2D grassTexture;
    public Texture2D dirtTexture;
    public Texture2D cliffTexture;

    [Header("Gameplay Prefabs")]
    public TeamManager teamPrefab;
    public City cityPrefab;
    public BuildZone buildZonePrefab;
    public GameObject wolfPrefab;
    public GameObject[] treePrefabs;
    public GameObject[] rockPrefabs;
    public Sprite[] flowerSprites;

    [Header("Scene Teams")]
    public TeamManager playerTeam;
    public TeamManager enemyTeam;

    [Header("Tree Spawn")]
    public float treeSpacing = 0.7f;
    public float treeJitter = 0.7f;
    public float treeSpawnChance = 1f;
    public float treeExclusionRadius = 2.2f;
    public float cityTreeExclusionRadius = 10f;
    public float buildZoneTreeExclusionRadius = 6.25f;
    public float minTreeDistance = 0.5f;
    public float minCliffDistance = 0.5f;
    public int maxTreeCount = 300;
    public int guaranteedCityForestTreeCount = 70;
    public float guaranteedCityForestMinDistanceFromCity = 5f;
    public float guaranteedCityForestMaxDistanceFromCity = 12f;
    public float guaranteedCityForestSpreadAngle = 80f;
    public float guaranteedCityForestCliffClearRadius = 4f;
    public int neighborSpawnChecksPerTree = 2;
    public float neighborSpawnRadiusMin = 0.5f;
    public float neighborSpawnRadiusMax = 1.35f;
    public float treeClusterNoiseScale = 5.5f;
    public float treeClusterThreshold = 0f;
    public int treeSpawnAttemptsPerCell = 8;

    [Header("Decoration Spawn")]
    public int rockCount = 100;
    public int flowerCount = 100;
    public float decorationMinDistance = 0.75f;
    public float decorationTreeExclusionRadius = 0.8f;
    public float decorationWolfExclusionRadius = 1.2f;
    public float decorationBuildZoneExclusionRadius = 4f;
    public float minBuildZoneDistanceFromCity = 7f;
    public float minBuildZoneDistanceFromOtherBuildZones = 7f;
    public float buildZoneCliffSafetyDistance = 8f;
    public float buildZoneObstacleCleanupDistance = 3f;
    public float rockBuildZoneCleanupDistance = 3f;
    public float decorationCityExclusionRadius = 4.5f;
    public float heroSpawnExclusionRadius = 4f;
    public float decorationCliffDistance = 0.5f;
    public int decorationSpawnAttemptBudget = 12000;
    public Vector2 flowerScaleRange = new Vector2(0.8f, 1.15f);

    [Header("Randomness")]
    public bool randomizeSeed = true;
    public int seed = 12345;
    public float maskWarpStrength = 0.012f;
    public float maskWarpScale = 3.6f;
    public float secondaryWarpStrength = 0.006f;
    public float secondaryWarpScale = 9.5f;

    private MaskRole[] _roles;
    private int _maskWidth;
    private int _maskHeight;
    private OrientationVariant _resolvedOrientation;
    private System.Random _random;
    private Transform _generatedRoot;
    private List<GuaranteedForestZone> _guaranteedForestZones = new List<GuaranteedForestZone>();
    private List<Vector3> _adjustedBuildZonePositions = new List<Vector3>();
    private Vector3 _playerBaseBeforeClear;
    private Vector3 _enemyBaseBeforeClear;
    private Color _playerTeamColorBeforeClear = HumanTeamColor;
    private Color _enemyTeamColorBeforeClear = AiTeamColor;

    private void Reset()
    {
#if UNITY_EDITOR
        ConfigureDefaultsFromProjectAssets();
        AssignSceneTeams(FindObjectsOfType<TeamManager>(true));
#endif
    }

    private void Awake()
    {
        if (!generateOnAwake)
        {
            return;
        }

        Generate();
    }

    [ContextMenu("Generate Map")]
    public void Generate()
    {
        if (!PrepareGeneration())
        {
            return;
        }

        ClearPreviousGeneratedMap();
        CacheMaskRoles();

        List<Vector2> cityPixels = ExtractMarkerCentroids(MaskRole.Blue, 1);
        List<Vector2> buildZonePixels = ExtractMarkerCentroids(MaskRole.Red, 1);
        List<Vector2> wolfPixels = ExtractMarkerCentroids(MaskRole.Cyan, 6);
        List<Vector3> cityWorldPositions = ConvertPixelsToWorld(cityPixels);
        List<Vector3> buildZoneWorldPositions = ConvertPixelsToWorld(buildZonePixels);
        List<Vector3> wolfWorldPositions = ConvertPixelsToWorld(wolfPixels);
        AdjustBuildZonePositionsNearCities(buildZoneWorldPositions, cityWorldPositions);
        _adjustedBuildZonePositions = new List<Vector3>(buildZoneWorldPositions);
        SnapBuildZoneHeightsToFinalTerrain(buildZoneWorldPositions);
        _adjustedBuildZonePositions = new List<Vector3>(buildZoneWorldPositions);
        _guaranteedForestZones = CreateGuaranteedForestZones(cityWorldPositions);

        _generatedRoot = new GameObject(GeneratedRootName).transform;
        _generatedRoot.SetParent(transform, false);

        BuildTerrain();
        BuildObstacleBlockers();
        SpawnBuildZones(buildZoneWorldPositions);
        SpawnTrees(cityWorldPositions, buildZoneWorldPositions);
        PositionTeamsAndCities(cityWorldPositions);
        SpawnWolves(wolfWorldPositions);
        SpawnDecorations();
        CleaningTextures();
    }

    [ContextMenu("Clear Generated Map")]
    public void ClearGeneratedMap()
    {
        if (playerTeam == null || enemyTeam == null)
        {
            AssignSceneTeams(FindObjectsOfType<TeamManager>(true));
        }

        _playerBaseBeforeClear = playerTeam != null ? playerTeam.BasePosition : Vector3.zero;
        _enemyBaseBeforeClear = enemyTeam != null ? enemyTeam.BasePosition : Vector3.zero;
        _playerTeamColorBeforeClear = HumanTeamColor;
        _enemyTeamColorBeforeClear = AiTeamColor;
        ClearPreviousGeneratedMap();
    }

#if UNITY_EDITOR
    public void ConfigureDefaultsFromProjectAssets()
    {
        maskTexture = maskTexture != null
            ? maskTexture
            : AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultMaskAssetPath);
        grassTexture = grassTexture != null
            ? grassTexture
            : AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultGrassTexturePath);
        dirtTexture = dirtTexture != null
            ? dirtTexture
            : AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultDirtTexturePath);
        cliffTexture = cliffTexture != null
            ? cliffTexture
            : AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultCliffTexturePath);
        groundMaterialTemplate = groundMaterialTemplate != null
            ? groundMaterialTemplate
            : AssetDatabase.LoadAssetAtPath<Material>(DefaultGrassMaterialPath);
        teamPrefab = teamPrefab != null
            ? teamPrefab
            : AssetDatabase.LoadAssetAtPath<TeamManager>(DefaultTeamPrefabPath);
        cityPrefab = cityPrefab != null
            ? cityPrefab
            : AssetDatabase.LoadAssetAtPath<City>(DefaultCityPrefabPath);
        buildZonePrefab = buildZonePrefab != null
            ? buildZonePrefab
            : AssetDatabase.LoadAssetAtPath<BuildZone>(DefaultBuildZonePrefabPath);
        wolfPrefab = wolfPrefab != null
            ? wolfPrefab
            : AssetDatabase.LoadAssetAtPath<GameObject>(DefaultWolfPrefabPath);

        if (treePrefabs == null || treePrefabs.Length == 0)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { DefaultTreePrefabsFolder });
            List<GameObject> prefabs = new List<GameObject>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    prefabs.Add(prefab);
                }
            }

            treePrefabs = prefabs.ToArray();
        }

        if (rockPrefabs == null || rockPrefabs.Length == 0)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { DefaultRockPrefabsFolder });
            List<GameObject> prefabs = new List<GameObject>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    prefabs.Add(prefab);
                }
            }

            rockPrefabs = prefabs.ToArray();
        }

        if (flowerSprites == null || flowerSprites.Length == 0)
        {
            UnityEngine.Object[] loadedAssets = AssetDatabase.LoadAllAssetsAtPath(DefaultFlowersSpritePath);
            List<Sprite> sprites = new List<Sprite>();
            for (int i = 0; i < loadedAssets.Length; i++)
            {
                if (loadedAssets[i] is Sprite sprite)
                {
                    sprites.Add(sprite);
                }
            }

            flowerSprites = sprites.ToArray();
        }
    }
#endif

    public void AssignSceneTeams(TeamManager[] teams)
    {
        if (teams == null || teams.Length == 0)
        {
            return;
        }

        for (int i = 0; i < teams.Length; i++)
        {
            TeamManager team = teams[i];
            if (team == null)
            {
                continue;
            }

            if (team.player != null)
            {
                playerTeam = team;
            }
            else if (team.playerAI != null)
            {
                enemyTeam = team;
            }
        }

        if (playerTeam == null && teams.Length > 0)
        {
            playerTeam = teams[0];
        }

        if (enemyTeam == null)
        {
            for (int i = 0; i < teams.Length; i++)
            {
                if (teams[i] != null && teams[i] != playerTeam)
                {
                    enemyTeam = teams[i];
                    break;
                }
            }
        }
    }

    private bool PrepareGeneration()
    {
        if (maskTexture == null || buildZonePrefab == null)
        {
            Debug.LogWarning("MapMaskGenerator is missing required references.");
            return false;
        }

        if (teamPrefab == null && cityPrefab == null)
        {
            Debug.LogWarning("MapMaskGenerator needs either a TeamPrefab or a City prefab.");
            return false;
        }

#if UNITY_EDITOR
        EnsureMaskTextureReadableInEditor();
        EnsureTextureReadableInEditor(grassTexture);
        EnsureTextureReadableInEditor(dirtTexture);
        EnsureTextureReadableInEditor(cliffTexture);
#endif

        if (playerTeam == null || enemyTeam == null)
        {
            AssignSceneTeams(FindObjectsOfType<TeamManager>(true));
        }

        if (playerTeam == null || enemyTeam == null)
        {
            Debug.LogWarning("MapMaskGenerator could not find two teams in the scene.");
            return false;
        }

        _maskWidth = Mathf.Max(1, maskTexture.width);
        _maskHeight = Mathf.Max(1, maskTexture.height);
        int resolvedSeed = randomizeSeed ? Environment.TickCount : seed;
        _random = new System.Random(resolvedSeed);
        _resolvedOrientation = ResolveOrientation();
        _playerBaseBeforeClear = playerTeam != null ? playerTeam.BasePosition : Vector3.zero;
        _enemyBaseBeforeClear = enemyTeam != null ? enemyTeam.BasePosition : Vector3.zero;
        _playerTeamColorBeforeClear = HumanTeamColor;
        _enemyTeamColorBeforeClear = AiTeamColor;
        return true;
    }

    private OrientationVariant ResolveOrientation()
    {
        if (orientation != OrientationVariant.Random)
        {
            return orientation;
        }

        int roll = _random.Next(0, 3);
        switch (roll)
        {
            case 1:
                return OrientationVariant.Rotate180;
            case 2:
                return OrientationVariant.MirrorX;
            default:
                return OrientationVariant.Identity;
        }
    }

    private void ClearPreviousGeneratedMap()
    {
        Transform existingRoot = transform.Find(GeneratedRootName);
        if (existingRoot != null)
        {
            DestroyNow(existingRoot.gameObject);
        }

        if (clearExistingBuildZones)
        {
            BuildZone[] existingZones = FindObjectsOfType<BuildZone>(true);
            for (int i = 0; i < existingZones.Length; i++)
            {
                if (existingZones[i] != null)
                {
                    DestroyNow(existingZones[i].gameObject);
                }
            }
        }

        if (clearExistingCities)
        {
            DestroyTeamCity(playerTeam);
            DestroyTeamCity(enemyTeam);
        }

        if (teamPrefab != null)
        {
            TeamManager[] existingTeams = FindObjectsOfType<TeamManager>(true);
            for (int i = 0; i < existingTeams.Length; i++)
            {
                if (existingTeams[i] != null)
                {
                    DestroyNow(existingTeams[i].gameObject);
                }
            }

            playerTeam = null;
            enemyTeam = null;
        }
    }

    private void DestroyTeamCity(TeamManager team)
    {
        if (team == null || team.city == null)
        {
            return;
        }

        DestroyNow(team.city.gameObject);
        team.city = null;
    }

    private void BuildTerrain()
    {
        GameObject terrainObject = new GameObject("Terrain");
        terrainObject.transform.SetParent(_generatedRoot, false);

        MeshFilter meshFilter = terrainObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = terrainObject.AddComponent<MeshRenderer>();
        meshFilter.sharedMesh = CreateTerrainMesh();
        meshRenderer.sharedMaterial = CreateTerrainMaterial();
    }

    private Mesh CreateTerrainMesh()
    {
        float effectiveWorldWidth = GetEffectiveWorldWidth();
        float effectiveWorldDepth = GetEffectiveWorldDepth();
        float expandedWorldWidth = effectiveWorldWidth + (Mathf.Max(0f, terrainMeshExtensionWorld) * 2f);
        float expandedWorldDepth = effectiveWorldDepth + (Mathf.Max(0f, terrainMeshExtensionWorld) * 2f);
        int resolution = Mathf.Max(24, terrainResolution);
        int columns = resolution + 1;
        int rows = resolution + 1;
        Vector3[] vertices = new Vector3[columns * rows];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[resolution * resolution * 6];

        for (int z = 0; z < rows; z++)
        {
            float v = z / (float)resolution;
            for (int x = 0; x < columns; x++)
            {
                float u = x / (float)resolution;
                Vector2 sampleUv = GetTerrainMaskUvFromExpandedSurface(u, v);
                float localX = (u - 0.5f) * expandedWorldWidth;
                float localZ = (v - 0.5f) * expandedWorldDepth;
                float localY = SampleHeight(sampleUv);

                int index = x + (z * columns);
                vertices[index] = new Vector3(localX, localY, localZ);
                uvs[index] = new Vector2(u, v);
            }
        }

        int triangleIndex = 0;
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int root = x + (z * columns);
                int nextRow = root + columns;

                triangles[triangleIndex++] = root;
                triangles[triangleIndex++] = nextRow;
                triangles[triangleIndex++] = root + 1;

                triangles[triangleIndex++] = root + 1;
                triangles[triangleIndex++] = nextRow;
                triangles[triangleIndex++] = nextRow + 1;
            }
        }

        Mesh mesh = new Mesh
        {
            name = "GeneratedMapTerrain"
        };
        mesh.indexFormat = vertices.Length > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Material CreateTerrainMaterial()
    {
        Material material = groundMaterialTemplate != null
            ? new Material(groundMaterialTemplate)
            : new Material(Shader.Find("Standard"));

        material.mainTextureScale = Vector2.one;
        if (material.HasProperty("_Glossiness"))
        {
            material.SetFloat("_Glossiness", 0f);
        }

        return material;
    }

    private void CleaningTextures()
    {
        if (_generatedRoot == null)
        {
            return;
        }

        Transform terrainTransform = _generatedRoot.Find("Terrain");
        if (terrainTransform == null)
        {
            return;
        }

        MeshRenderer meshRenderer = terrainTransform.GetComponent<MeshRenderer>();
        if (meshRenderer == null || meshRenderer.sharedMaterial == null)
        {
            return;
        }

        Texture2D generatedTexture = BuildGroundTexture();
        meshRenderer.sharedMaterial.mainTexture = generatedTexture;
        meshRenderer.sharedMaterial.mainTextureScale = Vector2.one;
    }

    private Texture2D BuildGroundTexture()
    {
        int resolution = Mathf.Clamp(groundTextureResolution, 128, 2048);
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true);
        texture.name = "GeneratedMapGroundTexture";
        texture.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        {
            float v = y / (float)(resolution - 1);
            for (int x = 0; x < resolution; x++)
            {
                float u = x / (float)(resolution - 1);
                Vector2 sampleUv = GetTerrainMaskUvFromExpandedSurface(u, v);
                pixels[x + (y * resolution)] = BuildGroundPixelColor(sampleUv, u, v);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(true, false);
        return texture;
    }

    private Color BuildGroundPixelColor(Vector2 sampleUv, float u, float v)
    {
        bool isGuaranteedForestClearZone = IsInsideGuaranteedForestClearZone(sampleUv);
        bool isBuildZoneCliffSafetyZone = IsInsideBuildZoneCliffSafetyZone(sampleUv);
        bool isOutsideMaskBounds = IsOutsideMaskBounds(sampleUv);
        bool isOuterBlackBand = IsOuterBlackBand(sampleUv);
        MaskRole role = isGuaranteedForestClearZone || isBuildZoneCliffSafetyZone
            ? MaskRole.White
            : isOutsideMaskBounds || isOuterBlackBand
                ? MaskRole.Black
                : GetRoleAtUv(sampleUv);
        float centerHeight = SampleHeight(sampleUv);
        float maxVisualHeight = Mathf.Max(0.01f, plateauHeight * Mathf.Clamp01(visualCliffHeightMultiplier));
        float normalizedHeight = centerHeight / maxVisualHeight;
        float cliffInfluence = 0f;
        if (role != MaskRole.Black && centerHeight > 0.025f && centerHeight < maxVisualHeight - 0.025f)
        {
            float lowerBand = Mathf.InverseLerp(0.03f, 0.18f, normalizedHeight);
            float upperBand = 1f - Mathf.InverseLerp(0.82f, 0.97f, normalizedHeight);
            cliffInfluence = Mathf.Clamp01(Mathf.Min(lowerBand, upperBand));
        }

        float dirtBlendRadiusPixels = Mathf.Max(1f, groundBlendWorldDistance * PixelsPerWorldUnitX());
        float distanceToYellow = FindDistanceToRole(sampleUv, MaskRole.Yellow, Mathf.CeilToInt(dirtBlendRadiusPixels) + 2);
        float dirtInfluence = role == MaskRole.Black || role == MaskRole.Yellow
            ? 1f
            : distanceToYellow < 0f
                ? 0f
                : Mathf.Clamp01(1f - (distanceToYellow / dirtBlendRadiusPixels));

        Color grassColor = SampleTiledTexture(grassTexture != null ? grassTexture : dirtTexture, u, v);
        Color dirtColor = SampleTiledTexture(dirtTexture != null ? dirtTexture : grassTexture, u, v);
        Color baseColor = Color.Lerp(grassColor, dirtColor, dirtInfluence);

        if (cliffTexture != null && cliffInfluence > 0.001f)
        {
            Color cliffColor = SampleTiledTexture(cliffTexture, u, v);
            float easedCliffInfluence = Mathf.SmoothStep(0f, 1f, cliffInfluence);
            baseColor = Color.Lerp(baseColor, cliffColor, easedCliffInfluence);
        }

        return baseColor;
    }

    private Texture2D ResolveLayerTexture(MaskRole role, bool isCliff)
    {
        if (role == MaskRole.Black)
        {
            return dirtTexture != null ? dirtTexture : grassTexture;
        }

        if (isCliff && cliffTexture != null)
        {
            return cliffTexture;
        }

        if (role == MaskRole.Yellow)
        {
            return dirtTexture != null ? dirtTexture : grassTexture;
        }

        return grassTexture != null ? grassTexture : dirtTexture;
    }

    private Color SampleTiledTexture(Texture2D texture, float u, float v)
    {
        if (texture == null)
        {
            return Color.white;
        }

        float noiseU = u + (Mathf.PerlinNoise(u * 7.1f + seed, v * 6.3f + 17f) - 0.5f) * 0.035f;
        float noiseV = v + (Mathf.PerlinNoise(u * 5.2f + 33f, v * 8.4f + seed) - 0.5f) * 0.035f;
        float tiledU = Mathf.Repeat(noiseU * textureTiling, 1f);
        float tiledV = Mathf.Repeat(noiseV * textureTiling, 1f);
        return texture.GetPixelBilinear(tiledU, tiledV);
    }

    private void BuildObstacleBlockers()
    {
        float effectiveWorldWidth = GetEffectiveWorldWidth();
        float effectiveWorldDepth = GetEffectiveWorldDepth();
        GameObject blockersRoot = new GameObject("Blockers");
        blockersRoot.transform.SetParent(_generatedRoot, false);

        int resolution = Mathf.Clamp(blockerResolution, 8, 256);
        float cellWidth = effectiveWorldWidth / resolution;
        float cellDepth = effectiveWorldDepth / resolution;

        for (int z = 0; z < resolution; z++)
        {
            float v = (z + 0.5f) / resolution;
            for (int x = 0; x < resolution; x++)
            {
                float u = (x + 0.5f) / resolution;
                Vector2 sampleUv = GetWarpedMaskUv(new Vector2(u, v));
                if (!IsBlockedTerrainAt(sampleUv))
                {
                    continue;
                }

                GameObject blocker = new GameObject($"Blocker_{x}_{z}");
                blocker.transform.SetParent(blockersRoot.transform, false);
                float blockerHeight = Mathf.Max(plateauHeight + 1f, SampleTerrainHeight(sampleUv, 1f) + 1f);
                blocker.transform.localPosition = new Vector3(
                    (u - 0.5f) * effectiveWorldWidth,
                    blockerHeight * 0.5f,
                    (v - 0.5f) * effectiveWorldDepth);

                BoxCollider collider = blocker.AddComponent<BoxCollider>();
                collider.size = new Vector3(cellWidth, blockerHeight, cellDepth);
            }
        }
    }

    private void SpawnBuildZones(List<Vector3> buildZoneWorldPositions)
    {
        if (buildZoneWorldPositions == null || buildZoneWorldPositions.Count == 0)
        {
            return;
        }

        GameObject buildZoneRoot = new GameObject("BuildZones");
        buildZoneRoot.transform.SetParent(_generatedRoot, false);
        for (int i = 0; i < buildZoneWorldPositions.Count; i++)
        {
            Vector3 position = buildZoneWorldPositions[i];
            BuildZone buildZone = Instantiate(buildZonePrefab, position, Quaternion.identity, buildZoneRoot.transform);
            buildZone.name = $"BuildZone ({i + 1})";
        }
    }

    private void SpawnWolves(List<Vector3> wolfWorldPositions)
    {
        if (wolfPrefab == null || wolfWorldPositions == null || wolfWorldPositions.Count == 0)
        {
            return;
        }

        GameObject wolvesRoot = new GameObject("Wolves");
        wolvesRoot.transform.SetParent(_generatedRoot, false);
        for (int i = 0; i < wolfWorldPositions.Count; i++)
        {
            Vector3 spawnPosition = wolfWorldPositions[i];
            Quaternion spawnRotation = Quaternion.Euler(0f, (float)_random.NextDouble() * 360f, 0f);
            GameObject wolfObject = Instantiate(wolfPrefab, spawnPosition, spawnRotation, wolvesRoot.transform);
            wolfObject.name = $"Wolf ({i + 1})";

            WolfUnit wolfUnit = wolfObject.GetComponent<WolfUnit>();
            if (wolfUnit == null)
            {
                wolfUnit = wolfObject.AddComponent<WolfUnit>();
            }

            wolfUnit.InitializeAtSpawn(spawnPosition, spawnRotation);
        }
    }

    private void SpawnDecorations()
    {
        if (_generatedRoot == null)
        {
            return;
        }

        List<Vector3> occupiedPositions = CollectOccupiedDecorationPositions();
        GameObject decorationsRoot = new GameObject("Decorations");
        decorationsRoot.transform.SetParent(_generatedRoot, false);

        Transform rocksRoot = new GameObject("Rocks").transform;
        rocksRoot.SetParent(decorationsRoot.transform, false);
        SpawnRockDecorations(rocksRoot, occupiedPositions);
        PruneRocksAroundBuildZones(rocksRoot);

        Transform flowersRoot = new GameObject("Flowers").transform;
        flowersRoot.SetParent(decorationsRoot.transform, false);
        SpawnFlowerDecorations(flowersRoot, occupiedPositions);

        if (buildZoneObstacleCleanupDistance > 0f)
        {
            ClearPropsAroundBuildZones(buildZoneObstacleCleanupDistance);
        }
    }

    private void SpawnRockDecorations(Transform rocksRoot, List<Vector3> occupiedPositions)
    {
        if (rockPrefabs == null || rockPrefabs.Length == 0 || rockCount <= 0)
        {
            return;
        }

        int spawnedCount = 0;
        int attemptBudget = Mathf.Max(rockCount * 25, decorationSpawnAttemptBudget);
        for (int attempt = 0; attempt < attemptBudget && spawnedCount < rockCount; attempt++)
        {
            Vector3 position = FindRandomDecorationCandidatePosition();
            if (!CanSpawnDecorationAt(position, occupiedPositions))
            {
                continue;
            }

            GameObject rockPrefab = rockPrefabs[_random.Next(0, rockPrefabs.Length)];
            Quaternion rotation = Quaternion.Euler(0f, (float)_random.NextDouble() * 360f, 0f);
            GameObject rockInstance = Instantiate(rockPrefab, position, rotation, rocksRoot);
            EnsureRockObstacle(rockInstance);
            occupiedPositions.Add(position);
            spawnedCount++;
        }
    }

    private void SpawnFlowerDecorations(Transform flowersRoot, List<Vector3> occupiedPositions)
    {
        if (flowerSprites == null || flowerSprites.Length == 0 || flowerCount <= 0)
        {
            return;
        }

        int spawnedCount = 0;
        int attemptBudget = Mathf.Max(flowerCount * 25, decorationSpawnAttemptBudget);
        for (int attempt = 0; attempt < attemptBudget && spawnedCount < flowerCount; attempt++)
        {
            Vector3 position = FindRandomDecorationCandidatePosition();
            if (!CanSpawnDecorationAt(position, occupiedPositions))
            {
                continue;
            }

            Sprite sprite = flowerSprites[_random.Next(0, flowerSprites.Length)];
            GameObject flowerObject = new GameObject($"Flower ({spawnedCount + 1})");
            flowerObject.transform.SetParent(flowersRoot, false);
            flowerObject.transform.position = position + Vector3.up * 0.02f;
            flowerObject.transform.rotation = Quaternion.Euler(90f, 0f, (float)_random.NextDouble() * 360f);
            float scale = Mathf.Lerp(flowerScaleRange.x, flowerScaleRange.y, (float)_random.NextDouble());
            flowerObject.transform.localScale = Vector3.one * scale;

            SpriteRenderer spriteRenderer = flowerObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.sortingOrder = -5;

            occupiedPositions.Add(position);
            spawnedCount++;
        }
    }

    private void PruneRocksAroundBuildZones(Transform rocksRoot)
    {
        if (rocksRoot == null || rockBuildZoneCleanupDistance <= 0f || _generatedRoot == null)
        {
            return;
        }

        Transform buildZonesRoot = _generatedRoot.Find("BuildZones");
        if (buildZonesRoot == null || buildZonesRoot.childCount == 0)
        {
            return;
        }

        float sqrDistance = rockBuildZoneCleanupDistance * rockBuildZoneCleanupDistance;
        List<GameObject> rocksToDestroy = new List<GameObject>();
        for (int i = 0; i < rocksRoot.childCount; i++)
        {
            Transform rock = rocksRoot.GetChild(i);
            Vector3 rockPosition = rock.position;
            rockPosition.y = 0f;

            for (int zoneIndex = 0; zoneIndex < buildZonesRoot.childCount; zoneIndex++)
            {
                Vector3 zonePosition = buildZonesRoot.GetChild(zoneIndex).position;
                zonePosition.y = 0f;
                if ((rockPosition - zonePosition).sqrMagnitude <= sqrDistance)
                {
                    rocksToDestroy.Add(rock.gameObject);
                    break;
                }
            }
        }

        for (int i = 0; i < rocksToDestroy.Count; i++)
        {
            DestroyNow(rocksToDestroy[i]);
        }
    }

    private void ClearPropsAroundBuildZones(float cleanupDistance)
    {
        if (_generatedRoot == null || cleanupDistance <= 0f)
        {
            return;
        }

        Transform buildZonesRoot = _generatedRoot.Find("BuildZones");
        if (buildZonesRoot == null || buildZonesRoot.childCount == 0)
        {
            return;
        }

        List<Vector3> buildZonePositions = new List<Vector3>(buildZonesRoot.childCount);
        for (int i = 0; i < buildZonesRoot.childCount; i++)
        {
            buildZonePositions.Add(buildZonesRoot.GetChild(i).position);
        }

        PruneTreesAroundReservedZones(buildZonePositions, cleanupDistance);

        Transform rocksRoot = _generatedRoot.Find("Decorations/Rocks");
        if (rocksRoot != null)
        {
            PruneRocksAroundPositions(rocksRoot, buildZonePositions, cleanupDistance);
        }
    }

    private void PruneRocksAroundPositions(Transform rocksRoot, List<Vector3> positions, float cleanupDistance)
    {
        if (rocksRoot == null || positions == null || positions.Count == 0 || cleanupDistance <= 0f)
        {
            return;
        }

        float sqrDistance = cleanupDistance * cleanupDistance;
        List<GameObject> rocksToDestroy = new List<GameObject>();
        for (int i = 0; i < rocksRoot.childCount; i++)
        {
            Transform rock = rocksRoot.GetChild(i);
            Vector3 rockPosition = rock.position;
            rockPosition.y = 0f;

            for (int j = 0; j < positions.Count; j++)
            {
                Vector3 zonePosition = positions[j];
                zonePosition.y = 0f;
                if ((rockPosition - zonePosition).sqrMagnitude <= sqrDistance)
                {
                    rocksToDestroy.Add(rock.gameObject);
                    break;
                }
            }
        }

        for (int i = 0; i < rocksToDestroy.Count; i++)
        {
            DestroyNow(rocksToDestroy[i]);
        }
    }

    private void SpawnTrees(
        List<Vector3> cityWorldPositions,
        List<Vector3> buildZoneWorldPositions)
    {
        if (treePrefabs == null || treePrefabs.Length == 0)
        {
            return;
        }

        GameObject treeRoot = new GameObject("Trees");
        treeRoot.transform.SetParent(_generatedRoot, false);

        List<ReservedSpawnZone> blockedZones = new List<ReservedSpawnZone>();
        List<ReservedSpawnZone> denseForestBlockedZones = new List<ReservedSpawnZone>();
        if (cityWorldPositions != null)
        {
            for (int i = 0; i < cityWorldPositions.Count; i++)
            {
                blockedZones.Add(new ReservedSpawnZone(cityWorldPositions[i], cityTreeExclusionRadius));
                denseForestBlockedZones.Add(new ReservedSpawnZone(cityWorldPositions[i], Mathf.Max(cityTreeExclusionRadius, guaranteedCityForestMinDistanceFromCity)));
            }
        }

        if (buildZoneWorldPositions != null)
        {
            for (int i = 0; i < buildZoneWorldPositions.Count; i++)
            {
                blockedZones.Add(new ReservedSpawnZone(buildZoneWorldPositions[i], buildZoneTreeExclusionRadius));
                denseForestBlockedZones.Add(new ReservedSpawnZone(buildZoneWorldPositions[i], 3f));
            }
        }

        float effectiveWorldWidth = GetEffectiveWorldWidth();
        float effectiveWorldDepth = GetEffectiveWorldDepth();
        float halfWidth = effectiveWorldWidth * 0.5f;
        float halfDepth = effectiveWorldDepth * 0.5f;
        float spacing = Mathf.Max(0.5f, treeSpacing);
        List<Vector3> spawnedTreePositions = new List<Vector3>();
        Queue<Vector3> frontier = new Queue<Vector3>();
        int targetTreeCount = Mathf.Max(0, maxTreeCount);

        SpawnGuaranteedCityForestTrees(treeRoot.transform, denseForestBlockedZones, spawnedTreePositions, targetTreeCount);
        if (spawnedTreePositions.Count >= targetTreeCount)
        {
            return;
        }

        for (float localZ = -halfDepth; localZ <= halfDepth; localZ += spacing)
        {
            if (spawnedTreePositions.Count >= targetTreeCount)
            {
                break;
            }

            for (float localX = -halfWidth; localX <= halfWidth; localX += spacing)
            {
                if (spawnedTreePositions.Count >= targetTreeCount)
                {
                    break;
                }

                for (int attempt = 0; attempt < Mathf.Max(1, treeSpawnAttemptsPerCell); attempt++)
                {
                    if (spawnedTreePositions.Count >= targetTreeCount)
                    {
                        break;
                    }

                    float jitterX = ((float)_random.NextDouble() - 0.5f) * treeJitter * spacing;
                    float jitterZ = ((float)_random.NextDouble() - 0.5f) * treeJitter * spacing;
                    Vector3 localPosition = new Vector3(localX + jitterX, 0f, localZ + jitterZ);
                    Vector3 worldPosition = transform.TransformPoint(localPosition);
                    if (!CanSpawnTreeAt(worldPosition, blockedZones, spawnedTreePositions))
                    {
                        continue;
                    }

                    SpawnTreeInstance(worldPosition, treeRoot.transform, spawnedTreePositions, frontier);
                }
            }
        }

        while (spawnedTreePositions.Count < targetTreeCount && frontier.Count > 0)
        {
            Vector3 origin = frontier.Dequeue();
            int attempts = Mathf.Max(1, neighborSpawnChecksPerTree);
            for (int i = 0; i < attempts && spawnedTreePositions.Count < targetTreeCount; i++)
            {
                Vector3 neighborPosition = FindNeighborTreePosition(origin);
                if (!CanSpawnTreeAt(neighborPosition, blockedZones, spawnedTreePositions, false))
                {
                    continue;
                }

                SpawnTreeInstance(neighborPosition, treeRoot.transform, spawnedTreePositions, frontier);
            }
        }

        int safetyBudget = Mathf.Max(targetTreeCount * 80, 4000);
        int safetyAttempts = 0;
        while (spawnedTreePositions.Count < targetTreeCount && spawnedTreePositions.Count > 0 && safetyAttempts < safetyBudget)
        {
            safetyAttempts++;

            Vector3 origin = spawnedTreePositions[_random.Next(0, spawnedTreePositions.Count)];
            Vector3 neighborPosition = FindNeighborTreePosition(origin);
            if (CanSpawnTreeAt(neighborPosition, blockedZones, spawnedTreePositions, false))
            {
                SpawnTreeInstance(neighborPosition, treeRoot.transform, spawnedTreePositions, frontier);
                continue;
            }

            Vector3 randomGreenPosition = FindRandomTreeCandidatePosition();
            if (CanSpawnTreeAt(randomGreenPosition, blockedZones, spawnedTreePositions, false))
            {
                SpawnTreeInstance(randomGreenPosition, treeRoot.transform, spawnedTreePositions, frontier);
            }
        }

        if (cityWorldPositions != null && cityWorldPositions.Count > 0)
        {
            PruneTreesAroundReservedZones(cityWorldPositions, cityTreeExclusionRadius);
        }
    }

    private void SpawnGuaranteedCityForestTrees(
        Transform treeRoot,
        List<ReservedSpawnZone> blockedZones,
        List<Vector3> spawnedTreePositions,
        int totalTreeBudget)
    {
        if (_guaranteedForestZones == null || _guaranteedForestZones.Count == 0)
        {
            return;
        }

        int treesPerZone = Mathf.Max(0, guaranteedCityForestTreeCount);
        if (treesPerZone <= 0)
        {
            return;
        }

        Queue<Vector3> forestFrontier = new Queue<Vector3>();
        for (int zoneIndex = 0; zoneIndex < _guaranteedForestZones.Count; zoneIndex++)
        {
            if (spawnedTreePositions.Count >= totalTreeBudget)
            {
                break;
            }

            GuaranteedForestZone zone = _guaranteedForestZones[zoneIndex];
            int spawnedInRegion = 0;
            int remainingBudget = Mathf.Max(0, totalTreeBudget - spawnedTreePositions.Count);
            int targetForRegion = Mathf.Min(treesPerZone, remainingBudget);
            if (targetForRegion <= 0)
            {
                break;
            }

            int attemptBudget = Mathf.Max(treesPerZone * 220, 8000);
            for (int attempt = 0; attempt < attemptBudget && spawnedInRegion < targetForRegion; attempt++)
            {
                Vector3 candidate = FindGuaranteedForestCandidatePosition(zone, forestFrontier, spawnedInRegion > 0);
                if (!CanSpawnGuaranteedForestTreeAt(candidate, zone, blockedZones, spawnedTreePositions))
                {
                    continue;
                }

                SpawnTreeInstance(candidate, treeRoot, spawnedTreePositions, forestFrontier);
                spawnedInRegion++;
            }

            if (spawnedInRegion < targetForRegion)
            {
                Debug.LogWarning($"Guaranteed city forest zone {zoneIndex + 1} could only place {spawnedInRegion}/{targetForRegion} trees.");
            }

            forestFrontier.Clear();
        }
    }

    private bool IsTooCloseToReservedZones(Vector3 candidate, List<ReservedSpawnZone> reservedZones)
    {
        if (reservedZones == null)
        {
            return false;
        }

        float defaultSqrRadius = treeExclusionRadius * treeExclusionRadius;
        for (int i = 0; i < reservedZones.Count; i++)
        {
            ReservedSpawnZone reservedZone = reservedZones[i];
            Vector3 delta = reservedZone.position - candidate;
            delta.y = 0f;
            float exclusionRadius = reservedZone.radius > 0f ? reservedZone.radius : treeExclusionRadius;
            float sqrRadius = exclusionRadius * exclusionRadius;
            if (exclusionRadius <= 0f)
            {
                sqrRadius = defaultSqrRadius;
            }

            if (delta.sqrMagnitude <= sqrRadius)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsTooCloseToOtherTrees(Vector3 candidate, List<Vector3> spawnedTreePositions)
    {
        if (spawnedTreePositions == null || spawnedTreePositions.Count == 0)
        {
            return false;
        }

        float minDistance = Mathf.Max(0.01f, minTreeDistance);
        float sqrMinDistance = minDistance * minDistance;
        for (int i = 0; i < spawnedTreePositions.Count; i++)
        {
            Vector3 delta = spawnedTreePositions[i] - candidate;
            delta.y = 0f;
            if (delta.sqrMagnitude < sqrMinDistance)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsFarEnoughFromCliffs(Vector2 sampleUv)
    {
        return IsFarEnoughFromCliffs(sampleUv, minCliffDistance);
    }

    private bool IsFarEnoughFromCliffs(Vector2 sampleUv, float minimumDistance)
    {
        float requiredDistance = Mathf.Max(0f, minimumDistance);
        if (requiredDistance <= 0f)
        {
            return true;
        }

        int searchRadiusPixels = Mathf.CeilToInt(requiredDistance * PixelsPerWorldUnitX()) + 3;
        float distanceToBlack = FindDistanceToRole(sampleUv, MaskRole.Black, searchRadiusPixels);
        if (distanceToBlack < 0f)
        {
            return true;
        }

        float distanceWorld = distanceToBlack / PixelsPerWorldUnitX();
        return distanceWorld >= requiredDistance;
    }

    private bool IsTooCloseToOtherDecorations(Vector3 candidate, List<Vector3> occupiedPositions)
    {
        if (occupiedPositions == null || occupiedPositions.Count == 0)
        {
            return false;
        }

        float minDistance = Mathf.Max(0.05f, decorationMinDistance);
        float sqrMinDistance = minDistance * minDistance;
        for (int i = 0; i < occupiedPositions.Count; i++)
        {
            Vector3 delta = occupiedPositions[i] - candidate;
            delta.y = 0f;
            if (delta.sqrMagnitude < sqrMinDistance)
            {
                return true;
            }
        }

        return false;
    }

    private bool CanSpawnTreeAt(
        Vector3 worldPosition,
        List<ReservedSpawnZone> blockedZones,
        List<Vector3> spawnedTreePositions,
        bool useDensityNoise = true,
        MaskRole requiredRole = MaskRole.Green)
    {
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        Vector2 sampleUv = WorldToUv(localPosition);
        if (GetRoleAtUv(sampleUv) != requiredRole)
        {
            return false;
        }

        if (!IsFarEnoughFromCliffs(sampleUv))
        {
            return false;
        }

        if (SampleHeight(sampleUv) > 0.05f)
        {
            return false;
        }

        if (useDensityNoise)
        {
            float clusterNoise = Mathf.PerlinNoise(
                sampleUv.x * treeClusterNoiseScale + seed,
                sampleUv.y * treeClusterNoiseScale + (seed * 0.23f));
            if (clusterNoise < treeClusterThreshold)
            {
                return false;
            }

            float chance = Mathf.PerlinNoise(sampleUv.x * 8f + seed, sampleUv.y * 8f + (seed * 0.23f));
            float densityThreshold = Mathf.Clamp01(treeSpawnChance - ((clusterNoise - treeClusterThreshold) * 0.12f));
            if (chance > densityThreshold)
            {
                return false;
            }
        }

        if (IsTooCloseToReservedZones(worldPosition, blockedZones))
        {
            return false;
        }

        if (IsTooCloseToOtherTrees(worldPosition, spawnedTreePositions))
        {
            return false;
        }

        return true;
    }

    private bool CanSpawnGuaranteedForestTreeAt(
        Vector3 worldPosition,
        GuaranteedForestZone forestZone,
        List<ReservedSpawnZone> blockedZones,
        List<Vector3> spawnedTreePositions)
    {
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        Vector2 sampleUv = WorldToUv(localPosition);
        MaskRole role = GetRoleAtUv(sampleUv);
        if (role == MaskRole.Black || role == MaskRole.Blue || role == MaskRole.Red || role == MaskRole.Cyan)
        {
            return false;
        }

        if (!IsInsideGuaranteedForestSector(worldPosition, forestZone))
        {
            return false;
        }

        if (!IsFarEnoughFromCliffs(sampleUv, 0.05f))
        {
            return false;
        }

        if (IsBlockedTerrainAt(sampleUv))
        {
            return false;
        }

        if (IsTooCloseToReservedZones(worldPosition, blockedZones))
        {
            return false;
        }

        if (IsTooCloseToOtherTrees(worldPosition, spawnedTreePositions))
        {
            return false;
        }

        return true;
    }

    private bool CanSpawnDecorationAt(Vector3 worldPosition, List<Vector3> occupiedPositions)
    {
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        Vector2 sampleUv = WorldToUv(localPosition);
        MaskRole role = GetRoleAtUv(sampleUv);
        if (role == MaskRole.Black || role == MaskRole.Blue || role == MaskRole.Red || role == MaskRole.Cyan)
        {
            return false;
        }

        float cliffDistance = Mathf.Max(minCliffDistance, decorationCliffDistance);
        if (!IsFarEnoughFromCliffs(sampleUv, cliffDistance))
        {
            return false;
        }

        if (IsBlockedTerrainAt(sampleUv))
        {
            return false;
        }

        return !IsTooCloseToOtherDecorations(worldPosition, occupiedPositions);
    }

    private void SpawnTreeInstance(Vector3 worldPosition, Transform treeRoot, List<Vector3> spawnedTreePositions, Queue<Vector3> frontier)
    {
        GameObject treePrefab = treePrefabs[_random.Next(0, treePrefabs.Length)];
        Quaternion rotation = Quaternion.Euler(0f, (float)_random.NextDouble() * 360f, 0f);
        Instantiate(treePrefab, worldPosition, rotation, treeRoot);
        spawnedTreePositions.Add(worldPosition);
        frontier.Enqueue(worldPosition);
    }

    private Vector3 FindNeighborTreePosition(Vector3 origin)
    {
        float minRadius = Mathf.Max(0.1f, neighborSpawnRadiusMin);
        float maxRadius = Mathf.Max(minRadius, neighborSpawnRadiusMax);
        float angle = (float)_random.NextDouble() * Mathf.PI * 2f;
        float radius = Mathf.Lerp(minRadius, maxRadius, (float)_random.NextDouble());
        Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
        return origin + offset;
    }

    private Vector3 FindRandomTreeCandidatePosition()
    {
        float effectiveWorldWidth = GetEffectiveWorldWidth();
        float effectiveWorldDepth = GetEffectiveWorldDepth();
        float halfWidth = effectiveWorldWidth * 0.5f;
        float halfDepth = effectiveWorldDepth * 0.5f;
        Vector3 localPosition = new Vector3(
            Mathf.Lerp(-halfWidth, halfWidth, (float)_random.NextDouble()),
            0f,
            Mathf.Lerp(-halfDepth, halfDepth, (float)_random.NextDouble()));
        return transform.TransformPoint(localPosition);
    }

    private Vector3 FindGuaranteedForestCandidatePosition(GuaranteedForestZone forestZone, Queue<Vector3> frontier, bool allowNeighborGrowth)
    {
        if (allowNeighborGrowth && frontier != null && frontier.Count > 0 && _random.NextDouble() < 0.55d)
        {
            Vector3 origin = frontier.Peek();
            frontier.Enqueue(frontier.Dequeue());
            return FindNeighborTreePosition(origin);
        }

        float halfAngle = Mathf.Clamp(guaranteedCityForestSpreadAngle, 5f, 180f) * 0.5f;
        float randomAngle = UnityEngine.Random.Range(-halfAngle, halfAngle);
        Vector3 direction = Quaternion.Euler(0f, randomAngle, 0f) * forestZone.outwardDirection;
        float minDistance = Mathf.Max(0.1f, guaranteedCityForestMinDistanceFromCity);
        float maxDistance = Mathf.Max(minDistance, guaranteedCityForestMaxDistanceFromCity);
        float distance = Mathf.Lerp(minDistance, maxDistance, Mathf.Sqrt((float)_random.NextDouble()));
        Vector3 candidate = forestZone.cityPosition + (direction.normalized * distance);
        Vector3 localPosition = transform.InverseTransformPoint(candidate);
        Vector2 sampleUv = WorldToUv(localPosition);
        candidate.y = transform.position.y + SampleHeight(sampleUv);
        return candidate;
    }

    private Vector3 FindRandomDecorationCandidatePosition()
    {
        return FindRandomTreeCandidatePosition();
    }

    private List<Vector3> CollectOccupiedDecorationPositions()
    {
        List<Vector3> occupiedPositions = new List<Vector3>();
        AddGeneratedChildPositions("Trees", decorationTreeExclusionRadius, occupiedPositions);
        AddGeneratedChildPositions("Wolves", decorationWolfExclusionRadius, occupiedPositions);
        AddGeneratedChildPositions("BuildZones", decorationBuildZoneExclusionRadius, occupiedPositions);
        AddCityAndHeroPositions(occupiedPositions);
        return occupiedPositions;
    }

    private void AddGeneratedChildPositions(string childName, float radius, List<Vector3> occupiedPositions)
    {
        if (_generatedRoot == null || occupiedPositions == null)
        {
            return;
        }

        Transform root = _generatedRoot.Find(childName);
        if (root == null)
        {
            return;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            AddOccupiedPosition(root.GetChild(i).position, radius, occupiedPositions);
        }
    }

    private void AddCityAndHeroPositions(List<Vector3> occupiedPositions)
    {
        AddTeamOccupiedPositions(playerTeam, occupiedPositions);
        AddTeamOccupiedPositions(enemyTeam, occupiedPositions);
    }

    private void AddTeamOccupiedPositions(TeamManager team, List<Vector3> occupiedPositions)
    {
        if (team == null)
        {
            return;
        }

        if (team.city != null)
        {
            AddOccupiedPosition(team.city.transform.position, decorationCityExclusionRadius, occupiedPositions);
            if (team.city.spawnPos != null)
            {
                AddOccupiedPosition(team.city.spawnPos.position, heroSpawnExclusionRadius, occupiedPositions);
            }
        }

        Unit heroUnit = team.GetHeroUnit();
        if (heroUnit != null)
        {
            AddOccupiedPosition(heroUnit.transform.position, heroSpawnExclusionRadius, occupiedPositions);
        }
    }

    private void AddOccupiedPosition(Vector3 center, float radius, List<Vector3> occupiedPositions)
    {
        if (occupiedPositions == null)
        {
            return;
        }

        float spacing = Mathf.Max(decorationMinDistance, radius);
        occupiedPositions.Add(center);
        if (spacing <= decorationMinDistance + 0.01f)
        {
            return;
        }

        occupiedPositions.Add(center + new Vector3(spacing, 0f, 0f));
        occupiedPositions.Add(center + new Vector3(-spacing, 0f, 0f));
        occupiedPositions.Add(center + new Vector3(0f, 0f, spacing));
        occupiedPositions.Add(center + new Vector3(0f, 0f, -spacing));
    }

    private void PruneTreesAroundReservedZones(List<Vector3> positions, float exclusionRadius)
    {
        if (positions == null || positions.Count == 0 || exclusionRadius <= 0f)
        {
            return;
        }

        Transform treeRoot = _generatedRoot != null ? _generatedRoot.Find("Trees") : null;
        if (treeRoot == null)
        {
            return;
        }

        float sqrRadius = exclusionRadius * exclusionRadius;
        List<GameObject> treesToDestroy = new List<GameObject>();
        for (int i = 0; i < treeRoot.childCount; i++)
        {
            Transform tree = treeRoot.GetChild(i);
            Vector3 treePosition = tree.position;
            treePosition.y = 0f;

            for (int zoneIndex = 0; zoneIndex < positions.Count; zoneIndex++)
            {
                Vector3 zonePosition = positions[zoneIndex];
                zonePosition.y = 0f;
                if ((treePosition - zonePosition).sqrMagnitude <= sqrRadius)
                {
                    treesToDestroy.Add(tree.gameObject);
                    break;
                }
            }
        }

        for (int i = 0; i < treesToDestroy.Count; i++)
        {
            DestroyNow(treesToDestroy[i]);
        }
    }

    private void PositionTeamsAndCities(List<Vector3> cityWorldPositions)
    {
        if (cityWorldPositions == null || cityWorldPositions.Count < 2)
        {
            Debug.LogWarning("MapMaskGenerator expected two blue city markers.");
            return;
        }

        Vector3[] orderedPositions = GetPlayerThenEnemyCityPositions(cityWorldPositions);

        if (teamPrefab != null)
        {
            playerTeam = CreateGeneratedTeamInstance(1, true, _playerTeamColorBeforeClear, orderedPositions[0]);
            enemyTeam = CreateGeneratedTeamInstance(2, false, _enemyTeamColorBeforeClear, orderedPositions[1]);
            ForceCameraOnHumanHero();
            return;
        }

        TeamManager[] orderedTeams = new[] { playerTeam, enemyTeam };
        for (int i = 0; i < orderedTeams.Length; i++)
        {
            PlaceCityForTeam(orderedTeams[i], orderedPositions[i]);
        }
    }

    private TeamManager CreateGeneratedTeamInstance(int teamId, bool isHumanControlled, Color teamColor, Vector3 targetCityPosition)
    {
        TeamManager teamInstance = Instantiate(teamPrefab, Vector3.zero, Quaternion.identity, _generatedRoot);
        teamInstance.name = isHumanControlled ? "Team1" : "Team2";

        City generatedCity = teamInstance.GetComponentInChildren<City>(true);
        PlayerController playerController = teamInstance.GetComponentInChildren<PlayerController>(true);
        AIHeroController aiHeroController = teamInstance.GetComponentInChildren<AIHeroController>(true);

        teamInstance.ConfigureGeneratedTeam(
            teamId,
            isHumanControlled,
            teamColor,
            playerController,
            aiHeroController,
            generatedCity);

        if (generatedCity != null)
        {
            generatedCity.transform.rotation = Quaternion.Euler(0f, teamInstance.BuildFacingY, 0f);
        }

        Vector3 currentCityPosition = generatedCity != null ? generatedCity.transform.position : teamInstance.transform.position;
        Vector3 cityOffset = targetCityPosition - currentCityPosition;
        teamInstance.transform.position += cityOffset;
        RepositionGeneratedHero(teamInstance, generatedCity);

        if (isHumanControlled && playerController != null)
        {
            playerController.ForceMainCameraToControlledUnit();
            StartCoroutine(ForceCameraOnPlayerControllerForFrames(playerController, 8));
        }

        return teamInstance;
    }

    private void ForceCameraOnHumanHero()
    {
        if (playerTeam == null)
        {
            return;
        }

        Unit heroUnit = playerTeam.GetHeroUnit();
        if (heroUnit == null)
        {
            return;
        }

        ApplyCameraToHero(heroUnit);
        StartCoroutine(ForceCameraOnHeroAtEndOfFrame(heroUnit));
    }

    private IEnumerator ForceCameraOnPlayerControllerForFrames(PlayerController playerController, int frameCount)
    {
        if (playerController == null)
        {
            yield break;
        }

        int safeFrameCount = Mathf.Max(1, frameCount);
        for (int i = 0; i < safeFrameCount; i++)
        {
            playerController.ForceMainCameraToControlledUnit();
            yield return null;
        }

        playerController.ForceMainCameraToControlledUnit();
        yield return new WaitForEndOfFrame();
        playerController.ForceMainCameraToControlledUnit();
    }

    private IEnumerator ForceCameraOnHeroAtEndOfFrame(Unit heroUnit)
    {
        yield return null;
        ApplyCameraToHero(heroUnit);
        yield return new WaitForEndOfFrame();
        ApplyCameraToHero(heroUnit);
    }

    private void ApplyCameraToHero(Unit heroUnit)
    {
        if (heroUnit == null)
        {
            return;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            return;
        }

        Vector3 position = mainCamera.transform.position;
        position.x = heroUnit.transform.position.x;
        position.z = heroUnit.transform.position.z;
        mainCamera.transform.position = position;
    }

    private void RepositionGeneratedHero(TeamManager teamInstance, City generatedCity)
    {
        if (teamInstance == null)
        {
            return;
        }

        Unit heroUnit = teamInstance.GetHeroUnit();
        if (heroUnit == null)
        {
            return;
        }

        Vector3 spawnPosition = generatedCity != null && generatedCity.spawnPos != null
            ? generatedCity.spawnPos.position
            : teamInstance.transform.position + (Vector3.forward * 2f);
        Quaternion spawnRotation = Quaternion.Euler(0f, teamInstance.BuildFacingY, 0f);
        heroUnit.RespawnAt(spawnPosition, spawnRotation);
    }

    private Vector3[] GetPlayerThenEnemyCityPositions(List<Vector3> cityPositions)
    {
        if (cityPositions == null || cityPositions.Count == 0)
        {
            return Array.Empty<Vector3>();
        }

        List<Vector3> ordered = new List<Vector3>(cityPositions);
        ordered.Sort((a, b) =>
        {
            int zComparison = a.z.CompareTo(b.z);
            if (zComparison != 0)
            {
                return zComparison;
            }

            return a.x.CompareTo(b.x);
        });

        if (ordered.Count == 1)
        {
            return new[] { ordered[0] };
        }

        return new[] { ordered[0], ordered[ordered.Count - 1] };
    }

    private readonly struct ReservedSpawnZone
    {
        public readonly Vector3 position;
        public readonly float radius;

        public ReservedSpawnZone(Vector3 position, float radius)
        {
            this.position = position;
            this.radius = radius;
        }
    }

    private readonly struct GuaranteedForestZone
    {
        public readonly Vector3 cityPosition;
        public readonly Vector3 outwardDirection;
        public readonly Vector3 clearZoneCenter;
        public readonly float clearZoneRadius;

        public GuaranteedForestZone(Vector3 cityPosition, Vector3 outwardDirection, Vector3 clearZoneCenter, float clearZoneRadius)
        {
            this.cityPosition = cityPosition;
            this.outwardDirection = outwardDirection;
            this.clearZoneCenter = clearZoneCenter;
            this.clearZoneRadius = clearZoneRadius;
        }
    }

    private void PlaceCityForTeam(TeamManager team, Vector3 targetPosition)
    {
        if (team == null)
        {
            return;
        }

        Vector3 currentBase = team == playerTeam ? _playerBaseBeforeClear : _enemyBaseBeforeClear;
        Vector3 delta = targetPosition - currentBase;
        team.transform.position += delta;

        City city = Instantiate(cityPrefab, targetPosition, Quaternion.Euler(0f, team.BuildFacingY, 0f), team.BuildsRoot);
        int existingPeasantCount = team.GetComponentsInChildren<Peasant>(true).Length;
        if (existingPeasantCount > 0)
        {
            city.peasantsSpawnedOnStart = 0;
        }

        city.AssignTeam(team);
        team.city = city;
    }

    private List<Vector3> ConvertPixelsToWorld(List<Vector2> pixels)
    {
        List<Vector3> positions = new List<Vector3>();
        if (pixels == null)
        {
            return positions;
        }

        for (int i = 0; i < pixels.Count; i++)
        {
            positions.Add(PixelToWorld(pixels[i]));
        }

        return positions;
    }

    private void AdjustBuildZonePositionsNearCities(List<Vector3> buildZoneWorldPositions, List<Vector3> cityWorldPositions)
    {
        if (buildZoneWorldPositions == null || buildZoneWorldPositions.Count == 0 || cityWorldPositions == null || cityWorldPositions.Count == 0)
        {
            return;
        }

        float minimumDistanceFromCity = Mathf.Max(0f, minBuildZoneDistanceFromCity);
        float minimumDistanceFromOtherBuildZones = Mathf.Max(0f, minBuildZoneDistanceFromOtherBuildZones);
        if (minimumDistanceFromCity <= 0f && minimumDistanceFromOtherBuildZones <= 0f)
        {
            return;
        }

        int iterationBudget = Mathf.Max(8, buildZoneWorldPositions.Count * 8);
        for (int iteration = 0; iteration < iterationBudget; iteration++)
        {
            bool movedAny = false;
            for (int i = 0; i < buildZoneWorldPositions.Count; i++)
            {
                Vector3 buildZonePosition = buildZoneWorldPositions[i];
                Vector3 pushVector = Vector3.zero;

                if (minimumDistanceFromCity > 0f)
                {
                    Vector3 nearestCityPosition = cityWorldPositions[0];
                    float nearestSqrDistance = float.MaxValue;

                    for (int cityIndex = 0; cityIndex < cityWorldPositions.Count; cityIndex++)
                    {
                        Vector3 deltaToCity = buildZonePosition - cityWorldPositions[cityIndex];
                        deltaToCity.y = 0f;
                        float sqrDistance = deltaToCity.sqrMagnitude;
                        if (sqrDistance < nearestSqrDistance)
                        {
                            nearestSqrDistance = sqrDistance;
                            nearestCityPosition = cityWorldPositions[cityIndex];
                        }
                    }

                    float nearestDistance = Mathf.Sqrt(nearestSqrDistance);
                    if (nearestDistance < minimumDistanceFromCity)
                    {
                        Vector3 pushDirection = buildZonePosition - nearestCityPosition;
                        pushDirection.y = 0f;
                        if (pushDirection.sqrMagnitude <= 0.0001f)
                        {
                            pushDirection = buildZonePosition.z >= nearestCityPosition.z ? Vector3.forward : Vector3.back;
                        }

                        float pushAmount = minimumDistanceFromCity - nearestDistance;
                        pushVector += pushDirection.normalized * pushAmount;
                    }
                }

                if (minimumDistanceFromOtherBuildZones > 0f)
                {
                    for (int otherIndex = 0; otherIndex < buildZoneWorldPositions.Count; otherIndex++)
                    {
                        if (otherIndex == i)
                        {
                            continue;
                        }

                        Vector3 deltaToOtherBuildZone = buildZonePosition - buildZoneWorldPositions[otherIndex];
                        deltaToOtherBuildZone.y = 0f;
                        float distanceToOtherBuildZone = deltaToOtherBuildZone.magnitude;
                        if (distanceToOtherBuildZone >= minimumDistanceFromOtherBuildZones)
                        {
                            continue;
                        }

                        Vector3 pushDirection = deltaToOtherBuildZone.sqrMagnitude > 0.0001f
                            ? deltaToOtherBuildZone.normalized
                            : ((i & 1) == 0 ? Vector3.right : Vector3.left);
                        float pushAmount = minimumDistanceFromOtherBuildZones - distanceToOtherBuildZone;
                        pushVector += pushDirection * pushAmount;
                    }
                }

                if (pushVector.sqrMagnitude <= 0.0001f)
                {
                    buildZoneWorldPositions[i] = FindNearestSafeBuildZonePosition(buildZonePosition);
                    continue;
                }

                Vector3 adjustedPosition = FindNearestSafeBuildZonePosition(buildZonePosition + pushVector);
                buildZoneWorldPositions[i] = adjustedPosition;
                movedAny = true;
            }

            if (!movedAny)
            {
                break;
            }
        }
    }

    private void SnapBuildZoneHeightsToFinalTerrain(List<Vector3> buildZoneWorldPositions)
    {
        if (buildZoneWorldPositions == null || buildZoneWorldPositions.Count == 0)
        {
            return;
        }

        for (int i = 0; i < buildZoneWorldPositions.Count; i++)
        {
            Vector3 adjustedPosition = buildZoneWorldPositions[i];
            Vector3 localPosition = transform.InverseTransformPoint(adjustedPosition);
            Vector2 sampleUv = WorldToUv(localPosition);
            adjustedPosition.y = transform.position.y + SampleHeight(sampleUv);
            buildZoneWorldPositions[i] = adjustedPosition;
        }
    }

    private Vector3 FindNearestSafeBuildZonePosition(Vector3 desiredPosition)
    {
        Vector3 bestPosition = desiredPosition;
        if (IsBuildZonePlacementSafe(desiredPosition))
        {
            return SnapWorldPositionToTerrain(desiredPosition);
        }

        float searchStep = 0.5f;
        float maxRadius = Mathf.Max(buildZoneCliffSafetyDistance + 6f, 12f);
        for (float radius = searchStep; radius <= maxRadius; radius += searchStep)
        {
            int samples = Mathf.Max(8, Mathf.CeilToInt(radius * 10f));
            for (int sampleIndex = 0; sampleIndex < samples; sampleIndex++)
            {
                float angle = (sampleIndex / (float)samples) * Mathf.PI * 2f;
                Vector3 candidate = desiredPosition + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                if (!IsBuildZonePlacementSafe(candidate))
                {
                    continue;
                }

                return SnapWorldPositionToTerrain(candidate);
            }
        }

        return SnapWorldPositionToTerrain(bestPosition);
    }

    private bool IsBuildZonePlacementSafe(Vector3 worldPosition)
    {
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        Vector2 sampleUv = WorldToUv(localPosition);
        if (IsOutsideMaskBounds(sampleUv))
        {
            return false;
        }

        MaskRole role = GetRoleAtUv(sampleUv);
        if (role == MaskRole.Black)
        {
            return false;
        }

        float requiredCliffDistance = Mathf.Max(0f, buildZoneCliffSafetyDistance);
        if (requiredCliffDistance <= 0f)
        {
            return true;
        }

        int searchRadiusPixels = Mathf.CeilToInt(requiredCliffDistance * PixelsPerWorldUnitX()) + 3;
        float distanceToBlack = FindDistanceToRole(sampleUv, MaskRole.Black, searchRadiusPixels);
        if (distanceToBlack < 0f)
        {
            return true;
        }

        float distanceWorld = distanceToBlack / PixelsPerWorldUnitX();
        return distanceWorld >= requiredCliffDistance;
    }

    private Vector3 SnapWorldPositionToTerrain(Vector3 worldPosition)
    {
        Vector3 adjustedPosition = worldPosition;
        Vector3 localPosition = transform.InverseTransformPoint(adjustedPosition);
        Vector2 sampleUv = WorldToUv(localPosition);
        adjustedPosition.y = transform.position.y + SampleHeight(sampleUv);
        return adjustedPosition;
    }

    private Vector3 PixelToWorld(Vector2 pixel)
    {
        float effectiveWorldWidth = GetEffectiveWorldWidth();
        float effectiveWorldDepth = GetEffectiveWorldDepth();
        Vector2 uv = new Vector2(
            (_maskWidth <= 1) ? 0f : pixel.x / (_maskWidth - 1f),
            (_maskHeight <= 1) ? 0f : pixel.y / (_maskHeight - 1f));
        uv = TransformUv(uv);

        Vector3 local = new Vector3(
            (uv.x - 0.5f) * effectiveWorldWidth,
            0f,
            (uv.y - 0.5f) * effectiveWorldDepth);

        return transform.TransformPoint(local);
    }

    private Vector2 WorldToUv(Vector3 localPosition)
    {
        float effectiveWorldWidth = GetEffectiveWorldWidth();
        float effectiveWorldDepth = GetEffectiveWorldDepth();
        float u = Mathf.InverseLerp(-effectiveWorldWidth * 0.5f, effectiveWorldWidth * 0.5f, localPosition.x);
        float v = Mathf.InverseLerp(-effectiveWorldDepth * 0.5f, effectiveWorldDepth * 0.5f, localPosition.z);
        return GetWarpedMaskUv(new Vector2(u, v));
    }

    private void CacheMaskRoles()
    {
        if (maskTexture == null)
        {
            throw new InvalidOperationException("Map mask texture is missing.");
        }

        _roles = new MaskRole[_maskWidth * _maskHeight];
        Color32[] pixels = maskTexture.GetPixels32();
        if (pixels == null || pixels.Length == 0)
        {
            throw new InvalidOperationException($"Map mask texture '{maskTexture.name}' could not be read. Make sure Read/Write is enabled.");
        }

        for (int i = 0; i < pixels.Length; i++)
        {
            _roles[i] = ClassifyColor(pixels[i]);
        }
    }

    private List<Vector2> ExtractMarkerCentroids(MaskRole targetRole, int minPixels)
    {
        List<List<Vector2Int>> regions = ExtractMarkerRegions(targetRole, minPixels);
        List<Vector2> centroids = new List<Vector2>(regions.Count);
        for (int i = 0; i < regions.Count; i++)
        {
            List<Vector2Int> region = regions[i];
            Vector2 sum = Vector2.zero;
            for (int j = 0; j < region.Count; j++)
            {
                sum += region[j];
            }

            centroids.Add(sum / region.Count);
        }

        return centroids;
    }

    private List<List<Vector2Int>> ExtractMarkerRegions(MaskRole targetRole, int minPixels)
    {
        List<List<Vector2Int>> regions = new List<List<Vector2Int>>();
        bool[] visited = new bool[_roles.Length];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();

        for (int y = 0; y < _maskHeight; y++)
        {
            for (int x = 0; x < _maskWidth; x++)
            {
                int startIndex = x + (y * _maskWidth);
                if (visited[startIndex] || _roles[startIndex] != targetRole)
                {
                    continue;
                }

                visited[startIndex] = true;
                queue.Enqueue(new Vector2Int(x, y));

                List<Vector2Int> region = new List<Vector2Int>();
                while (queue.Count > 0)
                {
                    Vector2Int current = queue.Dequeue();
                    region.Add(current);

                    for (int offsetY = -1; offsetY <= 1; offsetY++)
                    {
                        for (int offsetX = -1; offsetX <= 1; offsetX++)
                        {
                            if (offsetX == 0 && offsetY == 0)
                            {
                                continue;
                            }

                            int nextX = current.x + offsetX;
                            int nextY = current.y + offsetY;
                            if (nextX < 0 || nextX >= _maskWidth || nextY < 0 || nextY >= _maskHeight)
                            {
                                continue;
                            }

                            int nextIndex = nextX + (nextY * _maskWidth);
                            if (visited[nextIndex] || _roles[nextIndex] != targetRole)
                            {
                                continue;
                            }

                            visited[nextIndex] = true;
                            queue.Enqueue(new Vector2Int(nextX, nextY));
                        }
                    }
                }

                if (region.Count >= minPixels)
                {
                    regions.Add(region);
                }
            }
        }

        return regions;
    }

    private float SampleHeight(Vector2 uv)
    {
        return SampleTerrainHeight(uv, Mathf.Clamp01(visualCliffHeightMultiplier));
    }

    private bool IsOuterBlackBand(Vector2 uv)
    {
        float borderWorld = Mathf.Max(0f, outerBlackBorderWorldSize);
        if (borderWorld <= 0.01f)
        {
            return false;
        }

        float borderUvX = borderWorld / Mathf.Max(0.01f, GetEffectiveWorldWidth());
        float borderUvY = borderWorld / Mathf.Max(0.01f, GetEffectiveWorldDepth());
        return uv.x <= borderUvX ||
               uv.x >= 1f - borderUvX ||
               uv.y <= borderUvY ||
               uv.y >= 1f - borderUvY;
    }

    private float FindDistanceToRole(Vector2 uv, MaskRole role, int maxRadiusPixels)
    {
        int centerX = Mathf.RoundToInt(Mathf.Clamp01(uv.x) * (_maskWidth - 1));
        int centerY = Mathf.RoundToInt(Mathf.Clamp01(uv.y) * (_maskHeight - 1));
        if (GetRole(centerX, centerY) == role)
        {
            return 0f;
        }

        float bestDistance = float.MaxValue;
        int radius = Mathf.Max(1, maxRadiusPixels);
        for (int y = -radius; y <= radius; y++)
        {
            int sampleY = centerY + y;
            if (sampleY < 0 || sampleY >= _maskHeight)
            {
                continue;
            }

            for (int x = -radius; x <= radius; x++)
            {
                int sampleX = centerX + x;
                if (sampleX < 0 || sampleX >= _maskWidth)
                {
                    continue;
                }

                if (GetRole(sampleX, sampleY) != role)
                {
                    continue;
                }

                float distance = Mathf.Sqrt((x * x) + (y * y));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                }
            }
        }

        return bestDistance == float.MaxValue ? -1f : bestDistance;
    }

    private float PixelsPerWorldUnitX()
    {
        return _maskWidth / Mathf.Max(1f, GetEffectiveWorldWidth());
    }

    private float GetEffectiveWorldWidth()
    {
        return Mathf.Max(1f, worldWidth * Mathf.Max(0.1f, mapSizeMultiplier));
    }

    private float GetEffectiveWorldDepth()
    {
        return Mathf.Max(1f, worldDepth * Mathf.Max(0.1f, mapSizeMultiplier));
    }

    private Vector2 GetTerrainMaskUvFromExpandedSurface(float u, float v)
    {
        float effectiveWorldWidth = GetEffectiveWorldWidth();
        float effectiveWorldDepth = GetEffectiveWorldDepth();
        float expandedWorldWidth = effectiveWorldWidth + (Mathf.Max(0f, terrainMeshExtensionWorld) * 2f);
        float expandedWorldDepth = effectiveWorldDepth + (Mathf.Max(0f, terrainMeshExtensionWorld) * 2f);
        float localX = (u - 0.5f) * expandedWorldWidth;
        float localZ = (v - 0.5f) * expandedWorldDepth;
        Vector2 baseUv = new Vector2(
            (localX / Mathf.Max(0.01f, effectiveWorldWidth)) + 0.5f,
            (localZ / Mathf.Max(0.01f, effectiveWorldDepth)) + 0.5f);
        return GetWarpedMaskUv(baseUv, false);
    }

    private bool IsOutsideMaskBounds(Vector2 uv)
    {
        return uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f;
    }

    private float SampleTerrainHeight(Vector2 uv, float heightMultiplier)
    {
        if (IsOutsideMaskBounds(uv))
        {
            float noise = (Mathf.PerlinNoise((uv.x * 11f) + seed, (uv.y * 11f) + 3f) - 0.5f) * plateauNoiseHeight;
            return (plateauHeight + noise) * Mathf.Max(0f, heightMultiplier);
        }

        if (IsInsideGuaranteedForestClearZone(uv))
        {
            return 0f;
        }

        if (IsInsideBuildZoneCliffSafetyZone(uv))
        {
            return 0f;
        }

        if (IsOuterBlackBand(uv))
        {
            float noise = (Mathf.PerlinNoise((uv.x * 11f) + seed, (uv.y * 11f) + 3f) - 0.5f) * plateauNoiseHeight;
            return (plateauHeight + noise) * Mathf.Max(0f, heightMultiplier);
        }

        MaskRole role = GetRoleAtUv(uv);
        if (role == MaskRole.Black)
        {
            float noise = (Mathf.PerlinNoise((uv.x * 11f) + seed, (uv.y * 11f) + 3f) - 0.5f) * plateauNoiseHeight;
            return (plateauHeight + noise) * Mathf.Max(0f, heightMultiplier);
        }

        int slopeRadiusPixels = Mathf.Max(2, Mathf.CeilToInt(slopeWidth * PixelsPerWorldUnitX()));
        float distanceToBlack = FindDistanceToRole(uv, MaskRole.Black, slopeRadiusPixels);
        if (distanceToBlack < 0f)
        {
            return 0f;
        }

        float slopeNoise = (Mathf.PerlinNoise((uv.x * 8.1f) + seed, (uv.y * 8.1f) + 19f) - 0.5f) * slopeNoiseStrength;
        float localSlopeWidth = Mathf.Max(0.45f, slopeWidth + slopeNoise);
        float distanceWorld = distanceToBlack / PixelsPerWorldUnitX();
        if (distanceWorld >= localSlopeWidth)
        {
            return 0f;
        }

        float t = Mathf.Clamp01(distanceWorld / Mathf.Max(0.01f, localSlopeWidth));
        return Mathf.SmoothStep(plateauHeight, 0f, t) * Mathf.Max(0f, heightMultiplier);
    }

    private bool IsBlockedTerrainAt(Vector2 uv)
    {
        return SampleTerrainHeight(uv, 1f) > 0.05f;
    }

    private bool IsInsideGuaranteedForestClearZone(Vector2 uv)
    {
        if (_guaranteedForestZones == null || _guaranteedForestZones.Count == 0)
        {
            return false;
        }

        Vector3 worldPosition = transform.TransformPoint(UvToLocalPosition(uv));
        worldPosition.y = 0f;
        for (int i = 0; i < _guaranteedForestZones.Count; i++)
        {
            Vector3 clearZoneCenter = _guaranteedForestZones[i].clearZoneCenter;
            clearZoneCenter.y = 0f;
            float clearZoneRadius = _guaranteedForestZones[i].clearZoneRadius;
            if ((worldPosition - clearZoneCenter).sqrMagnitude <= clearZoneRadius * clearZoneRadius)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsInsideGuaranteedForestSector(Vector3 worldPosition, GuaranteedForestZone forestZone)
    {
        Vector3 flatDelta = worldPosition - forestZone.cityPosition;
        flatDelta.y = 0f;
        float distance = flatDelta.magnitude;
        float minDistance = Mathf.Max(0f, guaranteedCityForestMinDistanceFromCity);
        float maxDistance = Mathf.Max(minDistance, guaranteedCityForestMaxDistanceFromCity);
        if (distance < minDistance || distance > maxDistance)
        {
            return false;
        }

        if (flatDelta.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float angle = Vector3.Angle(forestZone.outwardDirection, flatDelta.normalized);
        return angle <= Mathf.Clamp(guaranteedCityForestSpreadAngle, 5f, 180f) * 0.5f;
    }

    private bool IsInsideBuildZoneCliffSafetyZone(Vector2 uv)
    {
        if (_adjustedBuildZonePositions == null || _adjustedBuildZonePositions.Count == 0)
        {
            return false;
        }

        float safetyDistance = Mathf.Max(0f, buildZoneCliffSafetyDistance);
        if (safetyDistance <= 0f)
        {
            return false;
        }

        Vector3 worldPosition = transform.TransformPoint(UvToLocalPosition(uv));
        worldPosition.y = 0f;
        float sqrSafetyDistance = safetyDistance * safetyDistance;
        for (int i = 0; i < _adjustedBuildZonePositions.Count; i++)
        {
            Vector3 buildZonePosition = _adjustedBuildZonePositions[i];
            buildZonePosition.y = 0f;
            if ((worldPosition - buildZonePosition).sqrMagnitude <= sqrSafetyDistance)
            {
                return true;
            }
        }

        return false;
    }

    private Vector3 UvToLocalPosition(Vector2 uv)
    {
        return new Vector3(
            (uv.x - 0.5f) * GetEffectiveWorldWidth(),
            0f,
            (uv.y - 0.5f) * GetEffectiveWorldDepth());
    }

    private MaskRole GetRoleAtUv(Vector2 uv)
    {
        if (IsOutsideMaskBounds(uv))
        {
            return MaskRole.Black;
        }

        int x = Mathf.RoundToInt(Mathf.Clamp01(uv.x) * (_maskWidth - 1));
        int y = Mathf.RoundToInt(Mathf.Clamp01(uv.y) * (_maskHeight - 1));
        return GetRole(x, y);
    }

    private MaskRole GetRole(int x, int y)
    {
        return _roles[x + (y * _maskWidth)];
    }

    private Vector2 TransformUv(Vector2 uv)
    {
        switch (_resolvedOrientation)
        {
            case OrientationVariant.Rotate180:
                return new Vector2(1f - uv.x, 1f - uv.y);
            case OrientationVariant.MirrorX:
                return new Vector2(1f - uv.x, uv.y);
            default:
                return uv;
        }
    }

    private Vector2 GetWarpedMaskUv(Vector2 uv)
    {
        return GetWarpedMaskUv(uv, true);
    }

    private Vector2 GetWarpedMaskUv(Vector2 uv, bool clampResult)
    {
        Vector2 orientedUv = TransformUv(uv);
        float primaryX = Mathf.PerlinNoise(
            orientedUv.x * maskWarpScale + seed,
            orientedUv.y * maskWarpScale + 13.37f);
        float primaryY = Mathf.PerlinNoise(
            orientedUv.x * maskWarpScale + 77.13f,
            orientedUv.y * maskWarpScale + seed);

        float secondaryX = Mathf.PerlinNoise(
            orientedUv.x * secondaryWarpScale + 101f + seed,
            orientedUv.y * secondaryWarpScale + 31f);
        float secondaryY = Mathf.PerlinNoise(
            orientedUv.x * secondaryWarpScale + 41f,
            orientedUv.y * secondaryWarpScale + 211f + seed);

        Vector2 warpedUv = orientedUv;
        warpedUv.x += ((primaryX - 0.5f) * 2f * maskWarpStrength) + ((secondaryX - 0.5f) * 2f * secondaryWarpStrength);
        warpedUv.y += ((primaryY - 0.5f) * 2f * maskWarpStrength) + ((secondaryY - 0.5f) * 2f * secondaryWarpStrength);
        if (clampResult)
        {
            warpedUv.x = Mathf.Clamp01(warpedUv.x);
            warpedUv.y = Mathf.Clamp01(warpedUv.y);
        }
        return warpedUv;
    }

    private MaskRole ClassifyColor(Color32 color)
    {
        float r = color.r / 255f;
        float g = color.g / 255f;
        float b = color.b / 255f;
        float max = Mathf.Max(r, Mathf.Max(g, b));
        float min = Mathf.Min(r, Mathf.Min(g, b));
        float saturation = max <= 0.0001f ? 0f : (max - min) / max;
        float brightness = max;

        if (color.r <= 36 && color.g <= 36 && color.b <= 36)
        {
            return MaskRole.Black;
        }

        if (Mathf.Abs(color.r - 255) <= 14 &&
            color.g <= 20 &&
            color.b <= 20 &&
            (color.r - color.g) >= 220 &&
            (color.r - color.b) >= 220)
        {
            return MaskRole.Red;
        }

        if (color.b >= 200 &&
            color.r <= 55 &&
            color.g <= 85)
        {
            return MaskRole.Blue;
        }

        if (Mathf.Abs(color.r - 255) <= 12 &&
            color.g <= 12 &&
            Mathf.Abs(color.b - 255) <= 12 &&
            Mathf.Abs(color.r - color.b) <= 12)
        {
            return MaskRole.Cyan;
        }

        if (Mathf.Abs(color.r - 227) <= 3 &&
            Mathf.Abs(color.g - 255) <= 3 &&
            color.b <= 3)
        {
            return MaskRole.DenseForest;
        }

        if (color.g >= 95 &&
            color.r <= 55 &&
            color.b <= 55)
        {
            return MaskRole.Green;
        }

        if (Mathf.Abs(color.r - 255) <= 12 &&
            Mathf.Abs(color.g - 235) <= 18 &&
            color.b <= 12 &&
            color.r >= color.g &&
            (color.r - color.b) >= 220 &&
            (color.g - color.b) >= 190)
        {
            return MaskRole.Yellow;
        }

        if (brightness >= 0.76f && saturation <= 0.12f)
        {
            return MaskRole.White;
        }

        if (brightness <= 0.22f && saturation <= 0.22f)
        {
            return MaskRole.Black;
        }

        if (brightness >= 0.7f && saturation <= 0.18f)
        {
            return MaskRole.White;
        }

        if (color.r > 170 && color.g < 80 && color.b < 80 && (color.r - color.b) > 110)
        {
            return MaskRole.Red;
        }

        if (color.r > 205 && color.b > 205 && color.g < 35)
        {
            return MaskRole.Cyan;
        }

        if (color.r > 210 && color.g > 185 && color.b < 35)
        {
            return MaskRole.Yellow;
        }

        if (color.g > 85 && color.r < 80 && color.b < 80)
        {
            return MaskRole.Green;
        }

        if (color.b > 160 && color.r < 80 && color.g < 110)
        {
            return MaskRole.Blue;
        }

        return brightness <= 0.5f ? MaskRole.Black : MaskRole.White;
    }

    private List<GuaranteedForestZone> CreateGuaranteedForestZones(List<Vector3> cityWorldPositions)
    {
        List<GuaranteedForestZone> zones = new List<GuaranteedForestZone>();
        if (cityWorldPositions == null || cityWorldPositions.Count == 0)
        {
            return zones;
        }

        List<Vector3> orderedCities = new List<Vector3>(cityWorldPositions);
        orderedCities.Sort((a, b) => a.z.CompareTo(b.z));

        zones.Add(CreateGuaranteedForestZone(orderedCities[0], Vector3.back));
        if (orderedCities.Count > 1)
        {
            zones.Add(CreateGuaranteedForestZone(orderedCities[orderedCities.Count - 1], Vector3.forward));
        }

        return zones;
    }

    private GuaranteedForestZone CreateGuaranteedForestZone(Vector3 cityPosition, Vector3 outwardDirection)
    {
        Vector3 normalizedDirection = outwardDirection.sqrMagnitude > 0.0001f ? outwardDirection.normalized : Vector3.forward;
        float minDistance = Mathf.Max(0f, guaranteedCityForestMinDistanceFromCity);
        float maxDistance = Mathf.Max(minDistance, guaranteedCityForestMaxDistanceFromCity);
        Vector3 clearZoneCenter = cityPosition + (normalizedDirection * ((minDistance + maxDistance) * 0.5f));
        return new GuaranteedForestZone(
            cityPosition,
            normalizedDirection,
            clearZoneCenter,
            Mathf.Max(0.5f, guaranteedCityForestCliffClearRadius));
    }

    private void EnsureRockObstacle(GameObject rockInstance)
    {
        if (rockInstance == null)
        {
            return;
        }

        Collider existingCollider = rockInstance.GetComponentInChildren<Collider>();
        if (existingCollider != null)
        {
            existingCollider.isTrigger = false;
            return;
        }

        Renderer[] renderers = rockInstance.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            return;
        }

        Bounds combinedBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combinedBounds.Encapsulate(renderers[i].bounds);
        }

        SphereCollider collider = rockInstance.AddComponent<SphereCollider>();
        collider.center = rockInstance.transform.InverseTransformPoint(combinedBounds.center);
        collider.radius = Mathf.Max(0.2f, Mathf.Max(combinedBounds.size.x, combinedBounds.size.z) * 0.22f);
        collider.isTrigger = false;
    }

    private void TryResolveBestRole(float candidateDistance, MaskRole role, ref float bestDistance, ref MaskRole bestRole)
    {
        if (candidateDistance < bestDistance)
        {
            bestDistance = candidateDistance;
            bestRole = role;
        }
    }

    private float ColorDistanceSq(Color32 color, byte r, byte g, byte b)
    {
        float dr = color.r - r;
        float dg = color.g - g;
        float db = color.b - b;
        return (dr * dr) + (dg * dg) + (db * db);
    }

    private float FlatDistanceSqr(Vector3 a, Vector3 b)
    {
        Vector3 delta = a - b;
        delta.y = 0f;
        return delta.sqrMagnitude;
    }

    private void DestroyNow(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        DestroyImmediate(target);
    }

#if UNITY_EDITOR
    private void EnsureMaskTextureReadableInEditor()
    {
        maskTexture = EnsureTextureReadableInEditor(maskTexture);
    }

    private Texture2D EnsureTextureReadableInEditor(Texture2D texture)
    {
        if (texture == null)
        {
            return null;
        }

        string texturePath = AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrWhiteSpace(texturePath))
        {
            return texture;
        }

        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null || importer.isReadable)
        {
            return texture;
        }

        importer.isReadable = true;
        importer.SaveAndReimport();
        AssetDatabase.Refresh();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
    }
#endif
}

public static class MapMaskGeneratorBootstrap
{
#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapSampleScene()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || activeScene.name != "SampleScene")
        {
            return;
        }

        if (UnityEngine.Object.FindObjectOfType<MapMaskGenerator>() != null)
        {
            return;
        }

        Texture2D maskTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(MapMaskGenerator.DefaultMaskAssetPath);
        if (maskTexture == null)
        {
            return;
        }

        GameObject generatorObject = new GameObject("MapMaskGenerator");
        MapMaskGenerator generator = generatorObject.AddComponent<MapMaskGenerator>();
        generator.generateOnAwake = false;
        generator.ConfigureDefaultsFromProjectAssets();
        generator.AssignSceneTeams(UnityEngine.Object.FindObjectsOfType<TeamManager>(true));
        generator.Generate();
    }
#endif
}
