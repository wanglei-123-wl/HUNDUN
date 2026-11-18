using System;

[Flags]
public enum PlayerInputKeys
{
    None = 0,
    W = 1 << 0,
    A = 1 << 1,
    S = 1 << 2,
    D = 1 << 3,
    Shift = 1 << 4,
    Space = 1 << 5
}

[Flags]
public enum PlayerInputActions
{
    None = 0,
    PrimaryAction = 1 << 0,
    SecondaryAction = 1 << 1,
    MouseLeftHeld = 1 << 2,
    MouseRightHeld = 1 << 3
}

[Serializable]
public sealed class PlayerInputFrame
{
    public long Sequence;
    public long ClientTick;
    public long ClientTimeMs;
    public PlayerInputKeys Keys;
    public PlayerInputActions Actions;
    public float LookYaw;
    public float CameraYaw;
    public bool FaceCamera;
    public bool JumpPressed;
    public bool IsRunning;

    public bool HasKey(PlayerInputKeys key)
    {
        return (Keys & key) != 0;
    }

    public bool HasAction(PlayerInputActions action)
    {
        return (Actions & action) != 0;
    }

    public PlayerInputRequest ToRequest()
    {
        return new PlayerInputRequest
        {
            sequence = Sequence,
            clientTick = ClientTick,
            clientTimeMs = ClientTimeMs,
            keys = (int)Keys,
            actions = (int)Actions,
            lookYaw = LookYaw,
            cameraYaw = CameraYaw,
            faceCamera = FaceCamera,
            jumpPressed = JumpPressed,
            isRunning = IsRunning
        };
    }
}
