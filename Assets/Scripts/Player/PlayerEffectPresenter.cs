using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerEffectPresenter : MonoBehaviour
{
    private readonly Dictionary<string, GameObject> activeEffects =
        new Dictionary<string, GameObject>(StringComparer.Ordinal);

    private PlayerPresentationSettings settings;
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
            return;
        }

        presenter.ClearBinding();
        presenter.settings = settings;
        presenter.followTarget = followTarget;
        presenter.skillReleasePoint = skillReleasePoint;
        presenter.SetVisual(visual);
        if (presenter.isActiveAndEnabled)
            presenter.PlaySpawnEffects();
    }

    public void ClearBinding()
    {
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

    private void OnEnable() => PlaySpawnEffects();

    private void OnDisable() => StopAllEffects();

    private void PlaySpawnEffects()
    {
        if (settings == null)
            return;

        foreach (PlayerEffectDefinition definition in settings.PlayerEffects)
        {
            if (definition != null && definition.Trigger == PlayerEffectTrigger.OnSpawn)
                TryStartEffect(definition);
        }
    }

    private bool TryStartEffect(PlayerEffectDefinition definition)
    {
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
    }

    private static void Release(GameObject instance)
    {
        if (instance == null)
            return;

        instance.SetActive(false);
        Destroy(instance);
    }
}
