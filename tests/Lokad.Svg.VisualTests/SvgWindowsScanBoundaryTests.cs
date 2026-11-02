namespace Lokad.Svg.VisualTests;

public sealed class SvgWindowsScanBoundaryTests
{
    [Fact]
    [Trait("Layer", "Rendering")]
    public Task The_raster_strike_selects_tiny_program_execution_before_component_fitting()
        => NativeFontFixture.AssertMatches("windows-small-strikes", "original-small-strikes", "ProbeTiny");

    [Fact]
    [Trait("Layer", "Rendering")]
    public Task A_turning_counter_uses_prepared_scan_curves_without_changing_the_fitted_outline()
        => NativeFontFixture.AssertMatches("windows-scan-boundary", "original-scan-turn", "ScanTurn");

    [Fact]
    [Trait("Layer", "Rendering")]
    public Task Conditional_pixel_delta_functions_preserve_native_recognition_and_ordinary_shift_controls()
        => NativeFontFixture.AssertMatches("windows-conditional-shift", "original-conditional-shift", "ConditionalShift");

    [Fact]
    [Trait("Layer", "Rendering")]
    public Task Conditional_pixel_deltas_keep_touched_vertical_and_composite_targets()
        => NativeFontFixture.AssertMatches("windows-conditional-targets", "original-conditional-targets", "ConditionalTouch");

    [Fact]
    [Trait("Layer", "Rendering")]
    public Task Stack_projection_keeps_supplied_components_and_the_independent_freedom_direction()
        => NativeFontFixture.AssertMatches("windows-stack-vectors", "original-stack-vectors", "StackVectors");

    [Fact]
    [Trait("Layer", "Rendering")]
    public Task Windows_movement_keeps_copied_and_independently_loaded_directions_distinct()
        => NativeFontFixture.AssertMatches("windows-movement", "original-windows-movement", "WindowsMovement");

}
