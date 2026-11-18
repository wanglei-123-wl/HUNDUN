$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$controller = Get-Content -LiteralPath (
    Join-Path $root 'Assets/怪物状态机/Ls003.controller') -Raw
$driverSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Monster/Animation/Ls003MonsterAnimatorDriver.cs') -Raw
$interfaceSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Monster/Animation/IMonsterAnimatorDriver.cs') -Raw

foreach ($parameter in @(
    @{ Name = 'IsMoving'; Type = 4 },
    @{ Name = 'Attack'; Type = 9 },
    @{ Name = 'Dead'; Type = 4 }
)) {
    $pattern = "(?m)^  - m_Name: $($parameter.Name)`r?`n    m_Type: $($parameter.Type)$"
    if ($controller -notmatch $pattern) {
        throw "Ls003 controller is missing $($parameter.Name)."
    }
}

foreach ($state in @('\u5F85\u673A', '\u884C\u8D70', '\u653B\u51FB', '\u6B7B\u4EA1')) {
    if ($controller -notmatch [regex]::Escape("m_Name: `"$state`"")) {
        throw "Ls003 controller is missing state $state."
    }
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
        protected T GetComponentInParent<T>() where T : class { return null; }
    }
    public static class Debug
    {
        public static void LogError(object message, object context) { throw new Exception(message.ToString()); }
    }
    public sealed class Animator
    {
        public object runtimeAnimatorController = new object();
        public AnimatorControllerParameter[] parameters = {
            Param("IsMoving", AnimatorControllerParameterType.Bool),
            Param("Attack", AnimatorControllerParameterType.Trigger),
            Param("Dead", AnimatorControllerParameterType.Bool)
        };
        public readonly Dictionary<int, bool> Bools = new Dictionary<int, bool>();
        public int CurrentState;
        public int AttackTriggers;
        public int AttackReplays;
        public int Resets;

        private static AnimatorControllerParameter Param(string name, AnimatorControllerParameterType type) =>
            new AnimatorControllerParameter { nameHash = StringToHash(name), type = type };
        public static int StringToHash(string name) => name.GetHashCode();
        public bool HasState(int layer, int state) => layer == 0;
        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer) =>
            new AnimatorStateInfo { fullPathHash = CurrentState };
        public void Play(int state, int layer, float offset)
        {
            CurrentState = state;
            if (state == StringToHash("Base Layer.攻击")) AttackReplays++;
        }
        public void Rebind() { Resets++; CurrentState = 0; Bools.Clear(); }
        public void Update(float delta) {}
        public void SetBool(int hash, bool value) { Bools[hash] = value; }
        public void ResetTrigger(int hash) {}
        public void SetTrigger(int hash) { AttackTriggers++; }
    }
}
public enum MonsterLocomotionMode { Grounded, TakingOff, Flying, Landing }
public sealed class MonsterNetworkView
{
    public event Action<int, int> OnHealthChanged;
    public void ApplyHealth(int hp, int maxHp) { OnHealthChanged?.Invoke(hp, maxHp); }
}
'@
$driverSource = $driverSource -replace '(?m)^using [^\r\n]+;\r?\n', ''
Add-Type -TypeDefinition ("using UnityEngine;`n" + $stubs + "`n" +
    $interfaceSource + "`n" + $driverSource) -IgnoreWarnings -ErrorAction Stop | Out-Null

$flags = [System.Reflection.BindingFlags]::Instance -bor
    [System.Reflection.BindingFlags]::NonPublic
$type = [Ls003MonsterAnimatorDriver]
$animatorField = $type.GetField('animator', $flags)
$networkField = $type.GetField('monsterNetworkView', $flags)
$awake = $type.GetMethod('Awake', $flags)
$enable = $type.GetMethod('OnEnable', $flags)
$disable = $type.GetMethod('OnDisable', $flags)
$idle = [UnityEngine.Animator]::StringToHash('Base Layer.待机')
$walk = [UnityEngine.Animator]::StringToHash('Base Layer.行走')
$attack = [UnityEngine.Animator]::StringToHash('Base Layer.攻击')
$moving = [UnityEngine.Animator]::StringToHash('IsMoving')
$dead = [UnityEngine.Animator]::StringToHash('Dead')

$animator = [UnityEngine.Animator]::new()
$network = [MonsterNetworkView]::new()
$driver = [Ls003MonsterAnimatorDriver]::new()
$animatorField.SetValue($driver, $animator)
$networkField.SetValue($driver, $network)
$awake.Invoke($driver, @()) | Out-Null
$enable.Invoke($driver, @()) | Out-Null
$driver.RebindAnimator()
if ($animator.Resets -ne 1 -or $animator.CurrentState -ne $idle -or
    $animator.Bools[$moving] -or $animator.Bools[$dead]) {
    throw 'Rebind must reset the view to a living idle state.'
}

$driver.BootstrapState('chase')
if ($animator.CurrentState -ne $walk -or -not $animator.Bools[$moving]) {
    throw 'Chase must enter the moving state.'
}
$driver.BootstrapState('return')
if ($animator.CurrentState -ne $walk) {
    throw 'Return must use the moving state.'
}
$driver.SetLocomotion([MonsterLocomotionMode]::Grounded)
if ($animator.Bools[$moving]) {
    throw 'Grounded must stop movement.'
}
$driver.BootstrapState('idle')
if ($animator.CurrentState -ne $idle) {
    throw 'Idle must play the idle state.'
}

$driver.PlayAction(1, 1)
if ($animator.AttackTriggers -ne 0) {
    throw 'Unsupported action slots must be ignored.'
}
$driver.PlayAction(0, 1)
if ($animator.AttackTriggers -ne 1) {
    throw 'Action slot zero must trigger an attack.'
}
$animator.CurrentState = $attack
$driver.PlayAction(0, 1)
if ($animator.AttackReplays -ne 1 -or $animator.AttackTriggers -ne 1) {
    throw 'A second attack must restart the current attack animation.'
}

$network.ApplyHealth(0, 100)
if (-not $animator.Bools[$dead] -or $animator.Bools[$moving]) {
    throw 'Zero health must stop movement and mark the monster dead.'
}
$driver.PlayAction(0, 1)
$driver.BootstrapState('chase')
$driver.SetLocomotion([MonsterLocomotionMode]::Flying)
$network.ApplyHealth(0, 100)
if ($animator.AttackTriggers -ne 1 -or $animator.CurrentState -ne $attack -or
    $animator.Bools[$moving]) {
    throw 'Death must be terminal until the view is reset.'
}

$driver.RebindAnimator()
if ($animator.Bools[$dead] -or $animator.CurrentState -ne $idle) {
    throw 'Rebind must allow the monster to respawn.'
}
$driver.BootstrapState('Dead')
$driver.PlayAction(0, 1)
if (-not $animator.Bools[$dead] -or $animator.AttackTriggers -ne 1) {
    throw 'A direct Dead state must block further attacks.'
}
$driver.ResetView()
if ($animator.Bools[$dead] -or $animator.CurrentState -ne $idle) {
    throw 'ResetView must restore idle after a direct Dead state.'
}
$driver.BootstrapState('attack')
if ($animator.AttackTriggers -ne 2) {
    throw 'The respawned monster must be able to attack.'
}
$disable.Invoke($driver, @()) | Out-Null
$network.ApplyHealth(0, 100)
if ($animator.Bools[$dead]) {
    throw 'OnDisable must unsubscribe from health changes.'
}
Write-Output 'PASS: Ls003 movement, attack replay, terminal death, reset, and health subscription.'
