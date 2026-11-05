using System.Globalization;
using System.Text.Json;

namespace Lokad.Svg.VisualTests;

public sealed partial class SvgShaderOriginalReferenceTests
{
    private static byte[] CreateSource(JsonElement row)
    {
        string group = Required(row, "group"), role = Required(row, "role");
        if (role is not ("candidate" or "control")) throw new InvalidDataException(role);
        JsonElement recipe = row.GetProperty("recipe");
        if (group == "r47-expanded-consumers")
            return File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "shader-originals", Required(recipe, "input")));
        string body = group switch
        {
            "r44-consumers" => GradientConsumer(recipe, role),
            "r44-focal" => Focal(recipe, role),
            "color-alpha" => Color(recipe, role),
            "percentage-color-alpha" => Percentage(recipe, role),
            "percentage-alpha-consumers" => AlphaConsumer(recipe, role),
            _ => throw new InvalidDataException(group)
        };
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'"
            + (group.StartsWith("r44-", StringComparison.Ordinal) ? "" : " ") + ">" + body + "</svg>");

        static string GradientConsumer(JsonElement recipe, string role)
        {
            string kind = Required(recipe, "kind"), units = Required(recipe, "units"), mapping = Required(recipe, "mapping");
            string consumer = Required(recipe, "consumer"), spread = Required(recipe, "spread");
            bool user = units == "userSpaceOnUse", replacement = recipe.GetProperty("replacement").GetBoolean();
            string coords = kind == "linearGradient"
                ? user ? "x1='4' y1='4' x2='20' y2='12'" : "x1='.1' y1='.1' x2='.8' y2='.3'"
                : user ? "cx='16' cy='16' r='10' fx='14' fy='15' fr='1'" : "cx='.5' cy='.5' r='.45' fx='.4' fy='.45' fr='.05'";
            string transform = mapping == "shear"
                ? user ? "matrix(1 .2 .3 1 2 0)" : "matrix(1 .2 .3 1 .1 0)"
                : user ? "matrix(-1 0 0 1 32 0)" : "matrix(-1 0 0 1 1 0)";
            string own = user ? "translate(4 0)" : "translate(.2 0)";
            const string stops = "<stop stop-color='red'/><stop offset='1' stop-color='blue'/>";
            const string ownStops = "<stop stop-color='green'/><stop offset='1' stop-color='blue'/>";
            bool overrides = consumer is "text" or "mask" or "marker";
            string paint = role == "candidate"
                ? $"<{kind} id='g' href='#middle'" + (replacement ? $" gradientTransform='{own}'" : "") + ">" + (overrides ? ownStops : "") + $"</{kind}>"
                    + $"<{kind} id='middle' href='#base'/><{kind} id='base' gradientUnits='{units}' gradientTransform='{transform}' spreadMethod='{spread}' {coords}>{stops}</{kind}>"
                : $"<{kind} id='g' gradientUnits='{units}' gradientTransform='{(replacement ? own : transform)}' spreadMethod='{spread}' {coords}>" + (overrides ? ownStops : stops) + $"</{kind}>";
            string node = consumer switch
            {
                "curve" => "<path d='M4 16Q4 4 16 4Q28 4 28 16Q28 28 16 28Q4 28 4 16Z' fill='url(#g)'/>",
                "stroke" => "<path d='M4 16Q4 4 16 4Q28 4 28 16Q28 28 16 28Q4 28 4 16Z' fill='none' stroke='url(#g)' stroke-width='3'/>",
                "text" => "<text x='4' y='24' font-family='serif' font-size='20' fill='url(#g)'>AV</text>",
                "textstroke" => "<text x='4' y='24' font-family='serif' font-size='20' fill='none' stroke='url(#g)' stroke-width='1'>AV</text>",
                "mask" => "<defs><mask id='m'><rect x='4' y='4' width='24' height='24' fill='url(#g)'/></mask></defs><rect x='4' y='4' width='24' height='24' fill='red' mask='url(#m)'/>",
                "marker" => "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='32' markerHeight='32' refX='0' refY='0'><rect x='4' y='4' width='24' height='24' fill='url(#g)'/></marker></defs><path d='M0 0H1' fill='none' marker-start='url(#m)'/>",
                _ => throw new InvalidDataException(consumer)
            };
            return "<defs>" + paint + "</defs>" + node;
        }

        static string Focal(JsonElement recipe, string role)
        {
            bool explicitFocus = recipe.GetProperty("explicit").GetBoolean(), full = recipe.GetProperty("full").GetBoolean(), own = recipe.GetProperty("ownmap").GetBoolean();
            const string stops = "<stop stop-color='red'/><stop offset='1' stop-color='blue'/>";
            string focus = explicitFocus ? " fx='.35' fy='.45' fr='.05'" : "";
            string child = full ? "cx='.55' cy='.5' r='.45'" : "";
            string paint = role == "candidate"
                ? $"<radialGradient id='g' href='#base' {child}" + (own ? " gradientTransform='translate(.1 0)'" : "") + "/>"
                    + "<radialGradient id='base' cx='.4' cy='.5' r='.45' gradientTransform='matrix(1 .2 .3 1 0 0)'" + focus + ">" + stops + "</radialGradient>"
                : "<radialGradient id='g' " + (full ? child : "cx='.4' cy='.5' r='.45'") + focus + " gradientTransform='"
                    + (own ? "translate(.1 0)" : "matrix(1 .2 .3 1 0 0)") + "'>" + stops + "</radialGradient>";
            return "<defs>" + paint + "</defs><rect x='4' y='4' width='24' height='24' fill='url(#g)'/>";
        }

        static string Color(JsonElement recipe, string role)
        {
            string color = Required(recipe, "color"), paintRole = Required(recipe, "role"), opacity = Required(recipe, "opacity"), transform = Required(recipe, "transform");
            string control = color switch
            {
                "hsl(30 100% 50%)" => "#ff8000", "rgb(50% 0% 0%)" => "#800000",
                "rgba(255,0,0,.5)" or "rgb(255 0 0 / 50%)" => "#ff000080", "hsla(240,100%,50%,.5)" => "#0000ff80",
                _ => throw new InvalidDataException(color)
            };
            if (role == "control") color = control;
            return paintRole == "stroke"
                ? $"<path d='M4 4H24V24H4Z' fill='none' stroke='{color}' stroke-width='3' {opacity} transform='{transform}'/>"
                : $"<path d='M4 4H24V24H4Z' fill='{color}' {opacity} transform='{transform}'/>";
        }

        static string Percentage(JsonElement recipe, string role)
        {
            int alpha = recipe.GetProperty("alpha").GetInt32();
            string paintRole = Required(recipe, "role"), opacity = recipe.GetProperty("opacity").GetRawText(), location = Required(recipe, "location");
            string color = role == "control" ? "rgba(255,0,0," + (alpha / 100.0).ToString("G", CultureInfo.InvariantCulture) + ")"
                : Required(recipe, "syntax") == "rgb" ? $"rgb(255 0 0 / {alpha}%)" : $"hsl(0 100% 50% / {alpha}%)";
            string shape = $"<path id='q' d='M4 4H24V24H4Z' fill='{(paintRole == "fill" ? color : "none")}' stroke='{(paintRole == "stroke" ? color : "none")}' stroke-width='3' {paintRole}-opacity='{opacity}'/>";
            return location switch
            {
                "direct" => shape,
                "use" => "<defs>" + shape + "</defs><use href='#q'/>",
                "symbol" => "<defs><symbol id='s' viewBox='0 0 32 32'>" + shape + "</symbol></defs><use href='#s' width='32' height='32'/>",
                "pattern" => "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + shape + "</pattern></defs><rect width='32' height='32' fill='url(#p)'/>",
                _ => throw new InvalidDataException(location)
            };
        }

        static string AlphaConsumer(JsonElement recipe, string role)
        {
            string family = Required(recipe, "family"), opacity = recipe.GetProperty("opacity").GetRawText(), transform = Required(recipe, "transform");
            string color = role == "control" ? "rgba(255,0,0,.5)"
                : Required(recipe, "mode") == "rgb" ? "rgb(255 0 0 / 50%)" : "hsl(0 100% 50% / 50%)";
            const string path = "<path d='M4 4H24V24H4Z'";
            if (family is "gradient" or "stop")
            {
                string stopOpacity = family == "stop" ? opacity : "1", fillOpacity = family == "gradient" ? opacity : "1";
                return $"<defs><linearGradient id='g'><stop stop-color='{color}' stop-opacity='{stopOpacity}'/><stop offset='1' stop-color='{color}' stop-opacity='{stopOpacity}'/></linearGradient></defs>"
                    + path + $" fill='url(#g)' fill-opacity='{fillOpacity}' transform='{transform}'/>";
            }
            return family switch
            {
                "solid" => path + $" fill='{color}' fill-opacity='{opacity}' transform='{transform}'/>",
                "flood" => $"<defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32' color-interpolation-filters='sRGB'><feFlood flood-color='{color}' flood-opacity='{opacity}'/></filter></defs>" + path + $" fill='blue' filter='url(#f)' transform='{transform}'/>",
                "mask" => $"<defs><mask id='m' mask-type='alpha'><rect width='32' height='32' fill='{color}' fill-opacity='{opacity}'/></mask></defs>" + path + $" fill='red' mask='url(#m)' transform='{transform}'/>",
                "marker" => $"<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='20' markerHeight='20'><rect width='20' height='20' fill='{color}' fill-opacity='{opacity}'/></marker></defs><path d='M4 4H24' fill='none' stroke='none' marker-start='url(#m)' transform='{transform}'/>",
                "text" => $"<text x='2' y='22' font-family='serif' font-size='12' fill='{color}' fill-opacity='{opacity}' transform='{transform}'>AV</text>",
                _ => throw new InvalidDataException(family)
            };
        }
    }
}
