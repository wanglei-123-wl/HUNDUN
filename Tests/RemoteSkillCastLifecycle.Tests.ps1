$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$timeline = Get-Content (Join-Path $root 'Assets/Scripts/Combat/RemoteSkillCastTimeline.cs') -Raw
$tracker = Get-Content (Join-Path $root 'Assets/Scripts/Combat/RemoteSkillCastEffectTracker.cs') -Raw
$entity = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/EntityManager.cs') -Raw
if (($entity -notmatch 'remoteSkillTimeline\.BeginSession\(activeConnectionEpoch,') -or
    ($entity -notmatch 'remoteSkillTimeline\.EndSession\(connectionEpoch\)') -or
    ($entity -notmatch 'remoteSkillEffects\.Track\(casterId,') -or
    ($entity -notmatch 'remoteSkillEffects\.RemoveCaster\(characterDbId\)') -or
    ($entity -notmatch 'remoteSkillEffects\.Clear\(\)')) {
    throw 'Observer timing or effect cleanup is not attached to session lifecycle.'
}

$stubs = @'
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class GameObject { public bool Destroyed; }
    public class Object {
        public static void Destroy(GameObject instance) { instance.Destroyed = true; }
    }
}
'@
$source = "using UnityEngine;`n" + $stubs + "`n" +
    ($timeline -replace '(?m)^using [^\r\n]+;\r?\n', '') + "`n" +
    ($tracker -replace '(?m)^using [^\r\n]+;\r?\n', '')
Add-Type -TypeDefinition $source -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Nearly($actual, $expected) {
    return [Math]::Abs($actual - $expected) -lt 0.001
}

$clock = [RemoteSkillCastTimeline]::new()
$elapsed = 0.0
Assert-True (-not $clock.TryGetMinimumElapsedSeconds(
    7, 99500, 1, [ref]$elapsed)) `
    'No aligned clock must report receive-only timing.'
$clock.BeginSession(7, 100000, 0)
Assert-True ($clock.TryGetMinimumElapsedSeconds(
    7, 99500, 1, [ref]$elapsed) -and (Nearly $elapsed 1.5)) `
    'Elapsed time must use the server timestamp and monotonic receive time.'
$clock.ObserveServerTime(7, 101800, 1)
Assert-True ($clock.TryGetMinimumElapsedSeconds(
    7, 100000, 1, [ref]$elapsed) -and (Nearly $elapsed 1.8)) `
    'A newer server timestamp must tighten the lower bound.'
$clock.ObserveServerTime(7, 100100, 1.1)
Assert-True ($clock.TryGetMinimumElapsedSeconds(
    7, 100000, 1.1, [ref]$elapsed) -and $elapsed -ge 1.8) `
    'Older timestamps must not rewind the lower bound.'
$clock.EndSession(6)
Assert-True ($clock.TryGetMinimumElapsedSeconds(
    7, 100000, 1.1, [ref]$elapsed)) `
    'An old disconnect must not erase the active session.'
$clock.EndSession(7)
Assert-True (-not $clock.TryGetMinimumElapsedSeconds(
    7, 100000, 1.1, [ref]$elapsed)) `
    'Disconnect must clear time state.'
$clock.BeginSession(8, 0, 2)
Assert-True (-not $clock.TryGetMinimumElapsedSeconds(
    8, 100000, 2, [ref]$elapsed)) `
    'Reentry without a timestamp must not inherit an old clock.'

$effects = [RemoteSkillCastEffectTracker]::new()
$attached = [UnityEngine.GameObject]::new()
$ground = [UnityEngine.GameObject]::new()
$effects.Track(101, $true, $attached)
$effects.Track(101, $false, $ground)
$effects.RemoveCaster(101)
Assert-True ($attached.Destroyed -and -not $ground.Destroyed) `
    'Leaving casters must release caster-bound effects but not fixed areas.'
$effects.Clear()
Assert-True ($ground.Destroyed) `
    'Leaving the scene must release fixed areas as well.'

Write-Output 'PASS: observer lower-bound clock, session reset and effect cleanup.'
