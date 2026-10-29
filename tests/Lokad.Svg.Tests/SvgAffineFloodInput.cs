namespace Lokad.Svg.Tests;

/// <summary>Shares exact source bytes between native flood regressions and restoration ownership contracts.</summary>
internal static class SvgAffineFloodInput
{
    internal static byte[] Create(string map, string region, string color, string opacity, string source, bool previous)
    {
        string node = source switch
        {
            "rect" => "<rect x='4' y='4' width='8' height='8' fill='red' filter='url(#f)'/>",
            "group" => "<g filter='url(#f)'><circle cx='8' cy='8' r='4' fill='red'/></g>",
            "use" => "<use href='#empty' filter='url(#f)'/>",
            "zero-symbol" => "<use href='#s' width='0' height='32' filter='url(#f)'/>",
            _ => throw new ArgumentException("Unknown source form.", nameof(source))
        };
        string graph = (previous ? "<feOffset dx='1'/>" : "") + "<feFlood flood-color='" + color + "' flood-opacity='" + opacity + "'/>";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><filter id='f' filterUnits='userSpaceOnUse' " + region + ">" + graph
            + "</filter><g id='empty'/><symbol id='s' viewBox='0 0 32 32'><rect width='32' height='32' fill='red'/></symbol></defs><g transform='" + map + "'>" + node + "</g></svg>");
    }
}
