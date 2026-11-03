using System;

namespace Lokad.Svg.Testing;

internal enum NativeAtan2Profile { Legacy, CoreMath }

/// <summary>Selects independently captured native references for the host's scalar atan2 implementation.</summary>
/// <remarks>No renderer output is inspected. The fixed, unrelated (1, 7) probe was measured through
/// native libm before recording the affected browser and original Skia function companions.
/// Unknown results fail qualification; neither profile permits a tolerance or reference rewrite.</remarks>
internal static class NativeMathProfile
{
    internal static NativeAtan2Profile Atan2 { get; }

    static NativeMathProfile()
    {
        if (!OperatingSystem.IsLinux()) { Atan2 = NativeAtan2Profile.Legacy; return; }
        uint bits = BitConverter.SingleToUInt32Bits(MathF.Atan2(1f, 7f));
        Atan2 = bits switch
        {
            0x3E114D77 => NativeAtan2Profile.Legacy,
            0x3E114D76 => NativeAtan2Profile.CoreMath,
            _ => throw new InvalidOperationException($"Unqualified native atan2f profile: {bits:X8}."),
        };
    }

}
