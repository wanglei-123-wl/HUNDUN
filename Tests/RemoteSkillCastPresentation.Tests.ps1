$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$presenter = Get-Content (Join-Path $root 'Assets/Scripts/Combat/RemoteSkillCastPresenter.cs') -Raw
$entity = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/EntityManager.cs') -Raw
$definition = Get-Content (Join-Path $root 'Assets/Scripts/Combat/Skills/SkillReleaseDefinition.cs') -Raw
if (($entity -notmatch 'remotePlayers\.TryGetValue\(casterId, out RemotePlayerSync caster\)') -or
    ($entity -notmatch 'remoteSkillRegistry\.TryGet\(message\.skillId, out 技能释放配置 config\)') -or
    ($entity -notmatch 'RemoteSkillCastPresenter\.TryPlay\(message, caster, config,') -or
    ($definition -notmatch 'public string 远端动画触发器')) {
    throw 'Observer playback is not wired through the remote caster and skill registry.'
}
if ($presenter -match 'cast_skill|skill_cast_result|ApplyItemUseMana|TryPlayAttack') {
    throw 'Observer playback must not execute local combat or attack requests.'
}

$stubs = @'
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object
    {
        public static GameObject LastInstance;
        public static Transform LastInstantiationParent;
        public static GameObject Instantiate(GameObject prefab, Vector3 position,
            Quaternion rotation, Transform parent)
        {
            GameObject instance = new GameObject();
            instance.transform.SetParent(parent, true);
            instance.transform.position = position;
            instance.transform.rotation = rotation;
            foreach (object component in prefab.Components)
            {
                HS_VolumeChanger cameraEffect = component as HS_VolumeChanger;
                instance.Components.Add(cameraEffect == null ? component :
                    new HS_VolumeChanger { enabled = cameraEffect.enabled });
            }
            LastInstance = instance;
            LastInstantiationParent = parent;
            return instance;
        }
        public static void Destroy(GameObject instance, float delay) { }
        public static void Destroy(GameObject instance) { instance.Destroyed = true; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 up => new Vector3(0,1,0);
        public float sqrMagnitude => x*x+y*y+z*z;
        public Vector3 normalized {
            get {
                float length=(float)Math.Sqrt(sqrMagnitude);
                return length==0 ? this : new Vector3(x/length,y/length,z/length);
            }
        }
        public static Vector3 operator +(Vector3 a, Vector3 b) =>
            new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) =>
            new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public struct Quaternion
    {
        public float yaw;
        public static Quaternion identity => new Quaternion {yaw=0};
        public static Quaternion Euler(float x,float y,float z) => new Quaternion {yaw=y};
        public static Quaternion Inverse(Quaternion q) => new Quaternion {yaw=-q.yaw};
        public static Vector3 operator *(Quaternion q,Vector3 v) {
            double radians=q.yaw*Math.PI/180;
            return new Vector3((float)(v.x*Math.Cos(radians)+v.z*Math.Sin(radians)),
                v.y,(float)(v.z*Math.Cos(radians)-v.x*Math.Sin(radians)));
        }
        public static Quaternion LookRotation(Vector3 v, Vector3 up) =>
            new Quaternion {yaw=(float)(Math.Atan2(v.x,v.z)*180/Math.PI)};
    }
    public class Transform {
        public string name;
        public Vector3 position;
        public Vector3 localPosition;
        public Quaternion rotation;
        public GameObject owner;
        public Transform parent;
        public List<Transform> children = new List<Transform>();
        public void SetParent(Transform nextParent, bool worldPositionStays) {
            parent = nextParent;
            if (owner != null) owner.UpdateActive();
        }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : class {
            List<T> found = new List<T>();
            foreach (Transform child in children) found.Add((T)(object)child);
            return found.ToArray();
        }
    }
    public class GameObject : Object {
        public Transform transform = new Transform();
        public List<object> Components = new List<object>();
        public bool activeSelf = true;
        public bool ActiveInHierarchy = true;
        public bool Destroyed;
        public GameObject() { transform.owner = this; }
        public GameObject(string name) : this() { transform.name = name; }
        public void SetActive(bool active) {
            activeSelf = active;
            UpdateActive();
        }
        public void UpdateActive() {
            bool wasActive = ActiveInHierarchy;
            ActiveInHierarchy = activeSelf &&
                (transform.parent == null ||
                 transform.parent.owner == null ||
                 transform.parent.owner.ActiveInHierarchy);
            if (!wasActive && ActiveInHierarchy)
                foreach (object component in Components)
                {
                    HS_VolumeChanger cameraEffect = component as HS_VolumeChanger;
                    if (cameraEffect != null && cameraEffect.enabled)
                        cameraEffect.OnEnableCount++;
                }
        }
        public T GetComponentInChildren<T>(bool inactive) where T : class {
            foreach (object component in Components)
                if (component is T) return (T)component;
            return null;
        }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : class {
            List<T> found=new List<T>();
            foreach (object component in Components)
                if (component is T) found.Add((T)component);
            return found.ToArray();
        }
    }
    public class ParticleSystem {
        public int PlayCount;
        public float SimulatedSeconds;
        public bool IsPlaying;
        public void Play(bool withChildren) { PlayCount++; IsPlaying = true; }
        public void Simulate(float seconds, bool withChildren,
            bool restart, bool fixedTimeStep) {
            SimulatedSeconds += seconds;
            IsPlaying = false;
        }
    }
    public class HS_VolumeChanger {
        public bool enabled = true;
        public int OnEnableCount;
    }
    public static class Mathf {
        public static float Max(float a, float b) => a > b ? a : b;
    }
    public class Animator {
        public object runtimeAnimatorController = new object();
        public AnimatorControllerParameter[] parameters = Array.Empty<AnimatorControllerParameter>();
        public int Triggers;
        public void ResetTrigger(string name) { }
        public void SetTrigger(string name) { Triggers++; }
    }
    public enum AnimatorControllerParameterType { Trigger }
    public class AnimatorControllerParameter {
        public string name;
        public AnimatorControllerParameterType type;
    }
    public static class Debug {
        public static void LogWarning(string text, object context) { }
    }
}
public class SkillCastEvent {
    public double casterX, casterY, casterZ, casterRotY, targetX, targetY, targetZ;
}
public class RemotePlayerSync {
    public UnityEngine.Transform transform = new UnityEngine.Transform();
    public UnityEngine.Animator VisualAnimator;
    public UnityEngine.Transform SkillReleasePoint;
}
public enum 技能释放方式 { 玩家释放节点, 怪物目标位置 }
public class 技能释放配置 {
    public 技能释放方式 释放方式;
    public UnityEngine.GameObject 特效预制体;
    public string 远端动画触发器;
    public float 远端特效时长秒数 = 5f;
}
public class 技能粒子特效 { public int Plays; public void 释放技能() { Plays++; } }
public class 节能技能特效 { public int Plays; public void 释放技能() { Plays++; } }
'@
$source = "using UnityEngine;`n" + $stubs + "`n" +
    ($presenter -replace '(?m)^using [^\r\n]+;\r?\n', '')
