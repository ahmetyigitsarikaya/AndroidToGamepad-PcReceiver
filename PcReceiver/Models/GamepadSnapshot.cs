namespace PcReceiver.Models;

/// <summary>
/// Immutable snapshot of the latest XUSB packet received from the phone.
/// </summary>
public readonly record struct GamepadSnapshot(
    ushort Buttons,
    byte LeftTrigger,
    byte RightTrigger,
    short LeftThumbX,
    short LeftThumbY,
    short RightThumbX,
    short RightThumbY);

/// <summary>
/// XUSB gamepad button bitmask flags matching the wire protocol and ViGEm X360 layout.
/// </summary>
public static class XusbButtons
{
    public const ushort DpadUp = 0x0001;
    public const ushort DpadDown = 0x0002;
    public const ushort DpadLeft = 0x0004;
    public const ushort DpadRight = 0x0008;
    public const ushort Menu = 0x0010;
    public const ushort View = 0x0020;
    public const ushort LS = 0x0040;
    public const ushort RS = 0x0080;
    public const ushort LB = 0x0100;
    public const ushort RB = 0x0200;
    public const ushort Guide = 0x0400;
    public const ushort A = 0x1000;
    public const ushort B = 0x2000;
    public const ushort X = 0x4000;
    public const ushort Y = 0x8000;
}
