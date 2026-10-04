namespace Lokad.Svg.Rendering;

/// <summary>Identifies the integer horizontal clip bounds used by the analytic edge walk.</summary>
/// <remarks>Coordinates are in device pixels within the output canvas. This window does not own storage and does not select the mask/run material route; path bounds retain those responsibilities.</remarks>
internal readonly record struct SvgAnalyticScanWindow(int Left, int Right);
