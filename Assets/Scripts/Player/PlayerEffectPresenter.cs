using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerEffectPresenter : MonoBehaviour
{
    private readonly Dictionary<string, GameObject> activeEffects =
        new Dictionary<string, GameObject>(StringComparer.Ordinal);
    private readonly HashSet<string> conditionEpisodes = new HashSet<string>(StringComparer.Ordinal);

    private PlayerPresentationSettings settings;
    private PlayerEffectStateSource stateSource;
    private Transform followTarget;
    private Transform skillReleasePoint;
    private GameObject visual;
    private List<PlayerMeshEffect.ModelMesh> modelMeshes;

    public static void Bind(GameObject playerRoot, PlayerPresentationSettings settings,
        Transform followTarget, Transform skillReleasePoint, GameObject visual)
    {
        playerRoot.TryGetComponent(out PlayerEffectPresenter presenter);
        if (settings == null || settings.PlayerEffects.Count == 0)
        {
            if (presenter != null)
                presenter.ClearBinding();
            return;
        }

        if (presenter == null)
            presenter = playerRoot.AddComponent<PlayerEffectPresenter>();

        if (presenter.settings == settings && presenter.followTarget == followTarget
            && presenter.skillReleasePoint == skillReleasePoint)
        {
            presenter.SetVisual(visual);
            presenter.AttachStateSource();
            presenter.RefreshConditions();
            return;
        }

        presenter.ClearBinding();
        presenter.settings = settings;
        presenter.followTarget = followTarget;
        presenter.skillReleasePoint = skillReleasePoint;
        presenter.SetVisual(visual);
        presenter.AttachStateSource();
        if (presenter.isActiveAndEnabled)
        {
            presenter.PlaySpawnEffects();
            presenter.RefreshConditions();
        }
    }

    public void ClearBinding()
    {
        DetachStateSource();
        stateSource = null;
        settings = null;
        followTarget = null;
        skillReleasePoint = null;
        StopAllEffects();
        SetVisual(null);
    }

    internal void SetVisual(GameObject newVisual)
    {
        if (ReferenceEquals(visual, newVisual))
            return;

        visual = newVisual;
        modelMeshes = null;
        foreach (GameObject effect in activeEffects.Values)
        {
            if (effect != null && effect.TryGetComponent(out PlayerMeshEffect meshEffect))
                BindMeshEffect(meshEffect);
        }
    }

    private void BindMeshEffect(PlayerMeshEffect meshEffect)
    {
        if (modelMeshes == null)
            modelMeshes = PlayerMeshEffect.FindModelMeshes(visual);

        meshEffect.BindModelMeshes(modelMeshes);
    }

    public bool TryPlay(string effectName)
    {
        if (!isActiveAndEnabled || settings == null || string.IsNullOrWhiteSpace(effectName))
            return false;

        foreach (PlayerEffectDefinition definition in settings.PlayerEffects)
        {
            if (definition != null && definition.Trigger == PlayerEffectTrigger.OnDemand
                && string.Equals(effectName, definition.EffectName, StringComparison.Ordinal))
                return TryStartEffect(definition);
        }

        return false;
    }

    public void Stop(string effectName)
    {
        if (effectName == null || !activeEffects.TryGetValue(effectName, out GameObject instance))
            return;

        activeEffects.Remove(effectName);
        Release(instance);
    }

    private void OnEnable()
    {
        if (settings == null) return;
        AttachStateSource();
        PlaySpawnEffects();
        RefreshConditions();
    }

    private void OnDisable()
    {
        DetachStateSource();
        StopAllEffects();
    }

    private void OnDestroy() => ClearBinding();

    private void AttachStateSource()
    {
        if (settings == null) return;
        if (stateSource == null && !TryGetComponent(out stateSource))
            stateSource = gameObject.AddComponent<PlayerEffectStateSource>();
        stateSource.Changed -= RefreshConditions;
        if (isActiveAndEnabled) stateSource.Changed += RefreshConditions;
    }

    private void DetachStateSource()
    {
        if (stateSource != null) stateSource.Changed -= RefreshConditions;
    }

    private bool ConditionsMet(PlayerEffectDefinition definition)
    {
        PlayerEffectConditions conditions = definition.Conditions;
        const PlayerEffectConditions supported = PlayerEffectConditions.Alive | PlayerEffectConditions.Meditating;
        if ((conditions & ~supported) != 0) return false;
        if (conditions == PlayerEffectConditions.None) return true;
        PlayerEffectState state = stateSource != null ? stateSource.Current : default;
        return state.IsKnown
            && ((conditions & PlayerEffectConditions.Alive) == 0 || state.IsAlive)
            && ((conditions & PlayerEffectConditions.Meditating) == 0 || state.IsMeditating);
    }

    private void RefreshConditions()
    {
        if (settings == null || !isActiveAndEnabled) return;
        foreach (PlayerEffectDefinition definition in settings.PlayerEffects)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.EffectId)) continue;
            string id = definition.EffectId;
            if (!ConditionsMet(definition))
            {
                conditionEpisodes.Remove(id);
                Stop(id);
                continue;
            }
            if (definition.Trigger != PlayerEffectTrigger.WhileCondition) continue;
            bool missingLoop = definition.Playback == PlayerEffectPlayback.Loop
                && (!activeEffects.TryGetValue(id, out GameObject instance) || instance == null);
            // Once effects fire only on eligibility edges. Loop effects can
            // recover a lost instance without creating duplicates.
            if ((!conditionEpisodes.Contains(id) || missingLoop) && TryStartEffect(definition))
                conditionEpisodes.Add(id);
        }
    }

    private void PlaySpawnEffects()
    {
        if (settings == null || !isActiveAndEnabled)
            return;

        foreach (PlayerEffectDefinition definition in settings.PlayerEffects)
        {
            if (definition != null && definition.Trigger == PlayerEffectTrigger.OnSpawn)
                TryStartEffect(definition);
        }
    }

    private bool TryStartEffect(PlayerEffectDefinition definition)
    {
        // All entry points, including manual TryPlay and spawn playback, use
        // the same gate. Shared definitions are never mutated at runtime.
        if (settings == null || !isActiveAndEnabled || !ConditionsMet(definition)) return false;
        string effectName = definition.EffectName;
        if (definition.Prefab == null || string.IsNullOrWhiteSpace(effectName)
            || (activeEffects.TryGetValue(effectName, out GameObject current) && current != null))
            return false;

        Transform anchor = definition.Anchor == PlayerEffectAnchor.SkillRelease && skillReleasePoint != null
            ? skillReleasePoint
            : followTarget != null ? followTarget : transform;

        // Configure the cloned systems before Unity can play them on activation.
        GameObject effectRoot = new GameObject(effectName);
        effectRoot.SetActive(false);
        effectRoot.transform.SetParent(anchor, false);
        effectRoot.transform.localPosition = definition.LocalOffset;
        GameObject instance = Instantiate(definition.Prefab, effectRoot.transform, false);
        instance.SetActive(true);

        bool loop = definition.Playback == PlayerEffectPlayback.Loop;
        foreach (ParticleSystem particle in instance.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = particle.main;
            main.playOnAwake = false;
            main.loop = loop;
            main.stopAction = ParticleSystemStopAction.None;
        }

        OverlayFX meshSource = instance.GetComponentInChildren<OverlayFX>(true);
        if (meshSource != null && meshSource.enabled)
        {
            meshSource.enabled = false;
            PlayerMeshEffect meshEffect = effectRoot.AddComponent<PlayerMeshEffect>();
            meshEffect.Configure(meshSource);
            BindMeshEffect(meshEffect);
        }

        effectRoot.SetActive(true);
        ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>();
        if (particles.Length == 0)
        {
            Debug.LogWarning($"玩家特效「{effectName}」没有可播放的粒子系统。", this);
            Release(effectRoot);
            return false;
        }

        activeEffects[effectName] = effectRoot;
        foreach (ParticleSystem particle in particles)
            particle.Play(false);

        if (!loop)
            StartCoroutine(ReleaseWhenFinished(effectName, effectRoot, particles));

        return true;
    }

    private IEnumerator ReleaseWhenFinished(string effectName, GameObject instance, ParticleSystem[] particles)
    {
        do
        {
            yield return null;
        }
        while (instance != null && Array.Exists(particles, particle => particle != null && particle.IsAlive(false)));

        if (activeEffects.TryGetValue(effectName, out GameObject current) && ReferenceEquals(instance, current))
            Stop(effectName);
    }

    private void StopAllEffects()
    {
        StopAllCoroutines();
        foreach (GameObject instance in activeEffects.Values)
            Release(instance);
        activeEffects.Clear();
        conditionEpisodes.Clear();
    }

    private static void Release(GameObject instance)
    {
        if (instance == null)
            return;

        instance.SetActive(false);
        Destroy(instance);
    }
}
