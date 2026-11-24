namespace androidtogamepad.Platforms.Android.Hid;

/// <summary>
/// XInput (XUSB) gamepad button bitmask flags.
/// </summary>
[Flags]
public enum XboxButton : ushort
{
    DpadUp = 0x0001,
    DpadDown = 0x0002,
    DpadLeft = 0x0004,
    DpadRight = 0x0008,
    Menu = 0x0010,
    View = 0x0020,
    LS = 0x0040,
    RS = 0x0080,
    LB = 0x0100,
    RB = 0x0200,
    Guide = 0x0400,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000
}
