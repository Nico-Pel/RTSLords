using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Animations;
using UnityEngine.UI;
using UnityEngine;

public class Hitbox : GameBehaviour
{
    private const string HpBarPrefabResourcePath = "HpBar";
    private static readonly Color PlayerHpBarColor = new Color32(0x00, 0xFF, 0x53, 0xFF);
    private static readonly Color EnemyHpBarColor = new Color32(0xCF, 0x24, 0x30, 0xFF);
    private static GameObject _cachedHpBarPrefab;

    public struct DamageContext
    {
        public int damage;
        public DamageTypes damageType;
        public UnitStats sourceStats;
        public Hitbox sourceHitbox;
    }

    public int teamID = 1;

    public enum DamageTypes
    {
        melee,
        distance,
        other
    }

    public UnitStats unitStats;
    public bool destroyOnDeath = true;
    [Header("Hp Bar")]
    [SerializeField] private bool showHpBarOnUnits = true;
    [SerializeField] private float defaultHpBarHeight = 1.15f;

    [SerializeField, ReadOnly] private int _currentHp;
    private bool _isDead;
    private GameObject _hpBarInstance;
    private Image _hpBarFill;
    private LookAtConstraint _hpBarLookAtConstraint;
    private Camera _hpBarCamera;

    public event Action<Hitbox> OnDeath;
    public event Action<DamageContext> OnDamaged;

    public int CurrentHp => _currentHp;
    public bool IsDead => _isDead;
    public TeamManager OwnerTeam { get; private set; }
    public Unit OwnerUnit { get; private set; }
    public Build OwnerBuild { get; private set; }

    private void Start()
    {
        _currentHp = unitStats == null ? 1 : unitStats.health;
        OwnerUnit = GetComponent<Unit>();
        OwnerBuild = GetComponent<Build>();

        TeamManager parentTeam = GetComponentInParent<TeamManager>();
        if (parentTeam != null)
        {
            AssignTeam(parentTeam);
        }

        RefreshHpBarVisibility();
    }

    private void LateUpdate()
    {
        if (_hpBarInstance == null || !_hpBarInstance.activeInHierarchy)
        {
            return;
        }

        EnsureHpBarLookAtSource();
    }

    private void OnDestroy()
    {
        if (_hpBarInstance != null)
        {
            Destroy(_hpBarInstance);
            _hpBarInstance = null;
        }
    }

    public void AssignTeam(TeamManager team)
    {
        OwnerTeam = team;
        if (team != null)
        {
            teamID = team.TeamId;
        }
    }

    public virtual void TakeDamage(int damage, DamageTypes damageType, UnitStats sourceStats = null, Hitbox sourceHitbox = null)
    {
        if (_isDead)
        {
            return;
        }

        if (sourceStats != null)
        {
            damage = Mathf.RoundToInt(damage * sourceStats.GetOutgoingDamageMultiplier(damageType, unitStats));
        }

        if (unitStats != null)
        {
            damage = Mathf.RoundToInt(damage * unitStats.GetIncomingDamageMultiplier(damageType, sourceStats));
        }

        if (damageType == DamageTypes.melee)
        {
            damage -= unitStats == null ? 0 : unitStats.armor;
        }
        else if (damageType == DamageTypes.distance)
        {
            damage -= unitStats == null ? 0 : unitStats.distanceArmor;
        }

        if (damage <= 0)
        {
            damage = 1;
        }

        _currentHp -= damage;
        OnDamaged?.Invoke(new DamageContext
        {
            damage = damage,
            damageType = damageType,
            sourceStats = sourceStats,
            sourceHitbox = sourceHitbox
        });

        RefreshHpBarVisibility();

        if (_currentHp <= 0)
        {
            Death();
        }
    }

    public virtual void Heal(int heal)
    {
        if (_isDead || unitStats == null)
        {
            return;
        }

        _currentHp += heal;
        if (_currentHp > unitStats.health)
        {
            _currentHp = unitStats.health;
        }

        RefreshHpBarVisibility();
    }

    public virtual void Death()
    {
        if (_isDead)
        {
            return;
        }

        _isDead = true;
        HideHpBar();
        OnDeath?.Invoke(this);

        if (destroyOnDeath)
        {
            Destroy(gameObject);
        }
    }

