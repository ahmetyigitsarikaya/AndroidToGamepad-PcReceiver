using System.Buffers.Binary;

namespace androidtogamepad.Platforms.Android.Hid;

/// <summary>
/// Holds the current normalized gamepad state and serializes it into a 14-byte XUSB TCP packet.
/// </summary>
public sealed class XboxGamepadState
{
    private readonly object _sync = new();

    public const int PacketLength = 14;
    private const byte Magic = 0xA5;
    private const byte Version = 0x01;

    public float LX { get; set; }
    public float LY { get; set; }
    public float RX { get; set; }
    public float RY { get; set; }

    public float LT { get; set; }
    public float RT { get; set; }

    public bool L2Digital { get; set; }
    public bool R2Digital { get; set; }

    public ushort Buttons { get; set; }

    public bool DpadUp { get; set; }
    public bool DpadDown { get; set; }
    public bool DpadLeft { get; set; }
    public bool DpadRight { get; set; }

    public void SetButton(XboxButton button, bool pressed)
    {
        lock (_sync)
        {
            ushort mask = (ushort)button;
            if (pressed)
                Buttons |= mask;
            else
                Buttons &= (ushort)~mask;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            LX = 0f;
            LY = 0f;
            RX = 0f;
            RY = 0f;
            LT = 0f;
            RT = 0f;
            L2Digital = false;
            R2Digital = false;
            Buttons = 0;
            DpadUp = false;
            DpadDown = false;
            DpadLeft = false;
            DpadRight = false;
        }
    }

    /// <summary>
    /// Serializes the current controller state into a 14-byte little-endian packet for PcReceiver.
    /// </summary>
    public byte[] ToPacket()
    {
        lock (_sync)
        {
            var packet = new byte[PacketLength];
            Span<byte> span = packet;

            span[0] = Magic;
            span[1] = Version;

            ushort buttons = Buttons;
            if (DpadUp) buttons |= (ushort)XboxButton.DpadUp;
            if (DpadDown) buttons |= (ushort)XboxButton.DpadDown;
            if (DpadLeft) buttons |= (ushort)XboxButton.DpadLeft;
            if (DpadRight) buttons |= (ushort)XboxButton.DpadRight;

            BinaryPrimitives.WriteUInt16LittleEndian(span[2..4], buttons);

            span[4] = EncodeTrigger(LT, L2Digital);
            span[5] = EncodeTrigger(RT, R2Digital);

            BinaryPrimitives.WriteInt16LittleEndian(span[6..8], EncodeStickX(LX));
            BinaryPrimitives.WriteInt16LittleEndian(span[8..10], EncodeStickY(LY));
            BinaryPrimitives.WriteInt16LittleEndian(span[10..12], EncodeStickX(RX));
            BinaryPrimitives.WriteInt16LittleEndian(span[12..14], EncodeStickY(RY));

            return packet;
        }
    }

    private static byte EncodeTrigger(float analog, bool digitalFallback)
    {
        float value = analog > 0f ? analog : (digitalFallback ? 1f : 0f);
        return (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
    }

    private static short EncodeStickX(float value) =>
        (short)Math.Clamp((int)MathF.Round(value * 32767f), -32768, 32767);

    // Android motion axes use negative for up; XInput uses positive for up.
    private static short EncodeStickY(float value) =>
        (short)Math.Clamp((int)MathF.Round(-value * 32767f), -32768, 32767);
}
