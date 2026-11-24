using Android.Views;
using androidtogamepad.Platforms.Android.Hid;

namespace androidtogamepad.Platforms.Android.Input;

/// <summary>
/// Maps Android KeyEvent and MotionEvent inputs from a Bluetooth (or USB) PS3 DualShock 3 controller
/// to an <see cref="XboxGamepadState"/>.
/// </summary>
public sealed class Ps3InputMapper
{
    public static bool IsGamepad(InputSourceType source) =>
        (source & InputSourceType.Gamepad) == InputSourceType.Gamepad ||
        (source & InputSourceType.Joystick) == InputSourceType.Joystick;

    public static bool IsGamepadEvent(InputEvent? e) =>
        e is not null &&
        (IsGamepad(e.Source) || (e.Device is { } dev && IsGamepad(dev.Sources)));

    public bool HandleKey(KeyEvent e, XboxGamepadState state)
    {
        if (e.Action != KeyEventActions.Down && e.Action != KeyEventActions.Up)
            return false;

        // Tekrarlayan basılı tutma (auto-repeat) olaylarını yut, gereksiz HID raporu gönderme.
        if (e.Action == KeyEventActions.Down && e.RepeatCount > 0)
            return true;

        bool pressed = e.Action == KeyEventActions.Down;

        switch (e.KeyCode)
        {
            case Keycode.ButtonA: // PS3 Cross (X)
                state.SetButton(XboxButton.A, pressed);
                return true;

            case Keycode.ButtonB: // PS3 Circle (O)
                state.SetButton(XboxButton.B, pressed);
                return true;

            case Keycode.ButtonX: // PS3 Square (Kare)
                state.SetButton(XboxButton.X, pressed);
                return true;

            case Keycode.ButtonY: // PS3 Triangle (Üçgen)
                state.SetButton(XboxButton.Y, pressed);
                return true;

            case Keycode.ButtonL1:
                state.SetButton(XboxButton.LB, pressed);
                return true;

            case Keycode.ButtonR1:
                state.SetButton(XboxButton.RB, pressed);
                return true;

            case Keycode.ButtonThumbl: // L3
                state.SetButton(XboxButton.LS, pressed);
                return true;

            case Keycode.ButtonThumbr: // R3
                state.SetButton(XboxButton.RS, pressed);
                return true;

            case Keycode.ButtonSelect:
            case Keycode.Back:
                state.SetButton(XboxButton.View, pressed);
                return true;

            case Keycode.ButtonStart:
                state.SetButton(XboxButton.Menu, pressed);
                return true;

            case Keycode.ButtonMode: // PS Home tuşu
            case Keycode.Home:
                state.SetButton(XboxButton.Guide, pressed);
                return true;

            case Keycode.DpadUp:
                state.DpadUp = pressed;
                return true;

            case Keycode.DpadDown:
                state.DpadDown = pressed;
                return true;

            case Keycode.DpadLeft:
                state.DpadLeft = pressed;
                return true;

            case Keycode.DpadRight:
                state.DpadRight = pressed;
                return true;

            case Keycode.ButtonL2:
                state.L2Digital = pressed;
                return true;

            case Keycode.ButtonR2:
                state.R2Digital = pressed;
                return true;

            default:
                return false;
        }
    }

    public bool HandleMotion(MotionEvent e, XboxGamepadState state)
    {
        if (e.Action != MotionEventActions.Move)
            return false;

        InputDevice? device = e.Device;

        // Sol analog çubuk (AXIS_X, AXIS_Y)
        state.LX = GetCenteredAxis(e, device, Axis.X);
        state.LY = GetCenteredAxis(e, device, Axis.Y);

        // Sağ analog çubuk: Android Sony keylayout dosyası sağ analogu AXIS_Z / AXIS_RZ üzerinden verir;
        // yoksa standart AXIS_RX / AXIS_RY eksenlerine düş.
        bool hasZAxes = device is null ||
                        device.GetMotionRange(Axis.Z, e.Source) is not null ||
                        device.GetMotionRange(Axis.Rz, e.Source) is not null;

        if (hasZAxes)
        {
            state.RX = GetCenteredAxis(e, device, Axis.Z);
            state.RY = GetCenteredAxis(e, device, Axis.Rz);
        }
        else
        {
            state.RX = GetCenteredAxis(e, device, Axis.Rx);
            state.RY = GetCenteredAxis(e, device, Axis.Ry);
        }

        // L2 / R2 analog tetikleyiciler (bazı sürücüler Brake / Gas kullanır)
        float lt = Math.Max(e.GetAxisValue(Axis.Ltrigger), e.GetAxisValue(Axis.Brake));
        float rt = Math.Max(e.GetAxisValue(Axis.Rtrigger), e.GetAxisValue(Axis.Gas));
        state.LT = Math.Clamp(lt, 0f, 1f);
        state.RT = Math.Clamp(rt, 0f, 1f);

        // Bazı PS3 keylayout profilleri D-Pad'i KeyEvent yerine HAT_X / HAT_Y ekseni olarak gönderir
        if (device?.GetMotionRange(Axis.HatX, e.Source) is not null)
        {
            float hatX = e.GetAxisValue(Axis.HatX);
            state.DpadLeft = hatX < -0.5f;
            state.DpadRight = hatX > 0.5f;
        }

        if (device?.GetMotionRange(Axis.HatY, e.Source) is not null)
        {
            float hatY = e.GetAxisValue(Axis.HatY);
            state.DpadUp = hatY < -0.5f;
            state.DpadDown = hatY > 0.5f;
        }

        return true;
    }

    private static float GetCenteredAxis(MotionEvent e, InputDevice? device, Axis axis)
    {
        float value = e.GetAxisValue(axis);
        float flat = device?.GetMotionRange(axis, e.Source)?.Flat ?? 0.08f;
        float deadzone = Math.Max(flat, 0.05f);

        if (MathF.Abs(value) <= deadzone)
            return 0f;

        return Math.Clamp(value, -1f, 1f);
    }
}
