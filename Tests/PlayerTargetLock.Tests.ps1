$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$targeting = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Combat/MonsterTargetingController.cs') -Raw
$selection = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Combat/CombatTargetSelection.cs') -Raw
$combat = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Combat/LocalPlayerCombatController.cs') -Raw
$input = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/LocalPlayerInputController.cs') -Raw

function Read-Method([string] $source, [string] $signature) {
    $start = $source.IndexOf($signature)
    if ($start -lt 0) { throw "Missing production method: $signature" }
    $body = $source.IndexOf('{', $start)
    $depth = 1
    for ($end = $body + 1; $end -lt $source.Length; $end++) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        if ($depth -eq 0) { return $source.Substring($start, $end - $start + 1) }
    }
    throw "Unclosed production method: $signature"
}

$combatMethods = @(
    [regex]::Match($combat, '(?s)internal CombatTargetSelection LockedTarget =>.*?;').Value
    (Read-Method $combat 'internal void ClearCombatTarget(')
    (Read-Method $combat 'internal bool TryPickTargetUnderPointer(')
    (Read-Method $combat 'internal void ToggleClickedTargetLock(')
    (Read-Method $combat 'public bool TrySendBasicAttack(')
    (Read-Method $combat 'private void FaceAttackTarget(')
) -join "`n"
$clickMethod = Read-Method $input 'private void HandleTargetRightClick('