Add-Type -TypeDefinition $source -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Nearly($actual, $expected) {
    return [Math]::Abs($actual - $expected) -lt 0.001
}

$caster = [RemotePlayerSync]::new()
$caster.transform.position = [UnityEngine.Vector3]::new(100, 0, 100)
$caster.transform.rotation = [UnityEngine.Quaternion]::Euler(0, 180, 0)
$node = [UnityEngine.Transform]::new()
$node.name = '施法挂点'
$node.localPosition = [UnityEngine.Vector3]::new(0, 1, 2)
$caster.SkillReleasePoint = $node
$legacyNode = [UnityEngine.Transform]::new()
$legacyNode.name = '新灵术'
$legacyNode.position = [UnityEngine.Vector3]::new(100, 9, 100)
$caster.transform.children.Add($legacyNode)
$particle = [UnityEngine.ParticleSystem]::new()
$prefab = [UnityEngine.GameObject]::new()
$prefab.Components.Add($particle)
$config = [技能释放配置]::new()
$config.释放方式 = [技能释放方式]::玩家释放节点
$config.特效预制体 = $prefab
$message = [SkillCastEvent]::new()
$message.casterX = 10
$message.casterY = 0
$message.casterZ = 20
$message.casterRotY = 90
$message.targetX = 15
$message.targetY = 1
$message.targetZ = 20
$instance = $null
$reason = ''
Assert-True ([RemoteSkillCastPresenter]::TryPlay(
    $message, $caster, $config, 0, [ref]$instance, [ref]$reason)) `
    'The bound release point should start the visual even with an old node in the model.'
Assert-True ((Nearly $instance.transform.position.x 12) -and
    (Nearly $instance.transform.position.y 1) -and
    (Nearly $instance.transform.position.z 20) -and
    (Nearly $instance.transform.rotation.yaw 90) -and
    $particle.PlayCount -eq 1 -and
    $particle.IsPlaying -and
    (Nearly $particle.SimulatedSeconds 0) -and
    (Nearly $caster.transform.position.x 100)) `
    'The effect should use server snapshot pose without moving the remote model.'

