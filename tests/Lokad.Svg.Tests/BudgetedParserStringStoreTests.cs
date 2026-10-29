using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class BudgetedParserStringStoreTests
{
    [Fact]
    public void Existing_immutable_strings_are_retained_once_by_reference_identity()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(4_096));
        using BudgetedParserStringStore strings = new(budget);
        string value = new('a', 32), equal = new('a', 32);
        Assert.Same(value, strings.Copy(value));
        long first = budget.Snapshot().CurrentBytes;
        Assert.True(first > 0);
        Assert.Same(value, strings.Copy(value));
        Assert.Equal(first, budget.Snapshot().CurrentBytes);
        Assert.Same(equal, strings.Copy(equal));
        Assert.True(budget.Snapshot().CurrentBytes > first);
    }

    [Fact]
    public void Span_sources_are_copied_and_the_result_is_already_retained()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(4_096));
        using BudgetedParserStringStore strings = new(budget);
        char[] source = "from-buffer".ToCharArray();
        string copy = strings.Copy(source.AsSpan());
        source[0] = 'X';
        Assert.Equal("from-buffer", copy);
        long retained = budget.Snapshot().CurrentBytes;
        Assert.Same(copy, strings.Copy(copy));
        Assert.Equal(retained, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Retained_reader_values_survive_reader_disposal()
    {
        using BudgetedParserStringStore strings = new(new RenderBudget(new HostMemoryBudget(4_096)));
        string value;
        using (XmlReader reader = XmlReader.Create(new StringReader("<rect id='source-value'/>")))
        {
            Assert.True(reader.Read());
            string attribute = reader.GetAttribute("id") ?? throw new InvalidDataException();
            value = strings.Copy(attribute);
            Assert.Same(attribute, value);
            Assert.False(reader.Read());
        }
        Assert.Equal("source-value", value);
    }

    [Fact]
    public void Aggregate_growth_denial_preserves_existing_strings_and_disposal_releases_storage()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(512));
        BudgetedParserStringStore strings = new(budget);
        string value = strings.Copy("small");
        long retained = budget.Snapshot().CurrentBytes;
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => strings.Copy(new string('x', 8_192))).ErrorCode);
        Assert.Equal(retained, budget.Snapshot().CurrentBytes);
        Assert.Same(value, strings.Copy(value));
        strings.Dispose();
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        strings.Dispose();
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Growing_a_reservation_enforces_the_shared_bound_and_releases_the_entire_owner()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(300));
        RenderBudget.BudgetReservation owner = budget.Reserve(100, SvgMemoryCategory.Parser);
        owner.Grow(200);
        Assert.Equal(300, budget.Snapshot().CurrentBytes);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => owner.Grow(1)).ErrorCode);
        Assert.Equal(300, budget.Snapshot().CurrentBytes);
        owner.Dispose();
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Throws<ObjectDisposedException>(() => owner.Grow(1));
    }
}