$harness = @'
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)] public class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public class MinAttribute : Attribute { public MinAttribute(float n) { } }
    [AttributeUsage(AttributeTargets.Field)] public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    public class GameObject
    {
        public readonly List<object> Components = new List<object>();
        public readonly Transform transform;
        public bool activeSelf = true;
        public int layer;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public GameObject() { transform = new Transform { gameObject = this }; }
        public void SetActive(bool active) { activeSelf = active; }
    }
    public class Transform
    {
        public GameObject gameObject;
        public Transform parent;
        public Vector3 position, forward = new Vector3(0, 0, 1);
        public bool TryGetComponent<T>(out T value) where T : class
        { value = gameObject.Components.OfType<T>().FirstOrDefault(); return value != null; }
        public T GetComponentInParent<T>() where T : class
        {
            for (Transform t = this; t != null; t = t.parent)
                if (t.TryGetComponent(out T result)) return result;
            return null;
        }
    }
    public class MonoBehaviour
    {
        private static readonly List<MonoBehaviour> Objects = new List<MonoBehaviour>();
        public readonly GameObject gameObject = new GameObject();
        public Transform transform => gameObject.transform;
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
        public MonoBehaviour() { gameObject.Components.Add(this); Objects.Add(this); }
        public T GetComponent<T>() where T : class { transform.TryGetComponent(out T result); return result; }
        public T GetComponentInParent<T>() where T : class => transform.GetComponentInParent<T>();
        public static T[] FindObjectsOfType<T>() where T : MonoBehaviour => Objects.OfType<T>().Where(x => x.isActiveAndEnabled).ToArray();
        public static T FindObjectOfType<T>() where T : MonoBehaviour => FindObjectsOfType<T>().FirstOrDefault();
        public static void ResetObjects() { Objects.Clear(); }
    }
    public class Collider : MonoBehaviour { public bool isTrigger; }
    public class Camera : MonoBehaviour
    {
        public static Camera main;
        public static Camera[] allCameras => main == null ? new Camera[0] : new[] { main };
        public Ray ScreenPointToRay(Vector3 position) => new Ray();
    }
    public struct Ray { }
    public struct RaycastHit { public Collider collider; public float distance; }
    public enum QueryTriggerInteraction { Collide }
    public static class Physics
    {
        public static RaycastHit[] Hits = new RaycastHit[0];
        public static RaycastHit[] RaycastAll(Ray ray, float distance, int layers, QueryTriggerInteraction triggers)
            => Hits.Where(h => h.distance <= distance && (layers & (1 << h.collider.gameObject.layer)) != 0).ToArray();
    }
    public struct LayerMask { public int value; public static implicit operator LayerMask(int n) => new LayerMask { value = n }; public static implicit operator int(LayerMask m) => m.value; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public float sqrMagnitude => x*x + y*y + z*z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x-b.x, a.y-b.y, a.z-b.z);
        public static float Angle(Vector3 a, Vector3 b) => (float)(Math.Acos(Math.Clamp((a.x*b.x+a.y*b.y+a.z*b.z)/(a.magnitude*b.magnitude), -1f, 1f))*180d/Math.PI);
    }
    public static class Mathf
    {
        public const float Rad2Deg = (float)(180d/Math.PI);
        public static float Atan2(float a, float b) => (float)Math.Atan2(a,b);
        public static float Repeat(float a, float b) => a-(float)Math.Floor(a/b)*b;
    }
    public static class Input
    {
        public static bool Left, Right, Down, Up;
        public static Vector3 mousePosition;
        public static bool GetMouseButton(int index) => index == 0 ? Left : Right;
        public static bool GetMouseButtonDown(int index) => Down;
        public static bool GetMouseButtonUp(int index) => Up;
    }
}
namespace UnityEngine.EventSystems
{
    public class EventSystem
    {
        public static EventSystem current;
        public bool OverUi;
        public bool IsPointerOverGameObject() => OverUi;
    }
}
public class RemotePlayerSync : MonoBehaviour
{
    public long CharacterDbId;
    public bool IsDead;
    public GameObject SelectionRing = new GameObject();
    public void SetSelectionRingVisible(bool visible) { SelectionRing.SetActive(visible); }
}
public class MonsterNetworkView : MonoBehaviour
{
    public string EntityId;
    public bool Alive = true;
    public bool IsTargetable => Alive && isActiveAndEnabled;
    public GameObject SelectionRing = new GameObject();
    public void SetSelectionRingVisible(bool visible) { SelectionRing.SetActive(visible); }
}
public class ThirdPersonCameraFollow : MonoBehaviour { public bool IsRightMouseDragging; }
public class LootClickBridge : MonoBehaviour { public bool TryGetLootUnderPointer(out object loot) { loot = null; return false; } }
public static class MapTeleporterInteraction { public static object FindTeleporterUnderPointer(Camera c, Vector3 p) => null; }
public class LocalPlayerVisualSmoother
{
    public Vector3 VisualWorldPosition;
    public float FacingYaw;
    public void ClearCombatFacing() { }
    public void FaceCombatTargetImmediately(float yaw) { FacingYaw = yaw; }
}
public class CombatActionRequest { public long actionSequence, clientTick; public string actionId, targetType, targetId; public float aimYaw; }
public class NetworkManager
{
    public static NetworkManager Instance = new NetworkManager();
    public bool IsConnected = true;
    public CombatActionRequest LastAttack;
    public bool SendCombatAction(CombatActionRequest request) { LastAttack = request; return true; }
}
public class LocalPlayerCombatController : MonoBehaviour
{
    public MonsterTargetingController targetingController;
    public LocalPlayerVisualSmoother visualSmoother = new LocalPlayerVisualSmoother();
    public bool combatEnabled = true;
    private long nextActionSequence;
    private string basicAttackActionId = "basic_attack";
    private readonly List<int> recentAttacks = new List<int>();
    private void ResolveReferences() { }
    private void TrackAttackForLock(NetworkManager network, string action, long sequence) { }
__COMBAT__
}
public class LocalPlayerInputController : MonoBehaviour
{
    public LocalPlayerCombatController combatController;
    public ThirdPersonCameraFollow cameraFollow = new ThirdPersonCameraFollow();
    private CombatTargetSelection rightClickTarget;
    private void ResolveCombatController() { }
    public void Tick(bool blocked = false) { HandleTargetRightClick(blocked); }
__CLICK__
}
public static class PlayerTargetLockTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Update(MonsterTargetingController targeting)
    { typeof(MonsterTargetingController).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(targeting, null); }
    private static Collider Body(MonoBehaviour owner)
    { var collider = new Collider(); collider.transform.parent = owner.transform; return collider; }
    private static void PointAt(Collider body)
    { Physics.Hits = new[] { new RaycastHit { collider = body, distance = 3 } }; }
    private static void Click(LocalPlayerInputController input)
    {
        Input.Down = Input.Right = true; Input.Up = false; input.Tick();
        Input.Down = Input.Right = false; Input.Up = true; input.Tick(); Input.Up = false;
    }
    public static void Run()
    {
        MonoBehaviour.ResetObjects(); Camera.main = new Camera();
        var targeting = new MonsterTargetingController();
        var combat = new LocalPlayerCombatController { targetingController = targeting };
        var input = new LocalPlayerInputController { combatController = combat };
        var player = new RemotePlayerSync { CharacterDbId = 41 };
        player.transform.position = new Vector3(10, 0, 0); player.SelectionRing.SetActive(false);
        var body = Body(player); PointAt(body); Click(input);
        Check(targeting.LockedTarget.EntityType == "player" && targeting.LockedTarget.EntityId == "41" && player.SelectionRing.activeSelf,
            "Right click on a player's model collider must lock the root player and show its ring.");
        var monster = new MonsterNetworkView { EntityId = "monster_1" }; monster.SelectionRing.SetActive(false);
        var monsterBody = Body(monster); PointAt(monsterBody);
        Check(targeting.TryResolveCombatTarget(out var selected) && selected.EntityId == "41",
            "A locked player must remain the combat target after moving the pointer onto a monster.");
        Check(combat.TrySendBasicAttack(1, 0) && NetworkManager.Instance.LastAttack.targetType == "player"
            && NetworkManager.Instance.LastAttack.targetId == "41" && Math.Abs(NetworkManager.Instance.LastAttack.aimYaw - 90) < .001f
            && Math.Abs(combat.visualSmoother.FacingYaw - 90) < .001f,
            "Basic attack requests and character facing must use the locked player's identity and yaw.");
        Click(input);
        Check(targeting.LockedTarget.EntityType == "monster" && !player.SelectionRing.activeSelf && monster.SelectionRing.activeSelf,
            "Switching from player to monster must hide the old ring and show the new ring.");
        PointAt(body); Click(input);
        Check(!monster.SelectionRing.activeSelf && player.SelectionRing.activeSelf, "Switching back to a player must clear the monster ring.");
        Click(input);
        Check(!targeting.LockedTarget.IsValid && !player.SelectionRing.activeSelf, "Right clicking the same player again must cancel its lock.");
        Click(input); player.IsDead = true; Update(targeting);
        Check(!targeting.LockedTarget.IsValid && !player.SelectionRing.activeSelf, "A player's death must release its lock and ring.");
        Check(!targeting.TryPickTargetUnderPointer(out _), "Dead players must not be selectable.");
        player.IsDead = false; Click(input); player.gameObject.SetActive(false); Update(targeting);
        Check(!targeting.LockedTarget.IsValid && !player.SelectionRing.activeSelf, "A leaving player must release its lock and ring.");
        player.gameObject.SetActive(true); Click(input); player.CharacterDbId = 42; Update(targeting);
        Check(!targeting.LockedTarget.IsValid && !player.SelectionRing.activeSelf, "A stale character identity must invalidate the lock.");
        player.CharacterDbId = 41;
        var legacy = new RemotePlayerSync { CharacterDbId = 0, enabled = false }; legacy.transform.parent = player.transform;
        PointAt(Body(legacy)); Click(input);
        Check(targeting.LockedTarget.TargetTransform == player.transform, "A disabled model-side sync must not hide the real parent player.");
        combat.ClearCombatTarget();
        var wall = new Collider(); Physics.Hits = new[] { new RaycastHit { collider = body, distance = 3 }, new RaycastHit { collider = wall, distance = 1 } };
        Click(input); Check(!targeting.LockedTarget.IsValid, "Right-click locking must not select a player through a solid wall.");
        PointAt(body); input.cameraFollow.IsRightMouseDragging = true; Click(input); input.cameraFollow.IsRightMouseDragging = false;
        Check(!targeting.LockedTarget.IsValid, "Right-drag camera movement must not lock a player.");
        EventSystem.current = new EventSystem { OverUi = true }; Click(input); EventSystem.current = null;
        Check(!targeting.LockedTarget.IsValid, "Clicks over UI must not lock a player.");
        Input.Left = true; Click(input); Input.Left = false;
        Check(!targeting.LockedTarget.IsValid, "Both-button movement must not lock a player.");
        Click(input); combat.combatEnabled = false; Click(input);
        Check(targeting.LockedTarget.IsValid, "Disabled combat input must not toggle an existing selection.");
        combat.combatEnabled = true; combat.ClearCombatTarget();
        Check(!targeting.LockedTarget.IsValid && !player.SelectionRing.activeSelf, "Clearing combat must clear the player's ring.");
    }
}
'@
$harness = $harness.Replace('__COMBAT__', $combatMethods).Replace('__CLICK__', $clickMethod)
$sources = (@($targeting, $selection, $harness) -join "`n") -replace '(?m)^using [^;]+;\r?\n', ''
$sources = "using System; using System.Collections.Generic; using System.Globalization; using System.Linq; using UnityEngine; using UnityEngine.EventSystems;`n" + $sources
Add-Type -TypeDefinition $sources -IgnoreWarnings -WarningAction SilentlyContinue
[PlayerTargetLockTests]::Run()
Write-Output 'PASS: player right-click locking, cancellation, target switching, attack identity/facing, lifecycle invalidation and input guards.'