$config.释放方式 = [技能释放方式]::怪物目标位置
$message.targetX = 0
$message.targetY = 0
$message.targetZ = 0
Assert-True ([RemoteSkillCastPresenter]::TryPlay(
    $message, $caster, $config, 0, [ref]$instance, [ref]$reason) -and
    (Nearly $instance.transform.position.x 0) -and
    (Nearly $instance.transform.position.y 0) -and
    (Nearly $instance.transform.position.z 0)) `
    'A fixed area effect must use the accepted target coordinates, including zero.'
$caster.SkillReleasePoint = $null
$config.释放方式 = [技能释放方式]::玩家释放节点
Assert-True (-not [RemoteSkillCastPresenter]::TryPlay(
    $message, $caster, $config, 0, [ref]$instance, [ref]$reason) -and
    $reason -eq 'missing_release_node') `
    'A missing bound release point must not fall back to an old model node.'

$config.释放方式 = [技能释放方式]::怪物目标位置
$animator = [UnityEngine.Animator]::new()
$animator.parameters = @([UnityEngine.AnimatorControllerParameter]@{
    name='Cast'; type=[UnityEngine.AnimatorControllerParameterType]::Trigger
})
$caster.VisualAnimator = $animator
$config.远端动画触发器 = 'Cast'
Assert-True ([RemoteSkillCastPresenter]::TryPlay(
    $message, $caster, $config, 0, [ref]$instance, [ref]$reason) -and
    $animator.Triggers -eq 1) `
    'Only an explicitly mapped Animator trigger should play.'
$config.远端动画触发器 = ''
[RemoteSkillCastPresenter]::TryPlay(
    $message, $caster, $config, 0, [ref]$instance, [ref]$reason) | Out-Null
Assert-True ($animator.Triggers -eq 1) `
    'Unmapped skills must not trigger a generic attack animation.'

$playsBeforeSeek = $particle.PlayCount
Assert-True ([RemoteSkillCastPresenter]::TryPlay(
    $message, $caster, $config, 2, [ref]$instance, [ref]$reason) -and
    (Nearly $particle.SimulatedSeconds 2) -and
    $particle.IsPlaying -and
    $particle.PlayCount -eq ($playsBeforeSeek + 2)) `
    'Particle visuals should keep playing after advancing by elapsed time.'

$effect = [技能粒子特效]::new()
$prefab.Components.Add($effect)
$playsBeforeWrappedSeek = $particle.PlayCount
Assert-True ([RemoteSkillCastPresenter]::TryPlay(
    $message, $caster, $config, 0.01, [ref]$instance, [ref]$reason) -and
    $effect.Plays -eq 1 -and
    $particle.IsPlaying -and
    $particle.PlayCount -eq ($playsBeforeWrappedSeek + 1)) `
    'Wrapped particle effects should also resume after advancing.'
$playsBeforeExpiry = $particle.PlayCount
Assert-True (-not [RemoteSkillCastPresenter]::TryPlay(
    $message, $caster, $config, 5, [ref]$instance, [ref]$reason) -and
    $reason -eq 'effect_expired' -and
    $particle.PlayCount -eq $playsBeforeExpiry) `
    'Expired visuals must not restart.'

$cameraEffect = [UnityEngine.HS_VolumeChanger]::new()
$prefab.Components.Add($cameraEffect)
Assert-True ([RemoteSkillCastPresenter]::TryPlay(
    $message, $caster, $config, 0, [ref]$instance, [ref]$reason)) `
    'Observer visuals should still start with a camera effect on the prefab.'
$remoteCameraEffect = $instance.Components |
    Where-Object { $_ -is [UnityEngine.HS_VolumeChanger] } |
    Select-Object -First 1
Assert-True ($cameraEffect.enabled -and
    -not $remoteCameraEffect.enabled -and
    $remoteCameraEffect.OnEnableCount -eq 0 -and
    $instance.ActiveInHierarchy -and
    $instance.transform.parent -eq $null -and
    [UnityEngine.Object]::LastInstantiationParent.owner.Destroyed) `
    'Observer camera effects must be disabled before activation without changing the prefab.'

Write-Output 'PASS: observer origin, target location, particle playback and camera isolation.'
