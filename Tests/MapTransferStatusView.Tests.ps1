$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/MapTransferStatusView.cs') -Raw
# Exercise the actual timing, destination and error methods without native Canvas construction.
$source = $source -replace '(?m)^using [^;]+;\r?\n', ''
$source = [regex]::Replace($source,
    '(?s)    public static MapTransferStatusView Create\(Transform owner\).*?(?=    public void BeginDestination)', '')
$stubs = @'
using UnityEngine;
using TMPro;
using Game.Minimap;
namespace UnityEngine {
    public sealed class SerializeField : System.Attribute {}
    public sealed class MinAttribute : System.Attribute { public MinAttribute(float value) {} }
    public sealed class GameObject {
        public bool activeInHierarchy;
        public void SetActive(bool active) { activeInHierarchy=active; }
    }
    public class MonoBehaviour {
        public GameObject gameObject = new GameObject();
        public static T FindObjectOfType<T>(bool inactive) where T:new() => new T();
        public static void Destroy(object o) {}
    }
    public static class Time { public static float realtimeSinceStartup; }
    public static class Mathf { public static float Max(float a,float b) => System.Math.Max(a,b); }
}
namespace TMPro { public sealed class TMP_Text { public bool richText; public string text; } }
namespace Game.Minimap {
    public sealed class MinimapPresenter { public string GetMapDisplayName(string scene,string map) => "长风村"; }
}
public static class GameplayUiInputGuard {
    public static bool Blocked;
    public static void RegisterModal(object root, bool blocksWorldActions = false) { if (!blocksWorldActions) throw new System.Exception("Transfer status must retain its world lock."); Blocked=true; }
    public static void UnregisterModal(object root) { Blocked=false; }
}
'@
Add-Type -TypeDefinition ($stubs + $source) -IgnoreWarnings | Out-Null
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
function Assert-True($condition, [string]$message) { if (!$condition) { throw $message } }
function Update-View($view) { $view.GetType().GetMethod('Update',$flags).Invoke($view,@()) | Out-Null }
$view = [MapTransferStatusView]::new()
$label = [TMPro.TMP_Text]::new()
$view.GetType().GetField('label',$flags).SetValue($view,$label)
[UnityEngine.Time]::realtimeSinceStartup = 10
$view.BeginDestination('world_002','map_001')
$view.Show('正在检查地图资源')
Assert-True ($view.IsVisible -and [GameplayUiInputGuard]::Blocked -and
    $label.text.Contains('正在前往长风村') -and $label.text.Contains('检查地图资源')) 'Destination and progress must use the bound label and block gameplay.'
[UnityEngine.Time]::realtimeSinceStartup = 12
$view.Show('正在准备地图')
Assert-True (!$view.MinimumDisplayElapsed) 'Progress updates must not release the minimum early.'
[UnityEngine.Time]::realtimeSinceStartup = 13.5
Assert-True ($view.MinimumDisplayElapsed) 'Progress updates must not restart the original minimum timer.'

$view.ShowFailure('地图资源不完整', $false)
[UnityEngine.Time]::realtimeSinceStartup = 16.9
Update-View $view
Assert-True ($view.IsVisible -and $label.text.Contains('资源不完整')) 'Recoverable error must remain readable in the same label.'
[UnityEngine.Time]::realtimeSinceStartup = 17
Update-View $view
Assert-True (!$view.IsVisible -and ![GameplayUiInputGuard]::Blocked) 'Dismissed error must release its modal input block.'

$view.BeginDestination('world_002','map_001')
$view.Show('正在进入地图')
Assert-True (!$view.MinimumDisplayElapsed) 'Every transfer must start a fresh minimum timer.'
$view.ShowFailure('正在重新连接', $true)
[UnityEngine.Time]::realtimeSinceStartup = 100
Update-View $view
Assert-True ($view.IsVisible -and [GameplayUiInputGuard]::Blocked -and
    $label.text.Contains('重新连接')) 'Recovery error cannot auto-dismiss into an unready map.'
$view.Release()
Assert-True (!$view.IsVisible -and ![GameplayUiInputGuard]::Blocked) 'Releasing the view must remove its modal state.'
Write-Output 'PASS: actual destination label, minimum-time reset, progress timing, error readability, persistent recovery and modal cleanup.'
