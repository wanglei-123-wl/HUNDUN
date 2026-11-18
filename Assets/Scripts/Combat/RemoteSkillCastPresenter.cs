using System;
using UnityEngine;

public static class RemoteSkillCastPresenter
{
    public static bool TryPlay(SkillCastEvent message, RemotePlayerSync caster,
        技能释放配置 config, float elapsedSeconds,
        out GameObject instance, out string reason)
    {
        instance = null;
        if (caster == null || config == null || config.特效预制体 == null)
        {
            reason = "missing_caster_or_effect";
            return false;
        }

        float duration = Mathf.Max(0.1f, config.远端特效时长秒数);
        float elapsed = float.IsNaN(elapsedSeconds)
            || float.IsInfinity(elapsedSeconds) ? 0f
            : Mathf.Max(0f, elapsedSeconds);
        if (elapsed >= duration)
        {
            reason = "effect_expired";
            return false;
        }

        Vector3 targetPosition = new Vector3(
            (float)message.targetX, (float)message.targetY, (float)message.targetZ);
        Vector3 position;
        Quaternion rotation;
        switch (config.释放方式)
        {
            case 技能释放方式.玩家释放节点:
                Transform releaseNode = caster.SkillReleasePoint;
                if (releaseNode == null)
                {
                    reason = "missing_release_node";
                    return false;
                }

                Quaternion snapshotRotation =
                    Quaternion.Euler(0f, (float)message.casterRotY, 0f);
                position = new Vector3(
                    (float)message.casterX, (float)message.casterY,
                    (float)message.casterZ) + snapshotRotation * releaseNode.localPosition;
                Vector3 direction = targetPosition - position;
                if (direction.sqrMagnitude < 0.00000001f)
                {
                    reason = "invalid_aim_direction";
                    return false;
                }

                rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                break;

            case 技能释放方式.怪物目标位置:
                position = targetPosition;
                rotation = Quaternion.identity;
                break;

            default:
                reason = "unknown_release_mode";
                return false;
        }

        GameObject prefab = config.特效预制体;
        if (prefab.GetComponentInChildren<技能粒子特效>(true) == null
            && prefab.GetComponentInChildren<节能技能特效>(true) == null
            && prefab.GetComponentInChildren<ParticleSystem>(true) == null)
        {
            reason = "effect_has_no_player";
            return false;
        }

        // Keep the observer instance inactive until its local-camera effects are disabled.
        GameObject stagingRoot = new GameObject("RemoteSkillCastStaging");
        stagingRoot.SetActive(false);
        try
        {
            instance = UnityEngine.Object.Instantiate(
                prefab, position, rotation, stagingRoot.transform);
            foreach (HS_VolumeChanger cameraEffect in
                instance.GetComponentsInChildren<HS_VolumeChanger>(true))
                if (cameraEffect != null)
                    cameraEffect.enabled = false;
            instance.transform.SetParent(null, true);
        }
        finally
        {
            UnityEngine.Object.Destroy(stagingRoot);
        }

        技能粒子特效 particleEffect =
            instance.GetComponentInChildren<技能粒子特效>(true);
        if (particleEffect != null)
        {
            particleEffect.释放技能();
        }
        else
        {
            节能技能特效 customEffect =
                instance.GetComponentInChildren<节能技能特效>(true);
            if (customEffect != null)
            {
                customEffect.释放技能();
                if (elapsed > 0f)
                    Debug.LogWarning("[RemoteSkillCast][TIMING] custom effect"
                        + " cannot seek; started at receive time.", caster);
            }
            else
            {
                ParticleSystem[] particles =
                    instance.GetComponentsInChildren<ParticleSystem>(true);
                foreach (ParticleSystem particle in particles)
                    if (particle != null) particle.Play(true);
            }
        }

        if (elapsed > 0f
            && instance.GetComponentInChildren<节能技能特效>(true) == null)
        {
            foreach (ParticleSystem particle in
                instance.GetComponentsInChildren<ParticleSystem>(true))
                if (particle != null)
                {
                    particle.Simulate(elapsed, false, false, true);
                    particle.Play(false);
                }
        }

        UnityEngine.Object.Destroy(instance, duration - elapsed);
        TryPlayConfiguredAnimation(caster, config.远端动画触发器);
        reason = "played";
        return true;
    }

    private static void TryPlayConfiguredAnimation(
        RemotePlayerSync caster, string triggerName)
    {
        if (string.IsNullOrWhiteSpace(triggerName)) return;

        Animator animator = caster.VisualAnimator;
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.type != AnimatorControllerParameterType.Trigger
                    || !string.Equals(parameter.name, triggerName, StringComparison.Ordinal))
                    continue;

                animator.ResetTrigger(triggerName);
                animator.SetTrigger(triggerName);
                return;
            }
        }

        Debug.LogWarning("[RemoteSkillCast] Animator trigger not found: "
            + triggerName, caster);
    }
}
