$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $root (
    'Assets/Scripts/Cultivation/Presentation/RefiningQuantityConfirmationView.cs')) -Raw
$stubs = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute {}
    public struct Vector2 {
        public float x, y;
        public Vector2(float a, float b) { x = a; y = b; }
        public static Vector2 zero => new(0, 0);
        public static Vector2 one => new(1, 1);
    }
    public struct Color {
        public Color(float r, float g, float b, float a) {}
        public static Color white => new(1, 1, 1, 1);
    }
    public struct Rect { public float width; }
    public class Component {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
    }
    public class MonoBehaviour : Component {
        protected static void Destroy(GameObject target) { target.SetActive(false); }
    }
    public class Transform : Component {
        public Transform parent;
        public readonly List<Transform> children = new();
        public void SetParent(Transform value, bool worldPositionStays) {
            parent = value;
            value.children.Add(this);
        }
    }
    public class RectTransform : Transform {
        public Vector2 anchorMin, anchorMax, offsetMin, offsetMax;
        public Vector2 sizeDelta, anchoredPosition;
        public Rect rect = new() { width = 500f };
    }
    public class GameObject {
        public static readonly List<GameObject> All = new();
        public readonly string name;
        private readonly Dictionary<Type, Component> components = new();
        public bool activeSelf = true;
        public Transform transform;
        public GameObject(string name, params Type[] types) {
            this.name = name;
            All.Add(this);
            foreach (Type type in types) Add(type);
            if (transform == null) Add(typeof(RectTransform));
        }
        private Component Add(Type type) {
            Component result = (Component)Activator.CreateInstance(type);
            result.gameObject = this;
            components.Add(type, result);
            if (result is Transform value) transform = value;
            return result;
        }
        public T AddComponent<T>() where T : Component, new() {
            var value = new T { gameObject = this };
            components.Add(typeof(T), value);
            return value;
        }
        public T GetComponent<T>() where T : class {
            foreach (Component value in components.Values)
                if (value is T found) return found;
            return null;
        }
        public void SetActive(bool value) { activeSelf = value; }
        public static GameObject Named(string name) => All.Find(o => o.name == name);
    }
    public class Canvas : Component {
        public Canvas rootCanvas => this;
        public int sortingOrder;
        public bool overrideSorting;
    }
    public class GraphicRaycaster : Component {}
    public class RectMask2D : Component {}
    public enum KeyCode { Escape }
    public static class Input {
        public static bool escape;
        public static bool GetKeyDown(KeyCode code) => escape && code == KeyCode.Escape;
    }
    public static class Mathf {
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int Clamp(int a, int min, int max) => Math.Clamp(a, min, max);
    }
}
namespace UnityEngine.UI
{
    public class Graphic : Component {
        public Color color;
        public bool raycastTarget;
    }
    public class Image : Graphic {}
    public class Button : Component {
        public readonly ClickEvent onClick = new();
        public bool interactable = true;
    }
    public class ClickEvent {
        private event Action listeners;
        public void AddListener(Action callback) { listeners += callback; }
        public void Invoke() { listeners?.Invoke(); }
    }
}
namespace TMPro
{
    public enum TextAlignmentOptions { Center }
    public class TMP_FontAsset {}
    public class TMP_Text : UnityEngine.UI.Graphic {
        public string text;
        public TMP_FontAsset font;
        public float fontSize, fontSizeMin, fontSizeMax;
        public bool enableAutoSizing;
        public TextAlignmentOptions alignment;
        public UnityEngine.RectTransform rectTransform =>
            GetComponent<UnityEngine.RectTransform>();
    }
    public class TextMeshProUGUI : TMP_Text {}
    public class TMP_InputField : UnityEngine.Component {
        public enum LineType { SingleLine }
        public enum ContentType { IntegerNumber }
        public UnityEngine.RectTransform textViewport;
        public TextMeshProUGUI textComponent;
        public UnityEngine.UI.Graphic targetGraphic;
        public LineType lineType;
        public ContentType contentType;
        public int characterLimit;
        public string text;
        public readonly ValueEvent onValueChanged = new();
        public void SetTextWithoutNotify(string value) { text = value; }
        public void Edit(string value) { text = value; onValueChanged.Invoke(value); }
    }
    public class ValueEvent {
        private event Action<string> listeners;
        public void AddListener(Action<string> handler) { listeners += handler; }
        public void Invoke(string value) { listeners?.Invoke(value); }
    }
}
public static class GameplayUiInputGuard {
    public static readonly HashSet<UnityEngine.GameObject> Dialogs = new();
    public static void RegisterModal(UnityEngine.GameObject value) => Dialogs.Add(value);
    public static void UnregisterModal(UnityEngine.GameObject value) => Dialogs.Remove(value);
}
'@
$source = $source -replace '(?m)^using [^;]+;\r?\n', ''
Add-Type -TypeDefinition ($stubs + "`n" + $source) -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$reason) {
    if (-not $condition) { throw $reason }
}
$canvasRoot = [UnityEngine.GameObject]::new(
    'root', @([UnityEngine.RectTransform], [UnityEngine.Canvas]))
