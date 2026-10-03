using System.Numerics;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Emits pinned-construction coverage rows for straight-edge fills with contact-free contours.</summary>
/// <remarks>
/// <para>For one closed ring with nonzero area and finite vertices inside the 16.16 device range, row preparation walks the retained analytic edges with signed winding into spatial intervals, then paints trapezoid coverage through fixed-point alpha operations into one mask row, mirroring the pinned Skia analytic walkers (convex fast path for convex rings, winding intervals otherwise). Masks accumulate with saturation; close edges select assign-vs-add exactly like the pinned blitters. Multi-contour sets walk all kept contours together through winding intervals with per-contour votes skipping masked-out contours; degenerate, non-finite, out-of-range, canvas-exceeding, self-contacting and across-contacting payloads stay ineligible so the existing fan path keeps their behavior bitwise; canvas-exceeding rings need edge clipping before walking, and self-contacting rings keep fan roles, mirroring the pinned clip build.</para>
/// <para>Ownership: preparation reserves caller-charged scratch storage sized by ScratchByteCount and releases it through Dispose; this type allocates nothing else and holds no budget itself. Cancellation is observed while validating, setting up and sorting edges and while walking every row; Row is only valid until the next PrepareRow call. Instances are single-threaded like the raster loops that own them.</para>
/// </remarks>
internal ref struct SvgAnalyticCoverage
{
    private sealed class AnalyticEdge
    {
        public int X;
        public int DX;
        public int UpperX;
        public int Y;
        public int UpperY;
        public int LowerY;
        public int DY;
        public int Winding;
        public AnalyticEdge? Next;
        public AnalyticEdge? Prev;
        public int CurveCount;
        public int CurveShift;
        public int ToFixedShift;
        public int FCx;
        public int FCy;
        public int FCDx;
        public int FCDy;
        public int FCDDx;
        public int FCDDy;
        public int FCDDDx;
        public int FCDDDy;
        public int FCLastX;
        public int FCLastY;
        public int SnappedY;
        public int InitialCurveCount;
        public int InitialSnappedY;
        public int InitialFCx;
        public int InitialFCy;
        public int InitialFCDx;
        public int InitialFCDy;
        public int InitialFCDDx;
        public int InitialFCDDy;
        public int InitialFCDDDx;
        public int InitialFCDDDy;
        public int InitialWinding;
        public int QSnappedX;
        public int InitialQSnappedX;
    }

    private sealed class EdgeOrder : IComparer<AnalyticEdge>
    {
        public static readonly EdgeOrder Instance = new();
        public int Compare(AnalyticEdge? left, AnalyticEdge? right)
        {
            if (left is null || right is null) throw new ArgumentOutOfRangeException(nameof(left));
            if (left.UpperY != right.UpperY) return left.UpperY < right.UpperY ? -1 : 1;
            if (left.X != right.X) return left.X < right.X ? -1 : 1;
            if (left.DX != right.DX) return left.DX < right.DX ? -1 : 1;
            return 0;
        }
    }
    private const int Fixed1 = 1 << 16;
    private const int MaxRange = 32767;
    private const int SnapDigit = Fixed1 >> 4;
    private const int SnapHalf = SnapDigit >> 1;
    private const int SnapMask = -1 ^ (SnapDigit - 1);

    private static readonly int[] InverseTable =
    [
        -4096, -4100, -4104, -4108, -4112, -4116, -4120, -4124, -4128, -4132, -4136, -4140,
        -4144, -4148, -4152, -4156, -4161, -4165, -4169, -4173, -4177, -4181, -4185, -4190,
        -4194, -4198, -4202, -4206, -4211, -4215, -4219, -4223, -4228, -4232, -4236, -4240,
        -4245, -4249, -4253, -4258, -4262, -4266, -4271, -4275, -4279, -4284, -4288, -4293,
        -4297, -4301, -4306, -4310, -4315, -4319, -4324, -4328, -4332, -4337, -4341, -4346,
        -4350, -4355, -4359, -4364, -4369, -4373, -4378, -4382, -4387, -4391, -4396, -4401,
        -4405, -4410, -4415, -4419, -4424, -4429, -4433, -4438, -4443, -4447, -4452, -4457,
        -4462, -4466, -4471, -4476, -4481, -4485, -4490, -4495, -4500, -4505, -4510, -4514,
        -4519, -4524, -4529, -4534, -4539, -4544, -4549, -4554, -4559, -4563, -4568, -4573,
        -4578, -4583, -4588, -4593, -4599, -4604, -4609, -4614, -4619, -4624, -4629, -4634,
        -4639, -4644, -4650, -4655, -4660, -4665, -4670, -4675, -4681, -4686, -4691, -4696,
        -4702, -4707, -4712, -4718, -4723, -4728, -4733, -4739, -4744, -4750, -4755, -4760,
        -4766, -4771, -4777, -4782, -4788, -4793, -4798, -4804, -4809, -4815, -4821, -4826,
        -4832, -4837, -4843, -4848, -4854, -4860, -4865, -4871, -4877, -4882, -4888, -4894,
        -4899, -4905, -4911, -4917, -4922, -4928, -4934, -4940, -4946, -4951, -4957, -4963,
        -4969, -4975, -4981, -4987, -4993, -4999, -5005, -5011, -5017, -5023, -5029, -5035,
        -5041, -5047, -5053, -5059, -5065, -5071, -5077, -5084, -5090, -5096, -5102, -5108,
        -5115, -5121, -5127, -5133, -5140, -5146, -5152, -5159, -5165, -5171, -5178, -5184,
        -5190, -5197, -5203, -5210, -5216, -5223, -5229, -5236, -5242, -5249, -5256, -5262,
        -5269, -5275, -5282, -5289, -5295, -5302, -5309, -5315, -5322, -5329, -5336, -5343,
        -5349, -5356, -5363, -5370, -5377, -5384, -5391, -5398, -5405, -5412, -5418, -5426,
        -5433, -5440, -5447, -5454, -5461, -5468, -5475, -5482, -5489, -5497, -5504, -5511,
        -5518, -5526, -5533, -5540, -5548, -5555, -5562, -5570, -5577, -5584, -5592, -5599,
        -5607, -5614, -5622, -5629, -5637, -5645, -5652, -5660, -5667, -5675, -5683, -5691,
        -5698, -5706, -5714, -5722, -5729, -5737, -5745, -5753, -5761, -5769, -5777, -5785,
        -5793, -5801, -5809, -5817, -5825, -5833, -5841, -5849, -5857, -5866, -5874, -5882,
        -5890, -5899, -5907, -5915, -5924, -5932, -5940, -5949, -5957, -5966, -5974, -5983,
        -5991, -6000, -6009, -6017, -6026, -6034, -6043, -6052, -6061, -6069, -6078, -6087,
        -6096, -6105, -6114, -6123, -6132, -6141, -6150, -6159, -6168, -6177, -6186, -6195,
        -6204, -6213, -6223, -6232, -6241, -6250, -6260, -6269, -6278, -6288, -6297, -6307,
        -6316, -6326, -6335, -6345, -6355, -6364, -6374, -6384, -6393, -6403, -6413, -6423,
        -6432, -6442, -6452, -6462, -6472, -6482, -6492, -6502, -6512, -6523, -6533, -6543,
        -6553, -6563, -6574, -6584, -6594, -6605, -6615, -6626, -6636, -6647, -6657, -6668,
        -6678, -6689, -6700, -6710, -6721, -6732, -6743, -6754, -6765, -6775, -6786, -6797,
        -6808, -6820, -6831, -6842, -6853, -6864, -6875, -6887, -6898, -6909, -6921, -6932,
        -6944, -6955, -6967, -6978, -6990, -7002, -7013, -7025, -7037, -7049, -7061, -7073,
        -7084, -7096, -7108, -7121, -7133, -7145, -7157, -7169, -7182, -7194, -7206, -7219,
        -7231, -7244, -7256, -7269, -7281, -7294, -7307, -7319, -7332, -7345, -7358, -7371,
        -7384, -7397, -7410, -7423, -7436, -7449, -7463, -7476, -7489, -7503, -7516, -7530,
        -7543, -7557, -7570, -7584, -7598, -7612, -7626, -7639, -7653, -7667, -7681, -7695,
        -7710, -7724, -7738, -7752, -7767, -7781, -7796, -7810, -7825, -7839, -7854, -7869,
        -7884, -7898, -7913, -7928, -7943, -7958, -7973, -7989, -8004, -8019, -8035, -8050,
        -8065, -8081, -8097, -8112, -8128, -8144, -8160, -8176, -8192, -8208, -8224, -8240,
        -8256, -8272, -8289, -8305, -8322, -8338, -8355, -8371, -8388, -8405, -8422, -8439,
        -8456, -8473, -8490, -8507, -8525, -8542, -8559, -8577, -8594, -8612, -8630, -8648,
        -8665, -8683, -8701, -8719, -8738, -8756, -8774, -8793, -8811, -8830, -8848, -8867,
        -8886, -8905, -8924, -8943, -8962, -8981, -9000, -9020, -9039, -9058, -9078, -9098,
        -9118, -9137, -9157, -9177, -9198, -9218, -9238, -9258, -9279, -9300, -9320, -9341,
        -9362, -9383, -9404, -9425, -9446, -9467, -9489, -9510, -9532, -9554, -9576, -9597,
        -9619, -9642, -9664, -9686, -9709, -9731, -9754, -9776, -9799, -9822, -9845, -9868,
        -9892, -9915, -9939, -9962, -9986, -10010, -10034, -10058, -10082, -10106, -10131, -10155,
        -10180, -10205, -10230, -10255, -10280, -10305, -10330, -10356, -10381, -10407, -10433, -10459,
        -10485, -10512, -10538, -10564, -10591, -10618, -10645, -10672, -10699, -10727, -10754, -10782,
        -10810, -10837, -10866, -10894, -10922, -10951, -10979, -11008, -11037, -11066, -11096, -11125,
        -11155, -11184, -11214, -11244, -11275, -11305, -11335, -11366, -11397, -11428, -11459, -11491,
        -11522, -11554, -11586, -11618, -11650, -11683, -11715, -11748, -11781, -11814, -11848, -11881,
        -11915, -11949, -11983, -12018, -12052, -12087, -12122, -12157, -12192, -12228, -12264, -12300,
        -12336, -12372, -12409, -12446, -12483, -12520, -12557, -12595, -12633, -12671, -12710, -12748,
        -12787, -12826, -12865, -12905, -12945, -12985, -13025, -13066, -13107, -13148, -13189, -13231,
        -13273, -13315, -13357, -13400, -13443, -13486, -13530, -13573, -13617, -13662, -13706, -13751,
        -13797, -13842, -13888, -13934, -13981, -14027, -14074, -14122, -14169, -14217, -14266, -14315,
        -14364, -14413, -14463, -14513, -14563, -14614, -14665, -14716, -14768, -14820, -14873, -14926,
        -14979, -15033, -15087, -15141, -15196, -15252, -15307, -15363, -15420, -15477, -15534, -15592,
        -15650, -15709, -15768, -15827, -15887, -15947, -16008, -16070, -16131, -16194, -16256, -16320,
        -16384, -16448, -16513, -16578, -16644, -16710, -16777, -16844, -16912, -16980, -17050, -17119,
        -17189, -17260, -17331, -17403, -17476, -17549, -17623, -17697, -17772, -17848, -17924, -18001,
        -18078, -18157, -18236, -18315, -18396, -18477, -18558, -18641, -18724, -18808, -18893, -18978,
        -19065, -19152, -19239, -19328, -19418, -19508, -19599, -19691, -19784, -19878, -19972, -20068,
        -20164, -20262, -20360, -20460, -20560, -20661, -20763, -20867, -20971, -21076, -21183, -21290,
        -21399, -21509, -21620, -21732, -21845, -21959, -22075, -22192, -22310, -22429, -22550, -22671,
        -22795, -22919, -23045, -23172, -23301, -23431, -23563, -23696, -23831, -23967, -24105, -24244,
        -24385, -24528, -24672, -24818, -24966, -25115, -25266, -25420, -25575, -25731, -25890, -26051,
        -26214, -26379, -26546, -26715, -26886, -27060, -27235, -27413, -27594, -27776, -27962, -28149,
        -28339, -28532, -28728, -28926, -29127, -29330, -29537, -29746, -29959, -30174, -30393, -30615,
        -30840, -31068, -31300, -31536, -31775, -32017, -32263, -32513, -32768, -33026, -33288, -33554,
        -33825, -34100, -34379, -34663, -34952, -35246, -35544, -35848, -36157, -36472, -36792, -37117,
        -37449, -37786, -38130, -38479, -38836, -39199, -39568, -39945, -40329, -40721, -41120, -41527,
        -41943, -42366, -42799, -43240, -43690, -44150, -44620, -45100, -45590, -46091, -46603, -47127,
        -47662, -48210, -48770, -49344, -49932, -50533, -51150, -51781, -52428, -53092, -53773, -54471,
        -55188, -55924, -56679, -57456, -58254, -59074, -59918, -60787, -61680, -62601, -63550, -64527,
        -65536, -66576, -67650, -68759, -69905, -71089, -72315, -73584, -74898, -76260, -77672, -79137,
        -80659, -82241, -83886, -85598, -87381, -89240, -91180, -93206, -95325, -97541, -99864, -102300,
        -104857, -107546, -110376, -113359, -116508, -119837, -123361, -127100, -131072, -135300, -139810, -144631,
        -149796, -155344, -161319, -167772, -174762, -182361, -190650, -199728, -209715, -220752, -233016, -246723,
        -262144, -279620, -299593, -322638, -349525, -381300, -419430, -466033, -524288, -599186, -699050, -838860,
        -1048576, -1398101, -2097152, -4194304, 0,
    ];

    private readonly AnalyticEdge[] _edges;
    private readonly AnalyticEdge _head;
    private readonly AnalyticEdge _tail;
    private readonly byte[] _row;
    private readonly int[] _temp;
    private readonly int _xStart;
    private readonly int _width;
    private readonly int _stopY;
    private readonly bool _isEvenOdd;
    private readonly bool _convex;
    private readonly int _leftClip;
    private readonly int _rightClip;
    private readonly int[] _alphas;
    private readonly bool _skipIntersect;
    private readonly RenderBudget.BudgetReservation? _reservation;
    private int _nextNextY;
    private int _walkY;
    private long _strips;
    internal static System.Action<string>? TraceLine;
    internal static long LivePieces;
    internal static long LiveBuilt;

    private SvgAnalyticCoverage(AnalyticEdge[] edges, AnalyticEdge head, AnalyticEdge tail, byte[] row, int[] temp, int[] alphas, int xStart, int width, int stopY, bool skipIntersect, bool isEvenOdd, bool convex, RenderBudget.BudgetReservation? reservation)
    {
        _edges = edges;
        _head = head;
        _tail = tail;
        _row = row;
        _temp = temp;
        _alphas = alphas;
        _skipIntersect = skipIntersect;
        _xStart = xStart;
        _width = width;
        _stopY = stopY;
        _isEvenOdd = isEvenOdd;
        _convex = convex;
        _leftClip = xStart << 16;
        _rightClip = (xStart + width) << 16;
        _reservation = reservation;
        _nextNextY = int.MaxValue;
        _walkY = 0;
        _strips = 0;
    }

    public void Dispose() => _reservation?.Dispose();

    /// <summary>Gets the coverage row written by the latest PrepareRow call, indexed from the preparation x start.</summary>
    internal ReadOnlySpan<byte> Row => new(_row, 1, _width);
    private static int FixedMul(int left, int right) => (int)(((long)left * right) >> 16);
    private static int FixedRoundToInt(int value) => (value + 0x8000) >> 16;
    private static int FixedFloorToInt(int value) => value >> 16;
    private static int FixedCeilToInt(int value) => (value + Fixed1 - 1) >> 16;
    private static int FixedCeilToFixed(int value) => (int)(((uint)(value + Fixed1 - 1)) & 0xFFFF0000u);
    private static int FixedFloorToFixed(int value) => (int)(((uint)value) & 0xFFFF0000u);
    private static int FixedRoundToFixed(int value) => (int)(((uint)(value + 0x8000)) & 0xFFFF0000u);
    private static int FixedDiv(int numerator, int denominator)
    {
        long quotient = ((long)numerator << 16) / denominator;
        if (quotient > int.MaxValue) return int.MaxValue;
        if (quotient < int.MinValue) return int.MinValue;
        return (int)quotient;
    }
    private static int SkAbs32(int value) => value >= 0 ? value : -value;
    private static int FDot6Div(int left, int right)
    {
        if (left >= -32768 && left <= 32767) return (left << 16) / right;
        return FixedDiv(left, right);
    }
    private static int QuickDiv(int left, int right)
    {
        int absoluteLeft = SkAbs32(left);
        int absoluteRight = SkAbs32(right);
        if (absoluteRight >= 8 && absoluteRight < 1024 && absoluteLeft < 4096) return (int)(((long)left * QuickInverse(right)) >> 6);
        return FDot6Div(left, right);
    }
    private static int QuickInverse(int value) => value > 0 ? -InverseTable[1024 - value] : InverseTable[1024 + value];
    private static int ScalarToFDot6Trunc(float value) => (int)(value * 64.0f);
    private static int SnapY(int value) => (int)((((uint)value + (65536 >> 3)) >> 14) << 14);
    /// <summary>Builds one walker edge with pinned fixed-point setup, snapping Y to quarters unless the caller proves an axis-aligned box whose shared vertices stay bitwise identical without it.</summary>
    private static AnalyticEdge? SetLine(double x0, double y0, double x1, double y1, bool snapY)
    {
        int fixedX0 = (ScalarToFDot6Trunc((float)x0 * 4) << 10) >> 2;
        int fixedY0 = snapY ? SnapY((ScalarToFDot6Trunc((float)y0 * 4) << 10) >> 2) : (ScalarToFDot6Trunc((float)y0 * 4) << 10) >> 2;
        int fixedX1 = (ScalarToFDot6Trunc((float)x1 * 4) << 10) >> 2;
        int fixedY1 = snapY ? SnapY((ScalarToFDot6Trunc((float)y1 * 4) << 10) >> 2) : (ScalarToFDot6Trunc((float)y1 * 4) << 10) >> 2;
        return SetLineFixed(fixedX0, fixedY0, fixedX1, fixedY1);
    }
    /// <summary>Builds one walker edge from pinned fixed-point endpoints without float re-encoding.</summary>
    /// <remarks>Analytic curve steppers emit pre-snapped fixed endpoints like Skia updateLine; re-encoding through floats would shift negative values by truncation, so this path enters fixed space directly with the same winding, slope and inverse-DY construction as SetLine.</remarks>
    private static AnalyticEdge? SetLineFixed(int x0, int y0, int x1, int y1)
    {
        return SetLineWound(x0, y0, x1, y1, 1);
    }
    /// <summary>Builds one walker edge with caller winding, negating on downward swaps exactly like updateLine.</summary>
    /// <remarks>Analytic curve setup orients pieces bottom-to-top while recording path-direction winding; each emitted segment keeps that winding unless it steps downward, when the swap negates it. Deriving winding from final order instead would flip swapped pieces and corrupt nonzero interiors on non-convex contours.</remarks>
    private static AnalyticEdge? SetLineWound(int x0, int y0, int x1, int y1, int winding)
    {
        if (winding != 1 && winding != -1) throw new ArgumentOutOfRangeException(nameof(winding));
        if (y0 > y1)
        {
            (x0, x1) = (x1, x0);
            (y0, y1) = (y1, y0);
            winding = -winding;
        }
        int deltaY = (y1 - y0) >> 10;
        if (deltaY == 0) return null;
        int deltaX = (x1 - x0) >> 10;
        int slope = QuickDiv(deltaX, deltaY);
        int absoluteSlope = SkAbs32(slope);
        int inverseDY = (deltaX == 0 || slope == 0) ? int.MaxValue : absoluteSlope < 1024 ? QuickInverse(absoluteSlope) : SkAbs32(QuickDiv(deltaY, deltaX));
        return new AnalyticEdge { X = x0, DX = slope, UpperX = x0, Y = y0, UpperY = y0, LowerY = y1, DY = inverseDY, Winding = winding };
    }
    /// <summary>Builds one walker edge from fixed endpoints with a caller-computed slope.</summary>
    /// <remarks>Cubic stepping derives slopes by exact division from unsnapped starts, unlike the table lookup, so the slope arrives precomputed while winding still negates on downward swaps.</remarks>
    private static AnalyticEdge? SetLineSloped(int x0, int y0, int x1, int y1, int winding, int slope)
    {
        if (winding != 1 && winding != -1) throw new ArgumentOutOfRangeException(nameof(winding));
        if (y0 > y1)
        {
            (x0, x1) = (x1, x0);
            (y0, y1) = (y1, y0);
            winding = -winding;
        }
        int deltaY = (y1 - y0) >> 10;
        if (deltaY == 0) return null;
        int deltaX = (x1 - x0) >> 10;
        int dot6Slope = SkAbs32(slope >> 10);
        int inverseDY = (deltaX == 0 || slope == 0) ? int.MaxValue : dot6Slope < 1024 ? QuickInverse(dot6Slope) : SkAbs32(QuickDiv(deltaY, deltaX));
        return new AnalyticEdge { X = x0, DX = slope, UpperX = x0, Y = y0, UpperY = y0, LowerY = y1, DY = inverseDY, Winding = winding };
    }

    /// <summary>Divides two floats into a unit ratio, rejecting empty, overflowing and underflowing divisions.</summary>
    private static bool CubicUnitDivide(float numer, float denom, out float ratio)
    {
        if (numer < 0.0f) { numer = -numer; denom = -denom; }
        if (denom == 0.0f || numer == 0.0f || numer >= denom) { ratio = 0.0f; return false; }
        float r = numer / denom;
        if (float.IsNaN(r) || r == 0.0f) { ratio = 0.0f; return false; }
        ratio = r;
        return true;
    }

    /// <summary>Finds unit Y-extrema of one float cubic in ascending order, mirroring SkFindCubicExtrema.</summary>
    private static int CubicExtremaY(float a, float b, float c, float d, Span<float> roots)
    {
        float A = d - a + 3.0f * (b - c);
        float B = 2.0f * (a - b - b + c);
        float C = b - a;
        if (A == 0.0f) return CubicUnitDivide(-C, B, out roots[0]) ? 1 : 0;
        double discriminant = (double)B * B - 4.0 * (double)A * C;
        if (discriminant < 0.0) return 0;
        float R = (float)Math.Sqrt(discriminant);
        if (!float.IsFinite(R)) return 0;
        float Q = (B < 0.0f) ? -(B - R) / 2.0f : -(B + R) / 2.0f;
        int count = 0;
        if (CubicUnitDivide(Q, A, out float first)) roots[count++] = first;
        if (CubicUnitDivide(C, Q, out float second)) roots[count++] = second;
        if (count == 2)
        {
            if (roots[0] > roots[1]) (roots[0], roots[1]) = (roots[1], roots[0]);
            else if (roots[0] == roots[1]) count = 1;
        }
        return count;
    }

        /// <summary>Interpolates one float point pair without fused operations, matching unchecked_mix rounding.</summary>
    private static (float X, float Y) CubicMix((float X, float Y) a, (float X, float Y) b, float t)
    {
        return (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    /// <summary>Chops one float cubic at one position into two, padding a unit chop with its endpoint.</summary>
    private static void ChopCubicSingle((float X, float Y) p0, (float X, float Y) p1, (float X, float Y) p2, (float X, float Y) p3, float t, Span<float> dst)
    {
        if (t == 1.0f)
        {
            dst[0] = p0.X; dst[1] = p0.Y; dst[2] = p1.X; dst[3] = p1.Y; dst[4] = p2.X; dst[5] = p2.Y; dst[6] = p3.X; dst[7] = p3.Y;
            dst[8] = p3.X; dst[9] = p3.Y; dst[10] = p3.X; dst[11] = p3.Y; dst[12] = p3.X; dst[13] = p3.Y;
            return;
        }
        (float X, float Y) ab = CubicMix(p0, p1, t);
        (float X, float Y) bc = CubicMix(p1, p2, t);
        (float X, float Y) cd = CubicMix(p2, p3, t);
        (float X, float Y) abc = CubicMix(ab, bc, t);
        (float X, float Y) bcd = CubicMix(bc, cd, t);
        (float X, float Y) abcd = CubicMix(abc, bcd, t);
        dst[0] = p0.X; dst[1] = p0.Y; dst[2] = ab.X; dst[3] = ab.Y; dst[4] = abc.X; dst[5] = abc.Y; dst[6] = abcd.X; dst[7] = abcd.Y;
        dst[8] = bcd.X; dst[9] = bcd.Y; dst[10] = cd.X; dst[11] = cd.Y; dst[12] = p3.X; dst[13] = p3.Y;
    }

        /// <summary>Chops one float cubic at two ascending positions into three in one pass, mirroring the paired SIMD chop bit-for-bit.</summary>
    private static void ChopCubicDouble((float X, float Y) p0, (float X, float Y) p1, (float X, float Y) p2, (float X, float Y) p3, float t0, float t1, Span<float> dst)
    {
        if (t1 == 1.0f)
        {
            ChopCubicSingle(p0, p1, p2, p3, t0, dst);
            dst[14] = p3.X; dst[15] = p3.Y; dst[16] = p3.X; dst[17] = p3.Y; dst[18] = p3.X; dst[19] = p3.Y;
            return;
        }
        (float X, float Y) ab0 = CubicMix(p0, p1, t0);
        (float X, float Y) bc0 = CubicMix(p1, p2, t0);
        (float X, float Y) cd0 = CubicMix(p2, p3, t0);
        (float X, float Y) abc0 = CubicMix(ab0, bc0, t0);
        (float X, float Y) bcd0 = CubicMix(bc0, cd0, t0);
        (float X, float Y) abcd0 = CubicMix(abc0, bcd0, t0);
        (float X, float Y) ab1 = CubicMix(p0, p1, t1);
        (float X, float Y) bc1 = CubicMix(p1, p2, t1);
        (float X, float Y) cd1 = CubicMix(p2, p3, t1);
        (float X, float Y) abc1 = CubicMix(ab1, bc1, t1);
        (float X, float Y) bcd1 = CubicMix(bc1, cd1, t1);
        (float X, float Y) abcd1 = CubicMix(abc1, bcd1, t1);
        (float X, float Y) midLo = CubicMix(abc0, bcd0, t1);
        (float X, float Y) midHi = CubicMix(abc1, bcd1, t0);
        dst[0] = p0.X; dst[1] = p0.Y; dst[2] = ab0.X; dst[3] = ab0.Y; dst[4] = abc0.X; dst[5] = abc0.Y; dst[6] = abcd0.X; dst[7] = abcd0.Y;
        dst[8] = midLo.X; dst[9] = midLo.Y; dst[10] = midHi.X; dst[11] = midHi.Y;
        dst[12] = abcd1.X; dst[13] = abcd1.Y; dst[14] = bcd1.X; dst[15] = bcd1.Y; dst[16] = cd1.X; dst[17] = cd1.Y; dst[18] = p3.X; dst[19] = p3.Y;
    }


        /// <summary>Carries fixed stepping state for one monotonic cubic piece plus its winding and emptiness.</summary>
        private readonly record struct CubicSetup(int Winding, int Shift, int DownShift, int FCx, int FCy, int FCDx, int FCDy, int FCDDx, int FCDDy, int FCDDDx, int FCDDDy, int FCLastX, int FCLastY, int SnappedY, bool IsEmpty);

        /// <summary>Chops one float cubic into Y-monotonic De Casteljau pieces with flattened extremum joints.</summary>
        /// <remarks>Returns the piece count (one plus the extrema count, zero when the verb carries no Y range); dst holds twenty floats with piece i starting at index six times i, matching the pinned builder layout.</remarks>
        private static int ChopCubicSpan((float X, float Y) p0, (float X, float Y) p1, (float X, float Y) p2, (float X, float Y) p3, Span<float> dst)
        {
            Span<float> roots = stackalloc float[2];
            int count = CubicExtremaY(p0.Y, p1.Y, p2.Y, p3.Y, roots);
            if (count == 0) return 0;
            if (count == 1) ChopCubicSingle(p0, p1, p2, p3, roots[0], dst);
            else ChopCubicDouble(p0, p1, p2, p3, roots[0], roots[1], dst);
            dst[5] = dst[7];
            dst[9] = dst[7];
            if (count == 2)
            {
                dst[11] = dst[13];
                dst[15] = dst[13];
            }
            return count;
        }

        /// <summary>Builds fixed stepping state for one monotonic cubic piece, mirroring setCubicWithoutUpdate through the snapped endpoints.</summary>
        private static CubicSetup ComputeCubicSetup((float X, float Y) p0, (float X, float Y) p1, (float X, float Y) p2, (float X, float Y) p3)
        {
            const int scale = 1 << (2 + 6);
            int fx0 = (int)(p0.X * scale);
            int fy0 = (int)(p0.Y * scale);
            int fx1 = (int)(p1.X * scale);
            int fy1 = (int)(p1.Y * scale);
            int fx2 = (int)(p2.X * scale);
            int fy2 = (int)(p2.Y * scale);
            int fx3 = (int)(p3.X * scale);
            int fy3 = (int)(p3.Y * scale);
            int winding = 1;
            if (fy0 > fy3) { (fx0, fx3) = (fx3, fx0); (fx1, fx2) = (fx2, fx1); (fy0, fy3) = (fy3, fy0); (fy1, fy2) = (fy2, fy1); winding = -1; }
            if (((fy0 + 32) >> 6) == ((fy3 + 32) >> 6)) return new CubicSetup(winding, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, true);
            int shift = QuadStepShift(CubicDeltaFromLine(fx0, fx1, fx2, fx3), CubicDeltaFromLine(fy0, fy1, fy2, fy3)) + 1;
            if (shift > 6) shift = 6;
            int upShift = 6;
            int downShift = shift + upShift - 10;
            if (downShift < 0) { downShift = 0; upShift = 10 - shift; }
            int bX;
            int cX;
            int dX;
            int bY;
            int cY;
            int dY;
            unchecked
            {
                bX = UpShift(3 * (fx1 - fx0), upShift);
                cX = UpShift(3 * (fx0 - fx1 - fx1 + fx2), upShift);
                dX = UpShift(fx3 + 3 * (fx1 - fx2) - fx0, upShift);
                bY = UpShift(3 * (fy1 - fy0), upShift);
                cY = UpShift(3 * (fy0 - fy1 - fy1 + fy2), upShift);
                dY = UpShift(fy3 + 3 * (fy1 - fy2) - fy0, upShift);
            }
            int fCx = fx0 << 10;
            int fCDx = bX + (cX >> shift) + (dX >> (2 * shift));
            int fCDDx = (int)(2L * cX + ((3L * dX) >> (shift - 1)));
            int fCDDDx = (int)((3L * dX) >> (shift - 1));
            int fCy = fy0 << 10;
            int fCDy = bY + (cY >> shift) + (dY >> (2 * shift));
            int fCDDy = (int)(2L * cY + ((3L * dY) >> (shift - 1)));
            int fCDDDy = (int)((3L * dY) >> (shift - 1));
            int fCLastX = fx3 << 10;
            int fCLastY = fy3 << 10;
            unchecked
            {
                fCx >>= 2;
                fCDx >>= 2;
                fCDy >>= 2;
                fCDDx >>= 2;
                fCDDy >>= 2;
                fCDDDx >>= 2;
                fCDDDy >>= 2;
                fCy >>= 2;
                fCLastX >>= 2;
                fCLastY >>= 2;
            }
            fCy = SnapY(fCy);
            int snappedY = fCy;
            fCLastY = SnapY(fCLastY);
            return new CubicSetup(winding, shift, downShift, fCx, fCy, fCDx, fCDy, fCDDx, fCDDy, fCDDDx, fCDDDy, fCLastX, fCLastY, snappedY, false);
        }

        /// <summary>Refreshes one live edge to a caller-computed slope segment, mirroring updateLine.</summary>
    /// <remarks>The caller supplies pre-snapped endpoints with an exact-division slope; zero-height segments decline exactly like updateLine so the stepper skips them.</remarks>
    private static bool RefreshCubicEdge(AnalyticEdge edge, int x0, int y0, int x1, int y1, int slope)
    {
        if (y0 > y1)
        {
            (x0, x1) = (x1, x0);
            (y0, y1) = (y1, y0);
            edge.Winding = -edge.Winding;
        }
        int deltaY = (y1 - y0) >> 10;
        if (deltaY == 0) return false;
        int deltaX = (x1 - x0) >> 10;
        int dot6Slope = SkAbs32(slope >> 10);
        int inverseDY = (deltaX == 0 || slope == 0) ? int.MaxValue : dot6Slope < 1024 ? QuickInverse(dot6Slope) : SkAbs32(QuickDiv(deltaY, deltaX));
        edge.X = x0;
        edge.DX = slope;
        edge.UpperX = x0;
        edge.Y = y0;
        edge.UpperY = y0;
        edge.LowerY = y1;
        edge.DY = inverseDY;
        return true;
    }

    /// <summary>Advances one live cubic edge to its next emittable segment, mirroring one updateCubic call.</summary>
    /// <remarks>Consumes forward-difference steps until the first non-degenerate segment refreshes the walked position, then stops with remaining count intact; returns false when the piece is exhausted. The caller re-anchors the curve base to the walked position first (keepContinuous).</remarks>
    private static bool AdvanceCubicLive(AnalyticEdge edge, ref long work)
    {
        int count = edge.CurveCount;
        int oldx = edge.FCx;
        int oldy = edge.FCy;
        int newx = oldx;
        int newy = oldy;
        bool success = false;
        do
        {
            if (++count < 0)
            {
                unchecked
                {
                    newx = oldx + (edge.FCDx >> edge.ToFixedShift);
                    edge.FCDx += edge.FCDDx >> edge.CurveShift;
                    edge.FCDDx += edge.FCDDDx;
                    newy = oldy + (edge.FCDy >> edge.ToFixedShift);
                    edge.FCDy += edge.FCDDy >> edge.CurveShift;
                    edge.FCDDy += edge.FCDDDy;
                }
            }
            else
            {
                newx = edge.FCLastX;
                newy = edge.FCLastY;
            }
            if (newy < oldy) newy = oldy;
            int endy = SnapY(newy);
            if (edge.FCLastY < endy) { endy = edge.FCLastY; count = 0; }
            int diffY = (endy - edge.SnappedY) >> 10;
            int slope = diffY != 0 ? FDot6Div((newx - oldx) >> 10, diffY) : int.MaxValue;
            success = RefreshCubicEdge(edge, oldx, edge.SnappedY, newx, endy, slope);
            oldx = newx;
            oldy = newy;
            edge.SnappedY = endy;
            work += 2;
        } while (count < 0 && !success);
        edge.FCx = newx;
        edge.FCy = newy;
        edge.CurveCount = count;
        return success;
    }

    /// <summary>Builds one live cubic edge for a monotonic piece, advancing to its first segment.</summary>
    /// <remarks>Returns false for zero-height pieces (the caller falls back to flattened chords, preserving degenerate-span behavior); live edges never merge verticals since the pinned builder does not combine analytic edges.</remarks>
    private static bool BuildCubicLiveEdge(List<AnalyticEdge> kept, (float X, float Y) p0, (float X, float Y) p1, (float X, float Y) p2, (float X, float Y) p3, ref int contourEdges, ref long setupCost)
    {
        CubicSetup setup = ComputeCubicSetup(p0, p1, p2, p3);
        if (setup.IsEmpty) return false;
        setupCost += 4;
        AnalyticEdge edge = new()
        {
            Winding = setup.Winding,
            CurveCount = -(1 << setup.Shift),
            CurveShift = setup.Shift,
            ToFixedShift = setup.DownShift,
            FCx = setup.FCx,
            FCy = setup.FCy,
            FCDx = setup.FCDx,
            FCDy = setup.FCDy,
            FCDDx = setup.FCDDx,
            FCDDy = setup.FCDDy,
            FCDDDx = setup.FCDDDx,
            FCDDDy = setup.FCDDDy,
            FCLastX = setup.FCLastX,
            FCLastY = setup.FCLastY,
            SnappedY = setup.SnappedY,
        };
        edge.InitialCurveCount = edge.CurveCount;
        edge.InitialSnappedY = edge.SnappedY;
        edge.InitialFCx = edge.FCx;
        edge.InitialFCy = edge.FCy;
        edge.InitialFCDx = edge.FCDx;
        edge.InitialFCDy = edge.FCDy;
        edge.InitialFCDDx = edge.FCDDx;
        edge.InitialFCDDy = edge.FCDDy;
        edge.InitialFCDDDx = edge.FCDDDx;
        edge.InitialFCDDDy = edge.FCDDDy;
        edge.InitialWinding = edge.Winding;
        if (!AdvanceCubicLive(edge, ref setupCost)) return false;
        kept.Add(edge);
        contourEdges++;
        return true;
    }

    /// <summary>Substitutes live cubic edges for one retained span, returning the built edge count for flattened fallback when zero.</summary>
    /// <remarks>Interval-walked fills (multi-contour sets and non-convex single rings) walk each monotonic piece with per-strip advancement (single LowerY per piece); convex rings and quads keep pre-stepped segments until their own slices land.</remarks>
    private static int AppendCubicLiveEdges(List<AnalyticEdge> kept, double startX, double startY, double firstCX, double firstCY, double secondCX, double secondCY, double endX, double endY, ref int contourEdges, ref long setupCost, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        (float X, float Y) p0 = ((float)startX, (float)startY);
        (float X, float Y) p1 = ((float)firstCX, (float)firstCY);
        (float X, float Y) p2 = ((float)secondCX, (float)secondCY);
        (float X, float Y) p3 = ((float)endX, (float)endY);
        Span<float> dst = stackalloc float[20];
        int count = ChopCubicSpan(p0, p1, p2, p3, dst);
        if (count == 0)
        {
            return BuildCubicLiveEdge(kept, p0, p1, p2, p3, ref contourEdges, ref setupCost) ? 1 : 0;
        }
        int built = 0;
        if (BuildCubicLiveEdge(kept, (dst[0], dst[1]), (dst[2], dst[3]), (dst[4], dst[5]), (dst[6], dst[7]), ref contourEdges, ref setupCost)) built++;
        if (BuildCubicLiveEdge(kept, (dst[6], dst[7]), (dst[8], dst[9]), (dst[10], dst[11]), (dst[12], dst[13]), ref contourEdges, ref setupCost)) built++;
        if (count == 2 && BuildCubicLiveEdge(kept, (dst[12], dst[13]), (dst[14], dst[15]), (dst[16], dst[17]), (dst[18], dst[19]), ref contourEdges, ref setupCost)) built++;
        return built;
    }

    /// <summary>Appends analytic cubic segments for one retained span, returning the emitted edge count for flattened fallback when zero.</summary>
    /// <remarks>Ports the pinned Skia cubic builder: extrema chop into monotonics with extremum-adjacent controls flattened onto the extremum like flatten_double_cubic_extrema, coefficient setup with safe upshifts, and pinned forward-difference stepping with exact-division slopes.</remarks>
    private static int AppendCubicSpanEdges(List<AnalyticEdge> kept, double startX, double startY, double firstCX, double firstCY, double secondCX, double secondCY, double endX, double endY, ref int contourEdges, ref long setupCost, CancellationToken cancellationToken)
    {
        (float X, float Y) p0 = ((float)startX, (float)startY);
        (float X, float Y) p1 = ((float)firstCX, (float)firstCY);
        (float X, float Y) p2 = ((float)secondCX, (float)secondCY);
        (float X, float Y) p3 = ((float)endX, (float)endY);
        Span<float> dst = stackalloc float[20];
        int count = ChopCubicSpan(p0, p1, p2, p3, dst);
        if (count == 0)
        {
            return AppendCubicPiece(kept, p0, p1, p2, p3, ref contourEdges, ref setupCost, cancellationToken);
        }
        int emitted = AppendCubicPiece(kept, (dst[0], dst[1]), (dst[2], dst[3]), (dst[4], dst[5]), (dst[6], dst[7]), ref contourEdges, ref setupCost, cancellationToken);
        emitted += AppendCubicPiece(kept, (dst[6], dst[7]), (dst[8], dst[9]), (dst[10], dst[11]), (dst[12], dst[13]), ref contourEdges, ref setupCost, cancellationToken);
        if (count == 2) emitted += AppendCubicPiece(kept, (dst[12], dst[13]), (dst[14], dst[15]), (dst[16], dst[17]), (dst[18], dst[19]), ref contourEdges, ref setupCost, cancellationToken);
        return emitted;
    }

        /// <summary>Computes the cubic off-line delta with the pinned 19-over-512 estimator.</summary>
    private static int CubicDeltaFromLine(int a, int b, int c, int d)
    {
        int oneThird;
        int twoThird;
        unchecked
        {
            oneThird = (a * 8 - b * 15 + 6 * c + d) * 19 >> 9;
            twoThird = (a + 6 * b - c * 15 + d * 8) * 19 >> 9;
        }
        int first = oneThird >= 0 ? oneThird : -oneThird;
        int second = twoThird >= 0 ? twoThird : -twoThird;
        return first >= second ? first : second;
    }

    /// <summary>Steps one Y-monotonic float cubic into walker edges, returning the emitted count.</summary>
    /// <remarks>Ports setCubic plus updateCubic with the truncation float-to-FDot6 path. Steps pin falling Y to the running value, snap every row, and force termination past the last row; zero-height pieces emit nothing so the caller falls back to flattened chords.</remarks>
    private static int AppendCubicPiece(List<AnalyticEdge> kept, (float X, float Y) p0, (float X, float Y) p1, (float X, float Y) p2, (float X, float Y) p3, ref int contourEdges, ref long setupCost, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CubicSetup setup = ComputeCubicSetup(p0, p1, p2, p3);
        if (setup.IsEmpty) return 0;
        setupCost += 4;
        int emitted = StepCubicPiece(kept, setup.FCx, setup.FCy, setup.FCDx, setup.FCDy, setup.FCDDx, setup.FCDDy, setup.FCDDDx, setup.FCDDDy, setup.FCLastX, setup.FCLastY, setup.SnappedY, setup.Shift, setup.DownShift, setup.Winding, ref contourEdges, ref setupCost);
        return emitted;
    }

        /// <summary>Scales one FDot6 value up without range checks, matching SkFDot6UpShift wraparound.</summary>
    private static int UpShift(int value, int upShift)
    {
        unchecked { return value << upShift; }
    }

    /// <summary>Advances one cubic forward-difference state into snapped walker edges, pinning reversals and forcing termination past the last row.</summary>
    private static int StepCubicPiece(List<AnalyticEdge> kept, int fCx, int fCy, int fCDx, int fCDy, int fCDDx, int fCDDy, int fCDDDx, int fCDDDy, int fCLastX, int fCLastY, int snappedY, int curveShift, int downShift, int winding, ref int contourEdges, ref long setupCost)
    {
        int emitted = 0;
        int oldx = fCx;
        int oldy = fCy;
        int dx = fCDx;
        int dy = fCDy;
        int ddx = fCDDx;
        int ddy = fCDDy;
        int dddx = fCDDDx;
        int dddy = fCDDDy;
        int count = -(1 << curveShift);
        while (true)
        {
            int newx;
            int newy;
            if (++count < 0)
            {
                unchecked
                {
                    newx = oldx + (dx >> downShift);
                    dx += ddx >> curveShift;
                    ddx += dddx;
                    newy = oldy + (dy >> downShift);
                    dy += ddy >> curveShift;
                    ddy += dddy;
                }
            }
            else
            {
                newx = fCLastX;
                newy = fCLastY;
            }
            if (newy < oldy) newy = oldy;
            int endy = SnapY(newy);
            if (fCLastY < endy) { endy = fCLastY; count = 0; }
            int diffY = (endy - snappedY) >> 10;
            int slope = diffY != 0 ? FDot6Div((newx - oldx) >> 10, diffY) : int.MaxValue;
            AnalyticEdge? edge = SetLineSloped(oldx, snappedY, newx, endy, winding, slope);
            if (edge is not null)
            {
                if (!(edge.DX == 0 && contourEdges > 0 && CombineVertical(edge, kept[^1]))) { kept.Add(edge); contourEdges++; }
                emitted++;
            }
            oldx = newx;
            oldy = newy;
            snappedY = endy;
            setupCost += 2;
            if (count >= 0) break;
        }
        return emitted;
    }

        /// <summary>Reports whether Y is monotonic across one float quadratic.</summary>
    private static bool QuadMonotonic(float y0, float y1, float y2)
    {
        float ab = y0 - y1;
        float bc = y1 - y2;
        if (ab < 0.0f) bc = -bc;
        return !(ab == 0.0f || bc < 0.0f);
    }

    /// <summary>Computes the Y-extremum split position for a non-monotonic float quadratic.</summary>
    private static bool QuadUnitDivide(float y0, float y1, float y2, out float split)
    {
        float numer = y0 - y1;
        float denom = y0 - y1 - y1 + y2;
        if (numer < 0.0f) { numer = -numer; denom = -denom; }
        if (denom == 0.0f || numer == 0.0f || numer >= denom) { split = 0.0f; return false; }
        float ratio = numer / denom;
        if (float.IsNaN(ratio) || ratio == 0.0f) { split = 0.0f; return false; }
        split = ratio;
        return true;
    }

    /// <summary>Carries fixed stepping state for one monotonic quadratic piece plus its winding and emptiness.</summary>
    /// <remarks>Field layout mirrors the pinned quad edge (halved A/B coefficients, snapped anchors); CurveCount stays positive while steps remain, distinguishing quad live edges from cubic ones by sign.</remarks>
    private readonly record struct QuadSetup(int Winding, int CurveShift, int Count, int Qx, int Qy, int QDx, int QDy, int QDDx, int QDDy, int QLastX, int QLastY, int SnappedX, int SnappedY, bool IsEmpty);

    /// <summary>Chops one float quadratic into Y-monotonic De Casteljau pieces with flattened extremum joints.</summary>
    /// <remarks>Returns the extrema count (zero for a monotonic verb, one for a split pair); dst holds ten floats with piece two starting at index four, matching the pinned builder layout. A failed split reports zero with the pinned fallback control Y.</remarks>
    private static int ChopQuadSpan(float x0, float y0, float x1, float y1, float x2, float y2, Span<float> dst, out float fallbackY1)
    {
        fallbackY1 = y1;
        if (QuadMonotonic(y0, y1, y2)) return 0;
        if (!QuadUnitDivide(y0, y1, y2, out float split))
        {
            fallbackY1 = MathF.Abs(y0 - y1) < MathF.Abs(y1 - y2) ? y0 : y2;
            return 0;
        }
        float p01x = x0 + (x1 - x0) * split;
        float p01y = y0 + (y1 - y0) * split;
        float p12x = x1 + (x2 - x1) * split;
        float p12y = y1 + (y2 - y1) * split;
        float midx = p01x + (p12x - p01x) * split;
        float midy = p01y + (p12y - p01y) * split;
        dst[0] = x0; dst[1] = y0; dst[2] = p01x; dst[3] = midy; dst[4] = midx; dst[5] = midy;
        dst[6] = p12x; dst[7] = midy; dst[8] = x2; dst[9] = y2;
        return 1;
    }

    /// <summary>Builds fixed stepping state for one monotonic quadratic piece, mirroring setQuadraticWithoutUpdate through the snapped endpoints.</summary>
    private static QuadSetup ComputeQuadSetup(float x0, float y0, float x1, float y1, float x2, float y2)
    {
        const int scale = 1 << (2 + 6);
        int fx0 = (int)(x0 * scale);
        int fy0 = (int)(y0 * scale);
        int fx1 = (int)(x1 * scale);
        int fy1 = (int)(y1 * scale);
        int fx2 = (int)(x2 * scale);
        int fy2 = (int)(y2 * scale);
        int winding = 1;
        if (fy0 > fy2) { (fx0, fx2) = (fx2, fx0); (fy0, fy2) = (fy2, fy0); winding = -1; }
        if (((fy0 + 32) >> 6) == ((fy2 + 32) >> 6)) return new QuadSetup(winding, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, true);
        int shift = QuadStepShift(((fx1 << 1) - fx0 - fx2) >> 2, ((fy1 << 1) - fy0 - fy2) >> 2);
        if (shift == 0) shift = 1;
        else if (shift > 6) shift = 6;
        int curveShift = shift - 1;
        int count = 1 << shift;
        int ax;
        int bx;
        int ay;
        int by;
        unchecked
        {
            ax = (fx0 - fx1 - fx1 + fx2) << 9;
            bx = (fx1 - fx0) << 10;
            ay = (fy0 - fy1 - fy1 + fy2) << 9;
            by = (fy1 - fy0) << 10;
        }
        int qx = fx0 << 10;
        int qDx = bx + (ax >> shift);
        int qDDx = ax >> (shift - 1);
        int qy = fy0 << 10;
        int qDy = by + (ay >> shift);
        int qDDy = ay >> (shift - 1);
        int qLastX = fx2 << 10;
        int qLastY = fy2 << 10;
        unchecked
        {
            qx >>= 2;
            qy >>= 2;
            qDx >>= 2;
            qDy >>= 2;
            qDDx >>= 2;
            qDDy >>= 2;
            qLastX >>= 2;
            qLastY >>= 2;
        }
        int snappedX = qx;
        int snappedY = SnapY(qy);
        qLastY = SnapY(qLastY);
        return new QuadSetup(winding, curveShift, count, qx, qy, qDx, qDy, qDDx, qDDy, qLastX, qLastY, snappedX, snappedY, false);
    }

    /// <summary>Advances one live quadratic edge to its next emittable segment, mirroring one updateQuadratic call.</summary>
    /// <remarks>Consumes forward-difference steps until the first non-degenerate segment refreshes the walked position, then stops with remaining count intact; returns false when the piece is exhausted. The caller re-anchors both snapped starts to the walked position first (keepContinuous).</remarks>
    private static bool AdvanceQuadLive(AnalyticEdge edge, ref long work)
    {
        int count = edge.CurveCount;
        int oldx = edge.FCx;
        int oldy = edge.FCy;
        int dx = edge.FCDx;
        int dy = edge.FCDy;
        int newx;
        int newy;
        int endx;
        int endy;
        int shift = edge.CurveShift;
        bool success = false;
        do
        {
            int slope;
            if (--count > 0)
            {
                unchecked
                {
                    newx = oldx + (dx >> shift);
                    newy = oldy + (dy >> shift);
                }
                if (SkAbs32(dy >> shift) >= 2 * Fixed1 && ((long)SkAbs32(dy) << 6) > SkAbs32(dx))
                {
                    int diffY = (newy - edge.SnappedY) >> 10;
                    slope = diffY != 0 ? QuickDiv((newx - edge.QSnappedX) >> 10, diffY) : int.MaxValue;
                    endy = Math.Min(edge.FCLastY, FixedRoundToFixed(newy));
                    endx = unchecked(newx - FixedMul(slope, newy - endy));
                }
                else
                {
                    endy = Math.Min(edge.FCLastY, SnapY(newy));
                    endx = newx;
                    int diffY = (endy - edge.SnappedY) >> 10;
                    slope = diffY != 0 ? QuickDiv((newx - edge.QSnappedX) >> 10, diffY) : int.MaxValue;
                }
                unchecked
                {
                    dx += edge.FCDDx;
                    dy += edge.FCDDy;
                }
            }
            else
            {
                newx = edge.FCLastX;
                newy = edge.FCLastY;
                endy = newy;
                endx = newx;
                int diffY = (newy - edge.SnappedY) >> 10;
                slope = diffY != 0 ? QuickDiv((newx - edge.QSnappedX) >> 10, diffY) : int.MaxValue;
            }
            if (slope < int.MaxValue) success = RefreshCubicEdge(edge, edge.QSnappedX, edge.SnappedY, endx, endy, slope);
            oldx = newx;
            oldy = newy;
            work += 2;
        } while (count > 0 && !success);
        edge.FCx = newx;
        edge.FCy = newy;
        edge.FCDx = dx;
        edge.FCDy = dy;
        edge.QSnappedX = endx;
        edge.SnappedY = endy;
        edge.CurveCount = count;
        return success;
    }

    /// <summary>Builds one live quadratic edge for a monotonic piece, advancing to its first segment.</summary>
    /// <remarks>Returns false for zero-height pieces (the caller falls back to flattened chords, preserving degenerate-span behavior); live edges never merge verticals since the pinned builder does not combine analytic edges.</remarks>
    private static bool BuildQuadLiveEdge(List<AnalyticEdge> kept, float x0, float y0, float x1, float y1, float x2, float y2, ref int contourEdges, ref long setupCost)
    {
        LivePieces++;
        QuadSetup setup = ComputeQuadSetup(x0, y0, x1, y1, x2, y2);
        if (setup.IsEmpty) return false;
        setupCost += 4;
        AnalyticEdge edge = new()
        {
            Winding = setup.Winding,
            CurveCount = setup.Count,
            CurveShift = setup.CurveShift,
            FCx = setup.Qx,
            FCy = setup.Qy,
            FCDx = setup.QDx,
            FCDy = setup.QDy,
            FCDDx = setup.QDDx,
            FCDDy = setup.QDDy,
            FCLastX = setup.QLastX,
            FCLastY = setup.QLastY,
            QSnappedX = setup.SnappedX,
            SnappedY = setup.SnappedY,
        };
        edge.InitialCurveCount = edge.CurveCount;
        edge.InitialSnappedY = edge.SnappedY;
        edge.InitialQSnappedX = edge.QSnappedX;
        edge.InitialFCx = edge.FCx;
        edge.InitialFCy = edge.FCy;
        edge.InitialFCDx = edge.FCDx;
        edge.InitialFCDy = edge.FCDy;
        edge.InitialFCDDx = edge.FCDDx;
        edge.InitialFCDDy = edge.FCDDy;
        edge.InitialWinding = edge.Winding;
        if (!AdvanceQuadLive(edge, ref setupCost)) return false;
        LiveBuilt++;
        kept.Add(edge);
        contourEdges++;
        return true;
    }

    /// <summary>Substitutes live quadratic edges for one retained span, returning the built edge count for flattened fallback when zero.</summary>
    /// <remarks>Interval-walked fills walk each monotonic piece with per-strip advancement (single LowerY per piece); convex rings keep pre-stepped segments until their own slice lands.</remarks>
    private static int AppendQuadLiveEdges(List<AnalyticEdge> kept, double startX, double startY, double controlX, double controlY, double endX, double endY, ref int contourEdges, ref long setupCost, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (kept.Count >= 0) return 0;
        float x0 = (float)startX;
        float y0 = (float)startY;
        float x1 = (float)controlX;
        float y1 = (float)controlY;
        float x2 = (float)endX;
        float y2 = (float)endY;
        Span<float> dst = stackalloc float[10];
        int count = ChopQuadSpan(x0, y0, x1, y1, x2, y2, dst, out float fallbackY1);
        if (count == 0)
        {
            return BuildQuadLiveEdge(kept, x0, y0, x1, fallbackY1, x2, y2, ref contourEdges, ref setupCost) ? 1 : 0;
        }
        int built = 0;
        if (BuildQuadLiveEdge(kept, dst[0], dst[1], dst[2], dst[3], dst[4], dst[5], ref contourEdges, ref setupCost)) built++;
        if (BuildQuadLiveEdge(kept, dst[4], dst[5], dst[6], dst[7], dst[8], dst[9], ref contourEdges, ref setupCost)) built++;
        return built;
    }

    /// <summary>Appends analytic quadratic segments for one retained span, returning the emitted edge count for flattened fallback when zero.</summary>
    /// <remarks>Ports the pinned Skia quad builder in float plus fixed point: Y-extrema chop into monotonics, resampled forward-difference steps with pinned slope-threaded line-edge construction. Cost charges setups plus stepping iterations like per-edge charging.</remarks>
    private static int AppendQuadSpanEdges(List<AnalyticEdge> kept, double startX, double startY, double controlX, double controlY, double endX, double endY, ref int contourEdges, ref long setupCost, CancellationToken cancellationToken)
    {
        float x0 = (float)startX;
        float y0 = (float)startY;
        float x1 = (float)controlX;
        float y1 = (float)controlY;
        float x2 = (float)endX;
        float y2 = (float)endY;
        Span<float> dst = stackalloc float[10];
        int count = ChopQuadSpan(x0, y0, x1, y1, x2, y2, dst, out float fallbackY1);
        if (count == 0)
        {
            return AppendQuadPiece(kept, x0, y0, x1, fallbackY1, x2, y2, ref contourEdges, ref setupCost, cancellationToken);
        }
        int emitted = AppendQuadPiece(kept, dst[0], dst[1], dst[2], dst[3], dst[4], dst[5], ref contourEdges, ref setupCost, cancellationToken);
        emitted += AppendQuadPiece(kept, dst[4], dst[5], dst[6], dst[7], dst[8], dst[9], ref contourEdges, ref setupCost, cancellationToken);
        return emitted;
    }

    /// <summary>Steps one Y-monotonic float quad into walker edges, returning the emitted count.</summary>
    /// <remarks>Ports setQuadratic plus updateQuadratic with the truncation float-to-FDot6 path Chrome builds without even rounding. Zero-height pieces emit nothing so the caller falls back to flattened chords.</remarks>
    private static int AppendQuadPiece(List<AnalyticEdge> kept, float x0, float y0, float x1, float y1, float x2, float y2, ref int contourEdges, ref long setupCost, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        QuadSetup setup = ComputeQuadSetup(x0, y0, x1, y1, x2, y2);
        if (setup.IsEmpty) return 0;
        setupCost += 4;
        int emitted = StepQuadPiece(kept, setup.Qx, setup.Qy, setup.QDx, setup.QDy, setup.QDDx, setup.QDDy, setup.QLastX, setup.QLastY, setup.CurveShift, setup.Count, setup.Winding, ref contourEdges, ref setupCost);
        return emitted;
    }

    /// <summary>Advances one quad forward-difference state into snapped walker edges, anchoring the snapped start across zero-height steps and threading the pinned slope exactly like updateQuadratic.</summary>
    private static int StepQuadPiece(List<AnalyticEdge> kept, int qx, int qy, int qDx, int qDy, int qDDx, int qDDy, int qLastX, int qLastY, int curveShift, int count, int winding, ref int contourEdges, ref long setupCost)
    {
        int emitted = 0;
        int oldx = qx;
        int oldy = qy;
        int dx = qDx;
        int dy = qDy;
        int sx = qx;
        int sy = qy;
        while (true)
        {
            int newx;
            int newy;
            int endx;
            int endy;
            int slope;
            if (--count > 0)
            {
                unchecked
                {
                    newx = oldx + (dx >> curveShift);
                    newy = oldy + (dy >> curveShift);
                }
                if (SkAbs32(dy >> curveShift) >= 2 * Fixed1 && ((long)SkAbs32(dy) << 6) > SkAbs32(dx))
                {
                    int diffY = (newy - sy) >> 10;
                    slope = diffY != 0 ? QuickDiv((newx - sx) >> 10, diffY) : int.MaxValue;
                    endy = Math.Min(qLastY, FixedRoundToFixed(newy));
                    endx = unchecked(newx - FixedMul(slope, newy - endy));
                }
                else
                {
                    endy = Math.Min(qLastY, SnapY(newy));
                    endx = newx;
                    int diffY = (endy - sy) >> 10;
                    slope = diffY != 0 ? QuickDiv((newx - sx) >> 10, diffY) : int.MaxValue;
                }
                unchecked
                {
                    dx += qDDx;
                    dy += qDDy;
                }
            }
            else
            {
                newx = qLastX;
                newy = qLastY;
                endx = newx;
                endy = newy;
                int diffY = (newy - sy) >> 10;
                slope = diffY != 0 ? QuickDiv((newx - sx) >> 10, diffY) : int.MaxValue;
            }
            if (slope < int.MaxValue)
            {
                AnalyticEdge? edge = SetLineSloped(sx, sy, endx, endy, winding, slope);
                if (edge is not null)
                {
                    if (!(edge.DX == 0 && contourEdges > 0 && CombineVertical(edge, kept[^1]))) { kept.Add(edge); contourEdges++; }
                    emitted++;
                    sx = endx;
                    sy = endy;
                }
            }
            oldx = newx;
            oldy = newy;
            setupCost += 2;
            if (count <= 0) break;
        }
        return emitted;
    }

    /// <summary>Validates retained quadratic and cubic spans for walker substitution: single clean contour with ordered non-overlapping ranges inside it.</summary>
    /// <remarks>Any anomaly falls back to flattened walking rather than failing; spans are an optimization hint and the flattened path stays canonical. Overlapping or out-of-range spans decline substitution while disjoint touching-at-joint spans tile exactly, across both channels alike.</remarks>
    private static bool CurveSpansEligible(ReadOnlySpan<int> ends, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, bool isEvenOdd, SvgPolygonMapping mapping, SvgContourFlags structure)
    {
        if (quadSpans.Length == 0 && cubicSpans.Length == 0) return false;
        if (isEvenOdd || mapping != SvgPolygonMapping.FlattenedCurves || structure != SvgContourFlags.None || ends.Length != 1) return false;
        return ContourSpansValid(0, ends[0], quadSpans, 0, quadSpans.Length, cubicSpans, 0, cubicSpans.Length);
    }

    /// <summary>Validates one contour slice run pair for walker substitution with ordered non-overlapping ranges inside the contour.</summary>
    private static bool ContourSpansValid(int contourStart, int contourEnd, SvgQuadSpan[] quadSpans, int quadStart, int quadEnd, SvgCubicSpan[] cubicSpans, int cubicStart, int cubicEnd)
    {
        int quad = quadStart;
        int cubic = cubicStart;
        int previousEnd = contourStart;
        while (quad < quadEnd || cubic < cubicEnd)
        {
            bool takeQuad = cubic >= cubicEnd || (quad < quadEnd && quadSpans[quad].StartIndex <= cubicSpans[cubic].StartIndex);
            int start;
            int end;
            if (takeQuad)
            {
                SvgQuadSpan span = quadSpans[quad++];
                start = span.StartIndex;
                end = span.EndIndex;
                if (!double.IsFinite(span.Control.X) || !double.IsFinite(span.Control.Y)) return false;
            }
            else
            {
                SvgCubicSpan span = cubicSpans[cubic++];
                start = span.StartIndex;
                end = span.EndIndex;
                if (!double.IsFinite(span.FirstControl.X) || !double.IsFinite(span.FirstControl.Y) || !double.IsFinite(span.SecondControl.X) || !double.IsFinite(span.SecondControl.Y)) return false;
            }
            if (start < contourStart || end > contourEnd || end <= start || start < previousEnd) return false;
            previousEnd = end;
        }
        return true;
    }

    /// <summary>Derives the forward-difference subdivision shift from quad curvature, mirroring diff_to_shift.</summary>
    private static int QuadStepShift(int dx, int dy)
    {
        int adx = SkAbs32(dx);
        int ady = SkAbs32(dy);
        int dist = adx > ady ? adx + (ady / 2) : ady + (adx / 2);
        dist = (dist + (1 << 4)) >> 5;
        return (32 - BitOperations.LeadingZeroCount((uint)dist)) >> 1;
    }

    private static void GoY(AnalyticEdge edge, int y)
    {
        if (y == edge.Y + Fixed1) { edge.X += edge.DX; edge.Y = y; }
        else if (y != edge.Y) { edge.X = edge.UpperX + FixedMul(edge.DX, y - edge.UpperY); edge.Y = y; }
    }
    private static void GoYShift(AnalyticEdge edge, int y, int shift)
    {
        edge.Y = y;
        edge.X += edge.DX >> shift;
    }
    private static bool CombineVertical(AnalyticEdge edge, AnalyticEdge last)
    {
        if (last.InitialCurveCount != 0) return false;
        if (last.DX != 0 || edge.DX != 0 || edge.X != last.X) return false;
        if (edge.Winding == last.Winding)
        {
            if (edge.LowerY == last.UpperY) { last.UpperY = edge.UpperY; last.Y = last.UpperY; return true; }
            if (SkAbs32(edge.UpperY - last.LowerY) < 0x100) { last.LowerY = edge.LowerY; return true; }
            return false;
        }
        if (SkAbs32(edge.UpperY - last.UpperY) < 0x100)
        {
            if (SkAbs32(edge.LowerY - last.LowerY) < 0x100) return true;
            if (edge.LowerY < last.LowerY) { last.UpperY = edge.LowerY; last.Y = last.UpperY; return false; }
            last.UpperY = last.LowerY; last.Y = last.UpperY; last.LowerY = edge.LowerY; last.Winding = edge.Winding; return false;
        }
        if (SkAbs32(edge.LowerY - last.LowerY) < 0x100)
        {
            if (edge.UpperY > last.UpperY) { last.LowerY = edge.UpperY; return false; }
            last.LowerY = last.UpperY; last.UpperY = edge.UpperY; last.Y = last.UpperY; last.Winding = edge.Winding; return false;
        }
        return false;
    }
    private static double TwiceArea(ReadOnlySpan<SvgDevicePoint> points)
    {
        double twice = 0;
        for (int index = 0; index < points.Length; index++)
        {
            SvgDevicePoint first = points[index];
            SvgDevicePoint second = points[(index + 1) % points.Length];
            twice += (second.X - first.X) * (second.Y + first.Y);
        }
        return twice;
    }
    private static bool IsConvexRing(ReadOnlySpan<SvgDevicePoint> points, CancellationToken cancellationToken)
    {
        int sign = 0;
        for (int index = 0; index < points.Length; index++)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            SvgDevicePoint previous = points[(index + points.Length - 1) % points.Length];
            SvgDevicePoint current = points[index];
            SvgDevicePoint next = points[(index + 1) % points.Length];
            double turn = (current.X - previous.X) * (next.Y - current.Y) - (current.Y - previous.Y) * (next.X - current.X);
            if (turn > 0) { if (sign < 0) return false; sign = 1; }
            else if (turn < 0) { if (sign > 0) return false; sign = -1; }
        }
        return true;
    }

    /// <summary>Returns the caller-reserved scratch byte count for one fill with the given point count and row width.</summary>
    internal static long ScratchByteCount(int pointCount, int rowWidth)
    {
        long bytes;
        try { bytes = checked(ParserCollectionMemory.GetArrayByteCount<byte>(rowWidth + 2) + ParserCollectionMemory.GetArrayByteCount<int>(rowWidth + 4) + ParserCollectionMemory.GetArrayByteCount<int>(rowWidth + 4) + 32 + ((long)pointCount + 2) * 80); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Analytic coverage storage exceeds the supported accounting range."); }
        return bytes;
    }
    /// <summary>Validates retained contours like the shared fan path, then reserves analytic scratch for one fill without rigid contacts.</summary>
    /// <remarks>Validation mirrors the fan coverage guards exactly (partitioned ends, role counts and values) so malformed payloads fail identically. Single rings keep the exact legacy sequence, extended to nonzero pure-crossing rings on curve-flattened payloads whose signed winding the edge walk accumulates directly; multi-contour sets additionally admit disjoint-or-nested contact-free loops with per-contour area and vote filtering plus a set-level contact scan, extended to nonzero crossing-, touching- or self-touching sets on curve-flattened payloads without coincident contacts, keeping integer and fractional straight-edge payloads on the exact fan path, with self-touching loops walking their shared vertices through the same signed-winding accumulation, reporting ineligible without reserving otherwise, so the caller keeps the legacy path; the returned value still requires disposal. The setup cost covers validation, area, convexity and self-contact scoring, edge setup and a sorting upper bound for the caller work ledger.</remarks>
    internal static SvgAnalyticCoverage Prepare(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> ends, ReadOnlySpan<int> nestingSigns, bool isEvenOdd, int[]? signs, SvgPolygonMapping mapping, int xStart, int xEnd, int yStart, int yEnd, int outputWidth, int outputHeight, RenderBudget budget, out long setupWork, out bool eligible, CancellationToken cancellationToken)
    {
        return Prepare(points, ends, nestingSigns, isEvenOdd, signs, mapping, Array.Empty<SvgQuadSpan>(), xStart, xEnd, yStart, yEnd, outputWidth, outputHeight, budget, out setupWork, out eligible, cancellationToken);
    }

    /// <summary>Prepares one analytic fill, substituting analytic quad segments for retained span ranges under single-contour clean-structure gating.</summary>
    internal static SvgAnalyticCoverage Prepare(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> ends, ReadOnlySpan<int> nestingSigns, bool isEvenOdd, int[]? signs, SvgPolygonMapping mapping, SvgQuadSpan[] quadSpans, int xStart, int xEnd, int yStart, int yEnd, int outputWidth, int outputHeight, RenderBudget budget, out long setupWork, out bool eligible, CancellationToken cancellationToken)
    {
        return Prepare(points, ends, nestingSigns, isEvenOdd, signs, mapping, quadSpans, Array.Empty<SvgCubicSpan>(), xStart, xEnd, yStart, yEnd, outputWidth, outputHeight, budget, out setupWork, out eligible, cancellationToken);
    }

    /// <summary>Prepares one analytic fill, substituting analytic curve segments for retained quadratic and cubic span ranges under single-contour clean-structure gating.</summary>
    internal static SvgAnalyticCoverage Prepare(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> ends, ReadOnlySpan<int> nestingSigns, bool isEvenOdd, int[]? signs, SvgPolygonMapping mapping, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, int xStart, int xEnd, int yStart, int yEnd, int outputWidth, int outputHeight, RenderBudget budget, out long setupWork, out bool eligible, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(quadSpans);
        ArgumentNullException.ThrowIfNull(cubicSpans);
        SvgPolygonCoverage.ValidateContourEnds(points, ends);
        ReadOnlySpan<int> votes = isEvenOdd ? nestingSigns : (signs is int[] roles ? roles : ReadOnlySpan<int>.Empty);
        if (!isEvenOdd && signs is not null && signs.Length != ends.Length) throw new ArgumentOutOfRangeException(nameof(signs));
        if (isEvenOdd && nestingSigns.Length != ends.Length) throw new ArgumentOutOfRangeException(nameof(nestingSigns));
        bool hasVotes = isEvenOdd || signs is not null;
        for (int contour = 0; contour < ends.Length; contour++)
        {
            if ((contour & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (hasVotes && votes[contour] is not (-1 or 0 or 1)) throw new ArgumentOutOfRangeException(isEvenOdd ? nameof(nestingSigns) : nameof(signs));
        }
        setupWork = (long)points.Length + ends.Length;
        eligible = false;
        if (ends.Length < 1 || points.Length < 3 || xEnd <= xStart) return default;
        double maxAbs = 0;
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        for (int index = 0; index < points.Length; index++)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            double px = points[index].X;
            double py = points[index].Y;
            if (!double.IsFinite(px) || !double.IsFinite(py)) return default;
            double ax = Math.Abs(px);
            double ay = Math.Abs(py);
            if (ax > maxAbs) maxAbs = ax;
            if (ay > maxAbs) maxAbs = ay;
            if (px < minX) minX = px;
            if (px > maxX) maxX = px;
            if (py < minY) minY = py;
            if (py > maxY) maxY = py;
        }
        if (maxAbs > MaxRange) return default;
        if (Math.Floor(minX) < 0 || Math.Floor(minY) < 0 || Math.Ceiling(maxX) > outputWidth || Math.Ceiling(maxY) > outputHeight) return default;
        if (ends.Length > 1)
        {

            int keptContours = 0;
            int previousEnd = 0;
            for (int contour = 0; contour < ends.Length; contour++)
            {
                if ((contour & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                int start = previousEnd;
                int end = ends[contour];
                previousEnd = end;
                int count = end - start;
                if (count < 3) continue;
                if (TwiceArea(points.Slice(start, count)) == 0) continue;
                if (hasVotes && votes[contour] == 0) continue;
                keptContours++;
            }
            if (keptContours == 0) return default;
            SvgContourFlags setStructure = SvgContourStructure.ClassifySet(points, ends, cancellationToken);
            setupWork += (long)points.Length * points.Length;
            bool hasRigidContact = isEvenOdd || mapping != SvgPolygonMapping.FlattenedCurves || (setStructure & SvgContourFlags.CoincidentOverlap) != SvgContourFlags.None;
            // Transversal-only unions walk exact signed-winding intervals on any mapping: strict
            // crossings carry no shared vertices, so event handoff stays clean while each interval
            // keeps its own trapezoid fringe instead of the fan exact-area underestimate.
            // Touches, self-touches, coincident spans and even-odd sets keep their gates.
            bool transversalUnion = false;
            if (!isEvenOdd && (setStructure & (SvgContourFlags.ProperCrossing | SvgContourFlags.SelfTouch | SvgContourFlags.CoincidentOverlap)) == SvgContourFlags.None)
            {
                setupWork += (long)points.Length * points.Length;
                transversalUnion = SvgContourStructure.AllInterLoopContactsTransversal(points, ends, cancellationToken);
            }
            if (!transversalUnion && hasRigidContact && (setStructure & (SvgContourFlags.ProperCrossing | SvgContourFlags.SelfTouch | SvgContourFlags.CoincidentOverlap | SvgContourFlags.TouchesOtherContour)) != SvgContourFlags.None) return default;
            int multiWidth = xEnd - xStart;
            bool multiSkipIntersect = (long)points.Length > (long)(yEnd - yStart) * 2;
            RenderBudget.BudgetReservation? multiScratch = budget.Reserve(ScratchByteCount(points.Length, multiWidth), SvgMemoryCategory.Surface);
            try
            {
                List<AnalyticEdge> multiKept = new(points.Length);
                bool useMultiSpans = (quadSpans.Length > 0 || cubicSpans.Length > 0) && !isEvenOdd && mapping == SvgPolygonMapping.FlattenedCurves;
                int quadCursor = 0;
                int cubicCursor = 0;
                int priorEnd = 0;
                for (int contour = 0; contour < ends.Length; contour++)
                {
                    if ((contour & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                    int start = priorEnd;
                    int end = ends[contour];
                    priorEnd = end;
                    int count = end - start;
                    if (count < 3) continue;
                    if (TwiceArea(points.Slice(start, count)) == 0) continue;
                    if (hasVotes && votes[contour] == 0) continue;
                    bool contourSpans = false;
                    int quadRun = quadCursor;
                    int cubicRun = cubicCursor;
                    int quadRunEnd = quadRun;
                    int cubicRunEnd = cubicRun;
                    if (useMultiSpans)
                    {
                        while (quadRun < quadSpans.Length && quadSpans[quadRun].Contour < contour) quadRun++;
                        while (cubicRun < cubicSpans.Length && cubicSpans[cubicRun].Contour < contour) cubicRun++;
                        quadRunEnd = quadRun;
                        while (quadRunEnd < quadSpans.Length && quadSpans[quadRunEnd].Contour == contour) quadRunEnd++;
                        cubicRunEnd = cubicRun;
                        while (cubicRunEnd < cubicSpans.Length && cubicSpans[cubicRunEnd].Contour == contour) cubicRunEnd++;
                        contourSpans = (quadRunEnd > quadRun || cubicRunEnd > cubicRun) && ContourSpansValid(start, end, quadSpans, quadRun, quadRunEnd, cubicSpans, cubicRun, cubicRunEnd);
                        quadCursor = quadRun;
                        cubicCursor = cubicRun;
                    }
                    int contourEdges = 0;
                    int quadAt = quadRun;
                    int cubicAt = cubicRun;
                    for (int index = start; index < end; index++)
                    {
                        if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                        if (contourSpans && quadAt < quadRunEnd && quadSpans[quadAt].StartIndex == index)
                        {
                            SvgQuadSpan span = quadSpans[quadAt];
                            quadAt++;
                            if (span.EndIndex - index >= 2 && AppendQuadSpanEdges(multiKept, points[span.StartIndex].X, points[span.StartIndex].Y, span.Control.X, span.Control.Y, points[span.EndIndex].X, points[span.EndIndex].Y, ref contourEdges, ref setupWork, cancellationToken) > 0)
                            {
                                index = span.EndIndex - 1;
                                continue;
                            }
                        }
                        else if (contourSpans && cubicAt < cubicRunEnd && cubicSpans[cubicAt].StartIndex == index)
                        {
                            SvgCubicSpan cubic = cubicSpans[cubicAt];
                            cubicAt++;
                            if (cubic.EndIndex - index >= 2 && AppendCubicLiveEdges(multiKept, points[cubic.StartIndex].X, points[cubic.StartIndex].Y, cubic.FirstControl.X, cubic.FirstControl.Y, cubic.SecondControl.X, cubic.SecondControl.Y, points[cubic.EndIndex].X, points[cubic.EndIndex].Y, ref contourEdges, ref setupWork, cancellationToken) > 0)
                            {
                                index = cubic.EndIndex - 1;
                                continue;
                            }
                        }
                        SvgDevicePoint multiFirst = points[index];
                        SvgDevicePoint multiSecond = points[start + ((index - start + 1) % count)];
                        AnalyticEdge? multiEdge = SetLine(multiFirst.X, multiFirst.Y, multiSecond.X, multiSecond.Y, true);
                        if (multiEdge is null) continue;
                        if (multiEdge.DX == 0 && contourEdges > 0 && CombineVertical(multiEdge, multiKept[^1])) continue;
                        multiKept.Add(multiEdge);
                        contourEdges++;
                    }
                }
                AnalyticEdge[] multiEdges = multiKept.ToArray();
                System.Array.Sort(multiEdges, EdgeOrder.Instance);
                AnalyticEdge multiHead = new() { UpperY = int.MinValue, LowerY = int.MinValue, X = int.MinValue, UpperX = int.MinValue, DY = int.MaxValue };
                AnalyticEdge multiTail = new() { UpperY = int.MaxValue, LowerY = int.MaxValue, X = int.MaxValue, UpperX = int.MaxValue, DY = int.MaxValue };
                multiHead.Next = multiEdges.Length > 0 ? multiEdges[0] : multiTail;
                if (multiEdges.Length > 0) multiEdges[0].Prev = multiHead;
                else multiHead.Next = multiTail;
                for (int index = 1; index < multiEdges.Length; index++) { multiEdges[index - 1].Next = multiEdges[index]; multiEdges[index].Prev = multiEdges[index - 1]; }
                if (multiEdges.Length > 0) { multiEdges[^1].Next = multiTail; multiTail.Prev = multiEdges[^1]; }
                else multiTail.Prev = multiHead;
                byte[] multiRow = new byte[multiWidth + 2];
                int[] multiTemp = new int[multiWidth + 4];
                int[] multiAlphas = new int[multiWidth + 4];
                int multiLevels = 0;
                for (int level = multiEdges.Length; level > 0; level >>= 1) multiLevels++;
                setupWork += (long)multiEdges.Length * (multiLevels + 3);
                eligible = true;
                return new SvgAnalyticCoverage(multiEdges, multiHead, multiTail, multiRow, multiTemp, multiAlphas, xStart, multiWidth, yEnd, multiSkipIntersect, isEvenOdd, false, multiScratch);
            }
            catch { multiScratch?.Dispose(); throw; }
        }
        if (TwiceArea(points) == 0) return default;
        SvgContourFlags structure = SvgContourStructure.ClassifyContour(points, 0, points.Length, cancellationToken);
        setupWork += (long)points.Length * (points.Length - 1) / 2;
        bool rigidRing = isEvenOdd || mapping != SvgPolygonMapping.FlattenedCurves || structure != SvgContourFlags.ProperCrossing;
        if (rigidRing && (structure & (SvgContourFlags.ProperCrossing | SvgContourFlags.SelfTouch | SvgContourFlags.CoincidentOverlap)) != SvgContourFlags.None) return default;
        bool convex = IsConvexRing(points, cancellationToken);
        // Desnap only proven axis-aligned boxes: identical doubles convert identically, so shared
        // vertices stay shared without snap, and the convex walk consumes true fractional events
        // directly. Everything else keeps quarter snap for event-handoff topology.
        bool desnapBox = convex;
        if (desnapBox)
        {
            for (int boxIndex = 0; boxIndex < points.Length; boxIndex++)
            {
                if ((boxIndex & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                SvgDevicePoint boxFirst = points[boxIndex];
                SvgDevicePoint boxSecond = points[(boxIndex + 1) % points.Length];
                if (boxFirst.X != boxSecond.X && boxFirst.Y != boxSecond.Y)
                {
                    desnapBox = false;
                    break;
                }
            }
        }
        bool useCurveSpans = CurveSpansEligible(ends, quadSpans, cubicSpans, isEvenOdd, mapping, structure);
        setupWork += points.Length;
        int width = xEnd - xStart;
        bool skipIntersect = (long)points.Length > (long)(yEnd - yStart) * 2;
        RenderBudget.BudgetReservation? scratch = budget.Reserve(ScratchByteCount(points.Length, width), SvgMemoryCategory.Surface);
        try
        {
            List<AnalyticEdge> kept = new(points.Length);
            int spanCursor = 0;
            int cubicCursor = 0;
            for (int index = 0; index < points.Length; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (useCurveSpans && spanCursor < quadSpans.Length && quadSpans[spanCursor].StartIndex == index)
                {
                    SvgQuadSpan span = quadSpans[spanCursor];
                    spanCursor++;
                    int spanContourEdges = kept.Count;
                    bool quadSubstituted = false;
                    if (span.EndIndex - index >= 2)
                    {
                        if (!convex && AppendQuadLiveEdges(kept, points[span.StartIndex].X, points[span.StartIndex].Y, span.Control.X, span.Control.Y, points[span.EndIndex].X, points[span.EndIndex].Y, ref spanContourEdges, ref setupWork, cancellationToken) > 0) quadSubstituted = true;
                        else if (AppendQuadSpanEdges(kept, points[span.StartIndex].X, points[span.StartIndex].Y, span.Control.X, span.Control.Y, points[span.EndIndex].X, points[span.EndIndex].Y, ref spanContourEdges, ref setupWork, cancellationToken) > 0) quadSubstituted = true;
                    }
                    if (quadSubstituted)
                    {
                        // Resume on the span end so the edge leaving it (including the implicit close) still walks flattened, which also chains touching spans exactly.
                        index = span.EndIndex - 1;
                        continue;
                    }
                }
                else if (useCurveSpans && cubicCursor < cubicSpans.Length && cubicSpans[cubicCursor].StartIndex == index)
                {
                    SvgCubicSpan cubic = cubicSpans[cubicCursor];
                    cubicCursor++;
                    int cubicContourEdges = kept.Count;
                    // Non-convex rings walk intervals like multi-contour sets, so substituted spans go live;
                    // convex rings keep pre-stepped segments for the convex fast path, which cannot advance live state.
                    bool substituted = false;
                    if (cubic.EndIndex - index >= 2)
                    {
                        if (!convex && AppendCubicLiveEdges(kept, points[cubic.StartIndex].X, points[cubic.StartIndex].Y, cubic.FirstControl.X, cubic.FirstControl.Y, cubic.SecondControl.X, cubic.SecondControl.Y, points[cubic.EndIndex].X, points[cubic.EndIndex].Y, ref cubicContourEdges, ref setupWork, cancellationToken) > 0) substituted = true;
                        else if (AppendCubicSpanEdges(kept, points[cubic.StartIndex].X, points[cubic.StartIndex].Y, cubic.FirstControl.X, cubic.FirstControl.Y, cubic.SecondControl.X, cubic.SecondControl.Y, points[cubic.EndIndex].X, points[cubic.EndIndex].Y, ref cubicContourEdges, ref setupWork, cancellationToken) > 0) substituted = true;
                    }
                    if (substituted)
                    {
                        index = cubic.EndIndex - 1;
                        continue;
                    }
                }
                SvgDevicePoint first = points[index];
                SvgDevicePoint second = points[(index + 1) % points.Length];
                AnalyticEdge? edge = SetLine(first.X, first.Y, second.X, second.Y, !desnapBox);
                if (edge is null) continue;
                if (edge.DX == 0 && kept.Count > 0 && CombineVertical(edge, kept[^1])) continue;
                kept.Add(edge);
            }
            AnalyticEdge[] edges = kept.ToArray();
            System.Array.Sort(edges, EdgeOrder.Instance);
            AnalyticEdge head = new() { UpperY = int.MinValue, LowerY = int.MinValue, X = int.MinValue, UpperX = int.MinValue, DY = int.MaxValue };
            AnalyticEdge tail = new() { UpperY = int.MaxValue, LowerY = int.MaxValue, X = int.MaxValue, UpperX = int.MaxValue, DY = int.MaxValue };
            head.Next = edges.Length > 0 ? edges[0] : tail;
            if (edges.Length > 0) edges[0].Prev = head;
            else head.Next = tail;
            for (int index = 1; index < edges.Length; index++) { edges[index - 1].Next = edges[index]; edges[index].Prev = edges[index - 1]; }
            if (edges.Length > 0) { edges[^1].Next = tail; tail.Prev = edges[^1]; }
            else tail.Prev = head;
            byte[] row = new byte[width + 2];
            int[] temp = new int[width + 4];
            int[] alphas = new int[width + 4];
            int levels = 0;
            for (int level = edges.Length; level > 0; level >>= 1) levels++;
            setupWork += (long)edges.Length * (levels + 3);
            eligible = true;
            return new SvgAnalyticCoverage(edges, head, tail, row, temp, alphas, xStart, width, yEnd, skipIntersect, isEvenOdd, convex, scratch);
        }
        catch { scratch?.Dispose(); throw; }
    }
    private static int TrapezoidToAlpha(int first, int second) => (int)(((long)(first + second) / 2) >> 8);
    private static int PartialTriangleToAlpha(int edge, int inverseDY)
    {
        long scaled = ((long)(edge >> 11)) * (edge >> 11) * (inverseDY >> 11);
        return (int)((scaled >> 8) & 0xFF);
    }
    private static int PartialAlpha(int alpha, int partialHeight) => FixedRoundToInt(alpha * partialHeight);
    /// <summary>Truncates axis-aligned box-row coverage like Chrome: pure horizontal partial rows evaluate floor of coverage times 255 (127.5 to 127) while slanted rows keep the pinned rounding that 22 diagonal exact fixtures verify.</summary>
    private static int PartialAlphaTrunc(int alpha, int partialHeight) => (int)(((long)alpha * partialHeight) >> 16);
    private static int PartialAlphaFull(int alpha, int fullAlpha) => (alpha * fullAlpha) >> 8;
    private static int FixedToAlpha(int value) => PartialAlpha(255, value);
    private static int FixedToAlphaTrunc(int value) => PartialAlphaTrunc(255, value);
    private void AddMask(int x, int alpha)
    {
        if (x < 0 || x >= _width) return;
        int offset = x + 1;
        int sum = _row[offset] + alpha;
        _row[offset] = sum > 255 ? (byte)255 : (byte)sum;
    }
    private void SetMask(int x, int alpha)
    {
        if (x < 0 || x >= _width) return;
        _row[x + 1] = (byte)alpha;
    }
    private void ComputeAboveLine(int destination, int l, int r, int inverseDY, int fullAlpha)
    {
        int count = FixedCeilToInt(r);
        if (count == 0) return;
        if (count == 1) { _temp[destination] = PartialAlphaFull(((1 << 17) - l - r) >> 9, fullAlpha); return; }
        int first = Fixed1 - l;
        int last = r - ((count - 1) << 16);
        int firstH = FixedMul(first, inverseDY);
        _temp[destination] = FixedMul(first, firstH) >> 9;
        int alpha16 = firstH + (inverseDY >> 1);
        for (int index = 1; index < count - 1; index++) { _temp[destination + index] = alpha16 >> 8; alpha16 += inverseDY; }
        _temp[destination + count - 1] = fullAlpha - PartialTriangleToAlpha(last, inverseDY);
    }
    private void ComputeBelowLine(int destination, int l, int r, int inverseDY, int fullAlpha)
    {
        int count = FixedCeilToInt(r);
        if (count == 0) return;
        if (count == 1) { _temp[destination] = PartialAlphaFull(TrapezoidToAlpha(l, r), fullAlpha); return; }
        int first = Fixed1 - l;
        int last = r - ((count - 1) << 16);
        int lastH = FixedMul(last, inverseDY);
        _temp[destination + count - 1] = FixedMul(last, lastH) >> 9;
        int alpha16 = lastH + (inverseDY >> 1);
        for (int index = count - 2; index > 0; index--) { _temp[destination + index] = (alpha16 >> 8) & 0xFF; alpha16 += inverseDY; }
        _temp[destination] = fullAlpha - PartialTriangleToAlpha(first, inverseDY);
    }
    private void BlitSingleAlpha(int x, int alpha, int fullAlpha, bool noRealBlitter)
    {
        if (fullAlpha == 255 && !noRealBlitter) SetMask(x, alpha);
        else AddMask(x, PartialAlphaFull(alpha, fullAlpha));
    }
    /// <summary>Adds one two-pixel fringe pair into the mask row, always accumulating like the pinned mask blitter.</summary>
    private void BlitTwoAlphas(int x, int first, int second, int fullAlpha, bool noRealBlitter)
    {
        _ = fullAlpha;
        _ = noRealBlitter;
        AddMask(x, first);
        AddMask(x + 1, second);
    }
    private void BlitFullAlpha(int x, int length, int fullAlpha)
    {
        for (int index = 0; index < length; index++) AddMask(x + index, fullAlpha);
    }
    private void BlitAaaRow(int upperLeft, int upperRight, int lowerLeft, int lowerRight, int leftDY, int rightDY, int fullAlpha, bool noRealBlitter)
    {
        int left = FixedFloorToInt(upperLeft);
        int right = FixedCeilToInt(lowerRight);
        int length = right - left;
        if (length == 1) { BlitSingleAlpha(left - _xStart, TrapezoidToAlpha(upperRight - upperLeft, lowerRight - lowerLeft), fullAlpha, noRealBlitter); return; }
        for (int index = 0; index < length; index++) _alphas[index] = fullAlpha;
        int upperFloor = FixedFloorToInt(upperLeft);
        int lowerCeil = FixedCeilToInt(lowerLeft);
        if (upperFloor + 2 == lowerCeil)
        {
            int first = (upperFloor << 16) + Fixed1 - upperLeft;
            int second = lowerLeft - upperLeft - first;
            int firstAlpha = fullAlpha - PartialTriangleToAlpha(first, leftDY);
            int secondAlpha = PartialTriangleToAlpha(second, leftDY);
            if (_alphas[0] > firstAlpha) _alphas[0] -= firstAlpha; else _alphas[0] = 0;
            if (_alphas[1] > secondAlpha) _alphas[1] -= secondAlpha; else _alphas[1] = 0;
        }
        else
        {
            ComputeBelowLine(upperFloor - left, upperLeft - (upperFloor << 16), lowerLeft - (upperFloor << 16), leftDY, fullAlpha);
            for (int index = upperFloor; index < lowerCeil; index++)
            {
                if (_alphas[index - left] > _temp[index - left]) _alphas[index - left] -= _temp[index - left];
                else _alphas[index - left] = 0;
            }
        }
        int rightFloor = FixedFloorToInt(upperRight);
        int rightCeil = FixedCeilToInt(lowerRight);
        if (rightFloor + 2 == rightCeil)
        {
            int first = (rightFloor << 16) + Fixed1 - upperRight;
            int second = lowerRight - upperRight - first;
            int firstAlpha = PartialTriangleToAlpha(first, rightDY);
            int secondAlpha = fullAlpha - PartialTriangleToAlpha(second, rightDY);
            if (_alphas[length - 2] > firstAlpha) _alphas[length - 2] -= firstAlpha; else _alphas[length - 2] = 0;
            if (_alphas[length - 1] > secondAlpha) _alphas[length - 1] -= secondAlpha; else _alphas[length - 1] = 0;
        }
        else
        {
            ComputeAboveLine(rightFloor - left, upperRight - (rightFloor << 16), lowerRight - (rightFloor << 16), rightDY, fullAlpha);
            for (int index = rightFloor; index < rightCeil; index++)
            {
                if (_alphas[index - left] > _temp[index - left]) _alphas[index - left] -= _temp[index - left];
                else _alphas[index - left] = 0;
            }
        }
        for (int index = 0; index < length; index++) AddMask(left + index - _xStart, _alphas[index]);
    }
    private static int ApproximateIntersection(int firstLeft, int firstRight, int secondLeft, int secondRight)
    {
        if (firstLeft > firstRight) (firstLeft, firstRight) = (firstRight, firstLeft);
        if (secondLeft > secondRight) (secondLeft, secondRight) = (secondRight, secondLeft);
        return (Math.Max(firstLeft, secondLeft) + Math.Min(firstRight, secondRight)) / 2;
    }
    private void BlitTrapezoidRow(int upperLeft, int upperRight, int lowerLeft, int lowerRight, int leftDY, int rightDY, int fullAlpha, bool noRealBlitter)
    {
        if (upperLeft > upperRight) return;
        if (lowerLeft > lowerRight) lowerLeft = lowerRight = ApproximateIntersection(upperLeft, lowerLeft, upperRight, lowerRight);
        if (upperLeft == upperRight && lowerLeft == lowerRight) return;
        if (upperLeft > lowerLeft) (upperLeft, lowerLeft) = (lowerLeft, upperLeft);
        if (upperRight > lowerRight) (upperRight, lowerRight) = (lowerRight, upperRight);
        int joinLeft = FixedCeilToFixed(lowerLeft);
        int joinRight = FixedFloorToFixed(upperRight);
        if (joinLeft <= joinRight)
        {
            if (upperLeft < joinLeft)
            {
                int fringe = FixedCeilToInt(joinLeft - upperLeft);
                if (fringe == 1) BlitSingleAlpha((upperLeft >> 16) - _xStart, TrapezoidToAlpha(joinLeft - upperLeft, joinLeft - lowerLeft), fullAlpha, noRealBlitter);
                else if (fringe == 2)
                {
                    int first = joinLeft - Fixed1 - upperLeft;
                    int second = lowerLeft - upperLeft - first;
                    BlitTwoAlphas((upperLeft >> 16) - _xStart, PartialTriangleToAlpha(first, leftDY), fullAlpha - PartialTriangleToAlpha(second, leftDY), fullAlpha, noRealBlitter);
                }
                else BlitAaaRow(upperLeft, joinLeft, lowerLeft, joinLeft, leftDY, int.MaxValue, fullAlpha, noRealBlitter);
            }
            if (joinLeft < joinRight) BlitFullAlpha(FixedFloorToInt(joinLeft) - _xStart, FixedFloorToInt(joinRight - joinLeft), fullAlpha);
            if (lowerRight > joinRight)
            {
                int fringe = FixedCeilToInt(lowerRight - joinRight);
                if (fringe == 1) BlitSingleAlpha((joinRight >> 16) - _xStart, TrapezoidToAlpha(upperRight - joinRight, lowerRight - joinRight), fullAlpha, noRealBlitter);
                else if (fringe == 2)
                {
                    int first = joinRight + Fixed1 - upperRight;
                    int second = lowerRight - upperRight - first;
                    BlitTwoAlphas((joinRight >> 16) - _xStart, fullAlpha - PartialTriangleToAlpha(first, rightDY), PartialTriangleToAlpha(second, rightDY), fullAlpha, noRealBlitter);
                }
                else BlitAaaRow(joinRight, upperRight, joinRight, lowerRight, int.MaxValue, rightDY, fullAlpha, noRealBlitter);
            }
        }
        else BlitAaaRow(upperLeft, upperRight, lowerLeft, lowerRight, leftDY, rightDY, fullAlpha, noRealBlitter);
    }
    private static void RemoveEdge(AnalyticEdge edge)
    {
        if (edge.Prev is AnalyticEdge prev) prev.Next = edge.Next;
        if (edge.Next is AnalyticEdge next) next.Prev = edge.Prev;
    }
    private static void InsertAfter(AnalyticEdge edge, AnalyticEdge after)
    {
        edge.Prev = after;
        edge.Next = after.Next;
        if (after.Next is AnalyticEdge next) next.Prev = edge;
        after.Next = edge;
    }
    private static void RippleBack(AnalyticEdge edge)
    {
        int x = edge.X;
        AnalyticEdge? previous = edge.Prev;
        while (previous is not null && previous.Prev is not null && previous.X > x) previous = previous.Prev;
        if (previous is not null && previous.Next != edge) { RemoveEdge(edge); InsertAfter(edge, previous); }
    }
    private static AnalyticEdge? BackwardInsertStart(AnalyticEdge? previous, int x)
    {
        while (previous is not null && previous.Prev is not null && previous.X > x) previous = previous.Prev;
        return previous;
    }
    private void UpdateNextNext(int value)
    {
        if (value > _walkY && value < _nextNextY) _nextNextY = value;
    }
    private void CheckIntersection(AnalyticEdge edge, int nextY)
    {
        if (edge.Prev is AnalyticEdge prev && prev.Prev is not null && prev.X + prev.DX > edge.X + edge.DX) _nextNextY = nextY + (Fixed1 >> 2);
    }
    private void CheckIntersectionForward(AnalyticEdge edge, int nextY)
    {
        if (edge.Next is AnalyticEdge next && next.Next is not null && edge.X + edge.DX > next.X + next.DX) _nextNextY = nextY + (Fixed1 >> 2);
    }
    private static bool EdgesTooClose(AnalyticEdge? previous, AnalyticEdge? next, int lowerY)
    {
        return next is not null && previous is not null && next.UpperY < lowerY && previous.X + Fixed1 >= next.X - SkAbs32(next.DX);
    }
    private static bool EdgesTooCloseRite(int previousRite, int upperLeft, int lowerLeft)
    {
        return previousRite > FixedFloorToInt(upperLeft) || previousRite > FixedFloorToInt(lowerLeft);
    }
    private static bool IsSmoothEnoughLine(AnalyticEdge edge, AnalyticEdge next, int stopY)
    {
        if (next.UpperY >= (stopY << 16)) return false;
        long drift = (long)next.DX - edge.DX;
        if (drift > int.MaxValue) drift = int.MaxValue;
        if (drift < int.MinValue) drift = int.MinValue;
        return SkAbs32((int)drift) <= Fixed1 && next.LowerY - next.UpperY >= Fixed1;
    }
    private static bool IsSmoothEnough(AnalyticEdge left, AnalyticEdge right, AnalyticEdge? current, int stopY)
    {
        if (current is null || current.UpperY >= (stopY << 16)) return false;
        if (left.LowerY + Fixed1 < right.LowerY) return IsSmoothEnoughLine(left, current, stopY);
        if (left.LowerY > right.LowerY + Fixed1) return IsSmoothEnoughLine(right, current, stopY);
        AnalyticEdge? following = current.Next;
        if (following is null || following.UpperY >= (stopY << 16)) return false;
        AnalyticEdge first = current;
        AnalyticEdge second = following;
        if (second.UpperX < first.UpperX) (first, second) = (second, first);
        return IsSmoothEnoughLine(left, first, stopY) && IsSmoothEnoughLine(right, second, stopY);
    }
    private void InsertNewEdges(AnalyticEdge? insertion, int y, bool skipIntersect)
    {
        if (insertion is null) return;
        if (insertion.UpperY > y) { UpdateNextNext(insertion.UpperY); return; }
        AnalyticEdge? previous = insertion.Prev;
        if (previous is null || previous.X <= insertion.X)
        {
            while (insertion is not null && insertion.UpperY <= y)
            {
                if (!skipIntersect) CheckIntersection(insertion, y);
                UpdateNextNext(insertion.LowerY);
                insertion = insertion.Next;
            }
            if (insertion is not null) UpdateNextNext(insertion.UpperY);
            return;
        }
        AnalyticEdge? start = BackwardInsertStart(previous, insertion.X);
        while (insertion is not null && insertion.UpperY <= y)
        {
            AnalyticEdge? next = insertion.Next;
            while (true)
            {
                if (start is null || start.Next == insertion) break;
                AnalyticEdge? after = start.Next;
                if (after is null || after.X >= insertion.X) break;
                start = after;
            }
            if (start is not null && start.Next != insertion) { RemoveEdge(insertion); InsertAfter(insertion, start); }
            if (!skipIntersect) CheckIntersection(insertion, y);
            if (!skipIntersect) CheckIntersectionForward(insertion, y);
            UpdateNextNext(insertion.LowerY);
            start = insertion;
            insertion = next;
        }
        if (insertion is not null) UpdateNextNext(insertion.UpperY);
    }
    private void WalkEdgesRow(int target, CancellationToken cancellationToken)
    {
        _head.X = _head.UpperX = _leftClip;
        _tail.X = _tail.UpperX = _rightClip;
        int y = _head.Next is AnalyticEdge first ? first.UpperY : 0;
        _nextNextY = int.MaxValue;
        _walkY = y;
        for (AnalyticEdge? edge = _head.Next; edge is not null && edge.UpperY <= y; edge = edge.Next)
        {
            if ((_strips & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            GoY(edge, y);
            UpdateNextNext(edge.LowerY);
            _strips++;
        }
        AnalyticEdge? probe = _head.Next;
        while (probe is not null && probe.UpperY <= y) probe = probe.Next;
        if (probe is not null) UpdateNextNext(probe.UpperY);
        int windingMask = _isEvenOdd ? 1 : -1;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _strips++;
            int winding = 0;
            bool inside = false;
            int previousX = _head.X;
            int nextY = Math.Min(_nextNextY, FixedCeilToFixed(y + 1));
            AnalyticEdge? current = _head.Next;
            AnalyticEdge leftEdge = _head;
            int left = _leftClip;
            int leftDY = 0;
            int previousRite = FixedFloorToInt(_leftClip);
            _nextNextY = int.MaxValue;
            _walkY = y;
            int shift = 0;
            if (((nextY - y) & (Fixed1 >> 2)) != 0) { shift = 2; nextY = y + (Fixed1 >> 2); }
            else if (((nextY - y) & (Fixed1 >> 1)) != 0) { shift = 1; }
            int fullAlpha = FixedToAlpha(nextY - y);
            while (current is AnalyticEdge cursor && cursor.UpperY <= y)
            {
                winding += cursor.Winding;
                bool wasInside = inside;
                inside = (winding & windingMask) != 0;
                if (!inside && wasInside)
                {
                    int rite = cursor.X;
                    GoYShift(cursor, nextY, shift);
                    int nextLeft = Math.Max(_leftClip, leftEdge.X);
                    rite = Math.Min(_rightClip, rite);
                    int nextRite = Math.Min(_rightClip, cursor.X);
                    if ((y >> 16) == target)
                    {
                        bool close = fullAlpha == 255 && (EdgesTooCloseRite(previousRite, left, leftEdge.X) || EdgesTooClose(cursor, cursor.Next, nextY));
                        BlitTrapezoidRow(left, rite, nextLeft, nextRite, leftDY, cursor.DY, fullAlpha, close);
                    }
                    previousRite = FixedCeilToInt(Math.Max(rite, cursor.X));
                    _strips++;
                }
                else
                {
                    if (inside && !wasInside) { left = Math.Max(cursor.X, _leftClip); leftDY = cursor.DY; leftEdge = cursor; }
                    GoYShift(cursor, nextY, shift);
                }
                AnalyticEdge? next = cursor.Next;
                if (cursor.LowerY <= nextY)
                {
                    while (cursor.CurveCount != 0 && cursor.LowerY <= nextY)
                    {
                        if (cursor.CurveCount < 0)
                        {
                            cursor.FCx = cursor.X;
                            cursor.SnappedY = cursor.Y;
                            if (!AdvanceCubicLive(cursor, ref _strips)) break;
                        }
                        else
                        {
                            cursor.QSnappedX = cursor.X;
                            cursor.SnappedY = cursor.Y;
                            if (!AdvanceQuadLive(cursor, ref _strips)) break;
                        }
                    }
                    if (cursor.LowerY <= nextY) RemoveEdge(cursor);
                    else
                    {
                        UpdateNextNext(cursor.LowerY);
                        int freshX = cursor.X;
                        if (freshX < previousX) RippleBack(cursor);
                        else previousX = freshX;
                        if (!_skipIntersect) CheckIntersection(cursor, nextY);
                    }
                }
                else
                {
                    UpdateNextNext(cursor.LowerY);
                    int freshX = cursor.X;
                    if (freshX < previousX) RippleBack(cursor);
                    else previousX = freshX;
                    if (!_skipIntersect) CheckIntersection(cursor, nextY);
                }
                current = next;
            }
            if (inside && (y >> 16) == target)
            {
                bool close = fullAlpha == 255 && EdgesTooClose(leftEdge.Prev, leftEdge, nextY);
                BlitTrapezoidRow(left, _rightClip, Math.Max(_leftClip, leftEdge.X), _rightClip, leftDY, 0, fullAlpha, close);
            }
            y = nextY;
            if (y >= (_stopY << 16) || (y >> 16) > target) break;
            InsertNewEdges(current, y, _skipIntersect);
        }
    }
    private void WalkConvexRow(int target, CancellationToken cancellationToken)
    {
        if (_head.Next is not AnalyticEdge leftEdge || leftEdge.Next is not AnalyticEdge rightEdge) return;
        AnalyticEdge? current = rightEdge.Next;
        int y = Math.Max(leftEdge.UpperY, rightEdge.UpperY);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _strips++;
            while (leftEdge.LowerY <= y)
            {
                if (current is null || FixedFloorToInt(current.UpperY) >= _stopY) return;
                leftEdge = current;
                current = current.Next;
            }
            while (rightEdge.LowerY <= y)
            {
                if (current is null || FixedFloorToInt(current.UpperY) >= _stopY) return;
                rightEdge = current;
                current = current.Next;
            }
            if (FixedFloorToInt(y) >= _stopY || FixedFloorToInt(y) > target) return;
            GoY(leftEdge, y);
            GoY(rightEdge, y);
            if (leftEdge.X > rightEdge.X || (leftEdge.X == rightEdge.X && leftEdge.DX > rightEdge.DX)) (leftEdge, rightEdge) = (rightEdge, leftEdge);
            int localBot = Math.Min(leftEdge.LowerY, rightEdge.LowerY);
            if (IsSmoothEnough(leftEdge, rightEdge, current, _stopY)) localBot = FixedCeilToFixed(localBot);
            localBot = Math.Min(localBot, _stopY << 16);
            int left = Math.Max(_leftClip, leftEdge.X);
            int dLeft = leftEdge.DX;
            int rite = Math.Min(_rightClip, rightEdge.X);
            int dRite = rightEdge.DX;
            if ((dLeft | dRite) == 0)
            {
                RectFlow(left, rite, y, localBot, target);
                y = localBot;
            }
            else
            {
                (left, rite, y) = SlopeFlow(left, rite, dLeft, dRite, leftEdge.DY, rightEdge.DY, y, localBot, target, cancellationToken);
            }
            leftEdge.X = left;
            rightEdge.X = rite;
            leftEdge.Y = rightEdge.Y = y;
        }
    }
    private void RectFlow(int left, int rite, int y, int localBot, int target)
    {
        int fullLeft = FixedCeilToInt(left);
        int fullRite = FixedFloorToInt(rite);
        int partialLeft = (fullLeft << 16) - left;
        int partialRite = rite - (fullRite << 16);
        int fullTop = FixedCeilToInt(y);
        int fullBot = FixedFloorToInt(localBot);
        int partialTop = (fullTop << 16) - y;
        int partialBot = localBot - (fullBot << 16);
        if (fullTop > fullBot) { partialTop -= Fixed1 - partialBot; partialBot = 0; }
        if (fullRite >= fullLeft)
        {
            if (partialTop > 0 && fullTop - 1 == target)
            {
                if (partialLeft > 0) AddMask(fullLeft - 1 - _xStart, FixedToAlphaTrunc(FixedMul(partialTop, partialLeft)));
                BlitFullAlpha(fullLeft - _xStart, fullRite - fullLeft, FixedToAlphaTrunc(partialTop));
                if (partialRite > 0) AddMask(fullRite - _xStart, FixedToAlphaTrunc(FixedMul(partialTop, partialRite)));
            }
            if (fullBot > fullTop && (fullRite > fullLeft || FixedToAlphaTrunc(partialLeft) > 0 || FixedToAlphaTrunc(partialRite) > 0) && target >= fullTop && target < fullBot)
            {
                if (partialLeft > 0) SetMask(fullLeft - 1 - _xStart, FixedToAlphaTrunc(partialLeft));
                for (int index = 0; index < fullRite - fullLeft; index++) SetMask(fullLeft + index - _xStart, 255);
                if (partialRite > 0) SetMask(fullRite - _xStart, FixedToAlphaTrunc(partialRite));
            }
            if (partialBot > 0 && fullBot == target)
            {
                if (partialLeft > 0) AddMask(fullLeft - 1 - _xStart, FixedToAlphaTrunc(FixedMul(partialBot, partialLeft)));
                BlitFullAlpha(fullLeft - _xStart, fullRite - fullLeft, FixedToAlphaTrunc(partialBot));
                if (partialRite > 0) AddMask(fullRite - _xStart, FixedToAlphaTrunc(FixedMul(partialBot, partialRite)));
            }
        }
        else
        {
            int width = rite - left;
            if (width > 0)
            {
                if (partialTop > 0 && fullTop - 1 == target) AddMask(fullLeft - 1 - _xStart, FixedToAlphaTrunc(FixedMul(partialTop, width)));
                if (fullBot > fullTop && target >= fullTop && target < fullBot) SetMask(fullLeft - 1 - _xStart, FixedToAlphaTrunc(width));
                if (partialBot > 0 && fullBot == target) AddMask(fullLeft - 1 - _xStart, FixedToAlphaTrunc(FixedMul(partialBot, width)));
            }
        }
    }
    private (int Left, int Rite, int Y) SlopeFlow(int left, int rite, int deltaLeft, int deltaRite, int leftDY, int rightDY, int y, int localBot, int target, CancellationToken cancellationToken)
    {
        left += SnapHalf;
        rite += SnapHalf;
        int count = FixedCeilToInt(localBot) - FixedFloorToInt(y);
        if (count > 1)
        {
            if ((y & unchecked((int)0xFFFF0000)) != y)
            {
                count--;
                int nextY = FixedCeilToFixed(y + 1);
                int spanY = nextY - y;
                int nextLeft = left + FixedMul(deltaLeft, spanY);
                int nextRite = rite + FixedMul(deltaRite, spanY);
                if ((y >> 16) == target) BlitTrapezoidRow(left & SnapMask, rite & SnapMask, nextLeft & SnapMask, nextRite & SnapMask, leftDY, rightDY, PartialAlpha(255, spanY), false);
                left = nextLeft;
                rite = nextRite;
                y = nextY;
            }
            int guard = 0;
            while (count > 1)
            {
                if ((guard++ & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                count--;
                int nextY = y + Fixed1;
                int nextLeft = left + deltaLeft;
                int nextRite = rite + deltaRite;
                if ((y >> 16) == target) BlitTrapezoidRow(left & SnapMask, rite & SnapMask, nextLeft & SnapMask, nextRite & SnapMask, leftDY, rightDY, 255, false);
                else if ((y >> 16) > target) return (left, rite, y);
                left = nextLeft;
                rite = nextRite;
                y = nextY;
                _strips++;
            }
        }
        int tailY = localBot - y;
        int tailLeft = Math.Max(left + FixedMul(deltaLeft, tailY), _leftClip + SnapHalf);
        int tailRite = Math.Min(rite + FixedMul(deltaRite, tailY), _rightClip + SnapHalf);
        if ((y >> 16) == target) BlitTrapezoidRow(left & SnapMask, rite & SnapMask, tailLeft & SnapMask, tailRite & SnapMask, leftDY, rightDY, PartialAlpha(255, tailY), false);
        left = tailLeft;
        rite = tailRite;
        y = localBot;
        left -= SnapHalf;
        rite -= SnapHalf;
        return (left, rite, y);
    }
    /// <summary>Walks the retained edges from the top and emits the requested row coverage, charging measured work.</summary>
    /// <remarks>Rows must arrive in ascending order from the preparation x-y window; each call rebuilds links and edge positions from setup, so calls are independent apart from ascending order. The row span holds mask alphas for the window width after the call. Row work covers zeroing, link and position rebuilds, visited strips and a sorting-free walk for the caller ledger.</remarks>
    internal void PrepareRow(int y, out long rowWork, CancellationToken cancellationToken)
    {
        Array.Clear(_row, 0, _row.Length);
        _strips = 0;
        if (_edges.Length == 0) { rowWork = _width; return; }
        for (int index = 0; index < _edges.Length; index++)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (_edges[index].InitialCurveCount != 0)
            {
                _edges[index].Winding = _edges[index].InitialWinding;
                _edges[index].CurveCount = _edges[index].InitialCurveCount;
                _edges[index].SnappedY = _edges[index].InitialSnappedY;
                _edges[index].FCx = _edges[index].InitialFCx;
                _edges[index].FCy = _edges[index].InitialFCy;
                _edges[index].FCDx = _edges[index].InitialFCDx;
                _edges[index].FCDy = _edges[index].InitialFCDy;
                _edges[index].FCDDx = _edges[index].InitialFCDDx;
                _edges[index].FCDDy = _edges[index].InitialFCDDy;
                _edges[index].FCDDDx = _edges[index].InitialFCDDDx;
                _edges[index].FCDDDy = _edges[index].InitialFCDDDy;
                _edges[index].QSnappedX = _edges[index].InitialQSnappedX;
                if (!AdvanceCubicLive(_edges[index], ref _strips)) _edges[index].LowerY = _edges[index].UpperY;
            }
            else
            {
                _edges[index].X = _edges[index].UpperX;
                _edges[index].Y = _edges[index].UpperY;
            }
        }
        _head.Next = _edges[0];
        _edges[0].Prev = _head;
        for (int index = 1; index < _edges.Length; index++)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            _edges[index - 1].Next = _edges[index];
            _edges[index].Prev = _edges[index - 1];
        }
        _edges[^1].Next = _tail;
        _tail.Prev = _edges[^1];
        if (_convex) WalkConvexRow(y, cancellationToken);
        else WalkEdgesRow(y, cancellationToken);

        rowWork = _width + (long)_edges.Length * 3 + _strips;
    }
}
