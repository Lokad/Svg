namespace Lokad.Svg.Parsing;

/// <summary>Counts selector specificity axes independently; any ID outranks classes, and any class outranks elements.</summary>
internal readonly record struct CssSpecificity(int Ids, int Classes, int Elements)
{
    public static CssSpecificity operator +(CssSpecificity left, CssSpecificity right)
        => new(checked(left.Ids + right.Ids), checked(left.Classes + right.Classes), checked(left.Elements + right.Elements));
}