$canvas = $canvasRoot.GetComponent[UnityEngine.Canvas]()
$font = [TMPro.TMP_FontAsset]::new()
$view = [Game.Cultivation.Presentation.RefiningQuantityConfirmationView]::Create(
    $canvas, $font)
$input = [UnityEngine.GameObject]::Named('填写数量').GetComponent[
    TMPro.TMP_InputField]()
$confirm = [UnityEngine.GameObject]::Named('确认炼化数量').GetComponent[
    UnityEngine.UI.Button]()
$all = [UnityEngine.GameObject]::Named('全部数量').GetComponent[
    UnityEngine.UI.Button]()
$increase = [UnityEngine.GameObject]::Named('增加数量').GetComponent[
    UnityEngine.UI.Button]()
$decrease = [UnityEngine.GameObject]::Named('减少数量').GetComponent[
    UnityEngine.UI.Button]()
$cancel = [UnityEngine.GameObject]::Named('取消炼化选择').GetComponent[
    UnityEngine.UI.Button]()
Assert-True ($null -ne $input -and $null -ne $confirm -and
    $input.contentType -eq [TMPro.TMP_InputField+ContentType]::IntegerNumber -and
    $input.lineType -eq [TMPro.TMP_InputField+LineType]::SingleLine -and
    $null -ne $input.textViewport -and $null -ne $input.textComponent) `
    'The quantity entry must be a wired integer-only single-line input.'
$view.Show('s:123', '灵石', 5, $null)
Assert-True ($input.text -eq '5' -and $confirm.interactable -and
    [GameplayUiInputGuard]::Dialogs.Contains($view.gameObject)) `
    'Opening must use stack count and block gameplay input.'
$input.Edit('')
Assert-True (-not $confirm.interactable) 'An empty count must block confirmation.'
$input.Edit('0')
Assert-True (-not $confirm.interactable) 'Zero must block confirmation.'
$input.Edit('6')
Assert-True (-not $confirm.interactable) 'Above available must block confirmation.'
$input.Edit('2')
Assert-True ($confirm.interactable) 'In-range manual entry must enable confirmation.'
$increase.onClick.Invoke()
Assert-True ($input.text -eq '3') 'Increment must update editable quantity.'
$decrease.onClick.Invoke()
Assert-True ($input.text -eq '2') 'Decrement must update editable quantity.'
$all.onClick.Invoke()
Assert-True ($input.text -eq '5') 'All must choose the available stack count.'
$increase.onClick.Invoke()
Assert-True ($input.text -eq '5') 'Increment cannot exceed the stack count.'
$view.Show('s:123', '灵石', [int]::MaxValue, $null)
$increase.onClick.Invoke()
Assert-True ($input.text -eq [int]::MaxValue.ToString()) `
    'Increment at the integer limit must not wrap the count.'
$limit = [System.Func[int,bool]]{ param($amount) return $amount -le 3 }
$view.Show('s:123', '灵石', 5, $limit)
Assert-True (-not $confirm.interactable) `
    'A count over the server batch limit must be disabled while typing.'
$input.Edit('3')
Assert-True ($confirm.interactable) `
    'A valid count within the batch limit must be confirmable.'
$view.ShowError('server changed')
Assert-True (-not $confirm.interactable) 'A changed catalog must block confirmation.'
$input.Edit('1')
$received = [System.Collections.Generic.List[string]]::new()
$handler = [System.Action[string,int]]{
    param($identity, $amount)
    $received.Add("$identity/$amount")
}
$view.add_Confirmed($handler)
$confirm.onClick.Invoke()
Assert-True ($received.Count -eq 1 -and $received[0] -eq 's:123/1') `
    'Confirmation must return the local stack identity and selected count.'
$cancel.onClick.Invoke()
Assert-True (-not $view.gameObject.activeSelf -and
    -not [GameplayUiInputGuard]::Dialogs.Contains($view.gameObject)) `
    'Cancel must hide the overlay and restore gameplay input.'
Write-Output 'PASS: refining quantity dialog input, controls and modal lifecycle.'
