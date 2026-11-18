$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$controller = Get-Content -LiteralPath (
    Join-Path $root 'Assets/怪物状态机/LS.controller') -Raw
$driverSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Monster/Animation/FlyingMonsterAnimatorDriver.cs') -Raw
$interfaceSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Monster/Animation/IMonsterAnimatorDriver.cs') -Raw
$prefab = Get-Content -LiteralPath (
    Join-Path $root 'Assets/预制体/怪物预制体/FlyingMonsterRoot.prefab') -Raw
$controllerGuid = [regex]::Match(
    (Get-Content -LiteralPath (Join-Path $root 'Assets/怪物状态机/LS.controller.meta') -Raw),
    '(?m)^guid: ([0-9a-f]+)$').Groups[1].Value

function Get-AnimatorBlock([string]$content, [string]$fileId) {
    return [regex]::Match($content,
        "(?ms)^--- !u!\d+ &$fileId\r?\n(.*?)(?=^--- !u!|\z)").Groups[1].Value
}

$machine = Get-AnimatorBlock $controller '2675512003241329806'
$transition = Get-AnimatorBlock $controller '8397657162820725170'
$deathState = Get-AnimatorBlock $controller '8397657162820725169'
if ($controller -notmatch '(?m)^  - m_Name: Dead\r?\n    m_Type: 4\r?\n    m_DefaultFloat: 0\r?\n    m_DefaultInt: 0\r?\n    m_DefaultBool: 0' -or
    $machine -notmatch 'm_AnyStateTransitions:\s+- \{fileID: 8397657162820725170\}' -or
    $transition -notmatch 'm_ConditionMode: 1\s+m_ConditionEvent: Dead' -or
    $transition -notmatch 'm_DstState: \{fileID: 8397657162820725169\}' -or
    $transition -notmatch '(?m)^\s*m_TransitionDuration: 0$' -or
    $transition -notmatch '(?m)^\s*m_HasExitTime: 0$' -or
    $transition -notmatch '(?m)^\s*m_CanTransitionToSelf: 0$' -or
    $deathState -notmatch 'm_Name: Death2\s+m_Speed:' -or
    $deathState -notmatch 'm_Transitions: \[\]' -or
    $prefab -notmatch "m_Controller: \{fileID: 9100000, guid: $controllerGuid") {
    throw 'Flying monster must have an immediate terminal Any State -> Death2 transition.'
}

$stubs = @'
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute {}
    public enum AnimatorControllerParameterType { Float, Int, Bool, Trigger }
    public sealed class AnimatorControllerParameter
    {
        public int nameHash;
        public AnimatorControllerParameterType type;
    }
    public struct AnimatorStateInfo { public int fullPathHash; }
    public class MonoBehaviour
    {
        protected T GetComponentInChildren<T>(bool includeInactive) where T : class { return null; }
    }
    public static class Debug
    {
        public static void LogError(object message, object context) { throw new Exception(message.ToString()); }
        public static void LogWarning(object message, object context) { throw new Exception(message.ToString()); }
    }
    public static class Mathf
    {
        public static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
        public static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        public static int Max(int left, int right) => Math.Max(left, right);
    }
    public sealed class Animator
    {
        public object runtimeAnimatorController = new object();
        public AnimatorControllerParameter[] parameters;
        public int CurrentState;
        public int DeathPlays;
        public int ActionTriggers;
        public Dictionary<int, bool> Bools = new Dictionary<int, bool>();
        public Dictionary<int, float> Floats = new Dictionary<int, float>();

        public Animator()
        {
            parameters = new [] {
                Param("LocomotionMode", AnimatorControllerParameterType.Int),
                Param("MoveSpeed", AnimatorControllerParameterType.Float),
                Param("ActionSlot", AnimatorControllerParameterType.Int),
                Param("ActionSpeed", AnimatorControllerParameterType.Float),
                Param("Action", AnimatorControllerParameterType.Trigger),
                Param("Dead", AnimatorControllerParameterType.Bool)
            };
        }
        private static AnimatorControllerParameter Param(string name, AnimatorControllerParameterType type)
        {
            return new AnimatorControllerParameter { nameHash = StringToHash(name), type = type };
        }
        public static int StringToHash(string value) => value.GetHashCode();
        public bool HasState(int layer, int state) => layer == 0;
        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer) =>
            new AnimatorStateInfo { fullPathHash = CurrentState };
        public void Play(int state, int layer, float offset)
        {
            CurrentState = state;
            if (state == StringToHash("Base Layer.Death2")) DeathPlays++;
        }
        public void Update(float delta) {}
        public void Rebind()
        {
            CurrentState = StringToHash("Base Layer.GroundIdle");
            Bools.Clear();
            Floats.Clear();
        }
        public void SetBool(int hash, bool value) { Bools[hash] = value; }
        public void SetFloat(int hash, float value) { Floats[hash] = value; }
        public void SetInteger(int hash, int value) {}
        public void ResetTrigger(int hash) {}
        public void SetTrigger(int hash) { ActionTriggers++; }
    }
}
public enum MonsterLocomotionMode { Grounded, TakingOff, Flying, Landing }
'@
$driverSource = $driverSource -replace '(?m)^using [^\r\n]+;\r?\n', ''
Add-Type -TypeDefinition ("using UnityEngine;`n" + $stubs + "`n" + $interfaceSource + "`n" + $driverSource) `
    -IgnoreWarnings -ErrorAction Stop | Out-Null

$deathHash = [UnityEngine.Animator]::StringToHash('Base Layer.Death2')
$deadParam = [UnityEngine.Animator]::StringToHash('Dead')
$moveParam = [UnityEngine.Animator]::StringToHash('MoveSpeed')
foreach ($start in @('idle', 'takeoff', 'chase', 'attack', 'landing')) {
    $animator = [UnityEngine.Animator]::new()
    $driver = [FlyingMonsterAnimatorDriver]::new()
    $driver.BindAnimator($animator)
    $driver.RebindAnimator()
    $driver.BootstrapState($start)
    $driver.BootstrapState('Dead')

    if ($animator.CurrentState -ne $deathHash -or $animator.DeathPlays -ne 1 -or
        -not $animator.Bools[$deadParam] -or $animator.Floats[$moveParam] -ne 0) {
        throw "$start must switch immediately to Death2 with no movement."
    }

    $driver.SetLocomotion([MonsterLocomotionMode]::Flying)
    $driver.SetMoveSpeed(1)
    $driver.PlayAction(0, 1)
    $driver.BootstrapState('idle')
    $driver.BootstrapState('Dead')
    if ($animator.CurrentState -ne $deathHash -or $animator.DeathPlays -ne 1 -or
        $animator.ActionTriggers -ne 0 -or $animator.Floats[$moveParam] -ne 0) {
        throw "$start death must not replay or be overwritten by movement/attack."
    }

    $driver.RebindAnimator()
    if ($animator.Bools[$deadParam]) { throw 'New life must clear Dead.' }
    $driver.BootstrapState('chase')
    $driver.PlayAction(0, 1)
    if ($animator.CurrentState -ne [UnityEngine.Animator]::StringToHash('Base Layer.Flying') -or
        $animator.ActionTriggers -ne 1) {
        throw 'New life must be able to move and attack.'
    }
}
Write-Output 'PASS: flying monster immediately dies from every state, stays dead, and resets on new life.'
