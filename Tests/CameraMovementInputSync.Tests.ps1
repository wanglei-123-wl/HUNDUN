$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$camera = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/ThirdPersonCameraFollow.cs') -Raw
$input = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/LocalPlayerInputController.cs') -Raw
$sample = [regex]::Match($camera, '(?s)    public void SampleInput\(\).*?(?=    private void LateUpdate\(\))')
$update = [regex]::Match($input, '(?s)    private void Update\(\).*?(?=    public void Bind\()')
if (-not $sample.Success -or -not $update.Success -or
    $camera.IndexOf('SampleInput();', $sample.Index + $sample.Length) -lt 0 -or
    $update.Value.IndexOf('cameraFollow?.SampleInput();') -lt 0 -or
    $update.Value.IndexOf('cameraFollow?.SampleInput();') -gt $update.Value.IndexOf('UpdateMovement(CreateInputFrame(), MovementNow)') -or
    $input -notmatch 'CameraYaw = cameraYaw' -or
    $input -notmatch 'cameraFollow\.CameraYaw') {
    throw 'Camera input must be sampled before movement and reused for the render frame.'
}

$harness = @'
using System;
using UnityEngine;
namespace UnityEngine
{
    public static class Time { public static int frameCount; }
    public static class Input
    {
        public static bool LeftHeld, RightHeld, LeftDown, RightDown, LeftUp, RightUp;
        public static float MouseX, MouseY;
        public static bool GetMouseButton(int index) { return index == 0 ? LeftHeld : RightHeld; }
        public static bool GetMouseButtonDown(int index) { return index == 0 ? LeftDown : RightDown; }
        public static bool GetMouseButtonUp(int index) { return index == 0 ? LeftUp : RightUp; }
        public static float GetAxisRaw(string axis) { return axis == "Mouse X" ? MouseX : MouseY; }
    }
    public static class Mathf
    {
        public static float Clamp(float value, float min, float max)
        { return Math.Max(min, Math.Min(max, value)); }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return new Vector2(); } }
        public float magnitude { get { return (float)Math.Sqrt(x * x + y * y); } }
    }
}
public static class GameplayUiInputGuard { public static bool BlocksGameplayInput; }
public sealed class CameraSampleHarness
{
    public float mouseXSpeed = 2f, mouseYSpeed = 1.5f;
    public float mouseDragThreshold = 4f, minPitch = -80f, maxPitch = 80f;
    private float yaw, pitch = 20f, characterLookYaw, leftDragDistance, rightDragDistance;
    private bool leftDragging, rightDragging;
    private int lastSampledInputFrame = -1;
    public bool HasTarget = true, Captured;
    public float CameraYaw { get { return yaw; } }
    public bool CharacterLookActive { get { return rightDragging && !GameplayUiInputGuard.BlocksGameplayInput; } }
    private object GetFollowTarget() { return HasTarget ? new object() : null; }
    private void SetCursorCaptured(bool value) { Captured = value; }
__SAMPLE__
}
public static class CameraMovementInputSyncTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    public static void Run()
    {
        var camera = new CameraSampleHarness();
        UnityEngine.Time.frameCount = 1;
        UnityEngine.Input.RightHeld = true;
        UnityEngine.Input.RightDown = true;
        UnityEngine.Input.MouseX = 2f;
        camera.SampleInput();
        Check(camera.CameraYaw == 0f && !camera.CharacterLookActive,
            "Right click below the drag threshold must not rotate the camera.");

        UnityEngine.Time.frameCount = 2;
        UnityEngine.Input.RightDown = false;
        UnityEngine.Input.MouseX = 3f;
        camera.SampleInput();
        Check(camera.CameraYaw == 6f && camera.CharacterLookActive && camera.Captured,
            "Movement input must see the current frame's right-drag yaw.");
        camera.SampleInput();
        Check(camera.CameraYaw == 6f, "The render frame must not apply the mouse delta again.");

        UnityEngine.Time.frameCount = 3;
        UnityEngine.Input.RightHeld = false;
        UnityEngine.Input.RightUp = true;
        UnityEngine.Input.LeftHeld = true;
        UnityEngine.Input.LeftDown = true;
        UnityEngine.Input.MouseX = 5f;
        camera.SampleInput();
        Check(camera.CameraYaw == 16f && !camera.CharacterLookActive,
            "Left drag may rotate the view without enabling right-drag character look.");

        UnityEngine.Time.frameCount = 4;
        GameplayUiInputGuard.BlocksGameplayInput = true;
        UnityEngine.Input.LeftDown = false;
        camera.SampleInput();
        Check(camera.CameraYaw == 16f && !camera.CharacterLookActive && !camera.Captured,
            "Blocking gameplay UI must stop camera drag without changing the view.");

        UnityEngine.Time.frameCount = 5;
        GameplayUiInputGuard.BlocksGameplayInput = false;
        UnityEngine.Input.LeftHeld = false;
        UnityEngine.Input.RightUp = false;
        camera.HasTarget = false;
        camera.SampleInput();
        Check(camera.CameraYaw == 16f, "An unbound camera must not consume mouse input.");
    }
}
'@.Replace('__SAMPLE__', $sample.Value)

Add-Type -TypeDefinition $harness -IgnoreWarnings
[CameraMovementInputSyncTests]::Run()
Write-Output 'Camera movement input sync tests passed.'