    public virtual void Revive()
    {
        _isDead = false;
        _currentHp = unitStats == null ? 1 : unitStats.health;
        RefreshHpBarVisibility();
    }

    private void RefreshHpBarVisibility()
    {
        if (!ShouldUseHpBar() || unitStats == null)
        {
            HideHpBar();
            return;
        }

        int maxHp = Mathf.Max(1, unitStats.health);
        float health01 = Mathf.Clamp01(_currentHp / (float)maxHp);
        if (health01 >= 0.999f)
        {
            HideHpBar();
            return;
        }

        EnsureHpBarInstance();
        if (_hpBarFill != null)
        {
            _hpBarFill.fillAmount = health01;
            _hpBarFill.color = ResolveHpBarColor();
        }

        if (_hpBarInstance != null)
        {
            _hpBarInstance.SetActive(true);
        }
    }

    private bool ShouldUseHpBar()
    {
        return showHpBarOnUnits && OwnerUnit != null;
    }

    private void EnsureHpBarInstance()
    {
        if (_hpBarInstance != null)
        {
            EnsureHpBarLookAtSource();
            return;
        }

        GameObject hpBarPrefab = LoadHpBarPrefab();
        if (hpBarPrefab == null)
        {
            return;
        }

        Transform hpBarAnchor = FindHpBarAnchor();
        _hpBarInstance = Instantiate(hpBarPrefab, hpBarAnchor);

        RectTransform hpBarRect = _hpBarInstance.transform as RectTransform;
        if (hpBarRect != null)
        {
            if (hpBarAnchor != transform)
            {
                hpBarRect.localPosition = Vector3.zero;
            }
            else
            {
                hpBarRect.localPosition = new Vector3(0f, defaultHpBarHeight, 0f);
            }

            hpBarRect.localRotation = Quaternion.identity;
        }
        else
        {
            _hpBarInstance.transform.localPosition = hpBarAnchor != transform
                ? Vector3.zero
                : new Vector3(0f, defaultHpBarHeight, 0f);
            _hpBarInstance.transform.localRotation = Quaternion.identity;
        }

        _hpBarFill = FindHpBarFill(_hpBarInstance);
        _hpBarLookAtConstraint = _hpBarInstance.GetComponent<LookAtConstraint>();
        EnsureHpBarLookAtSource();
        _hpBarInstance.SetActive(false);
    }

    private void EnsureHpBarLookAtSource()
    {
        if (_hpBarLookAtConstraint == null)
        {
            return;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null || mainCamera == _hpBarCamera)
        {
            return;
        }

        _hpBarLookAtConstraint.SetSources(new List<ConstraintSource>());
        ConstraintSource source = new ConstraintSource
        {
            sourceTransform = mainCamera.transform,
            weight = 1f
        };
        _hpBarLookAtConstraint.AddSource(source);
        _hpBarCamera = mainCamera;
    }

    private void HideHpBar()
    {
        if (_hpBarInstance != null)
        {
            _hpBarInstance.SetActive(false);
        }
    }

    private Transform FindHpBarAnchor()
    {
        if (OwnerUnit != null && OwnerUnit.hpBarPos != null)
        {
            return OwnerUnit.hpBarPos;
        }

        return transform;
    }

    private static Image FindHpBarFill(GameObject hpBarInstance)
    {
        if (hpBarInstance == null)
        {
            return null;
        }

        Image[] images = hpBarInstance.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image != null && image.gameObject.name == "Fill")
            {
                return image;
            }
        }

        return null;
    }

    private static GameObject LoadHpBarPrefab()
    {
        if (_cachedHpBarPrefab == null)
        {
            _cachedHpBarPrefab = Resources.Load<GameObject>(HpBarPrefabResourcePath);
        }

        return _cachedHpBarPrefab;
    }

    private Color ResolveHpBarColor()
    {
        TeamManager localPlayerTeam = TeamManager.GetLocalPlayerTeam();
        return localPlayerTeam != null && OwnerTeam == localPlayerTeam ? PlayerHpBarColor : EnemyHpBarColor;
    }
}
