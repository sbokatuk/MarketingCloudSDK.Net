using Xunit;

namespace MarketingCloudSDK.Net.UnitTests;

/// <summary>
/// The registration edit recorder: EditAsync runs the caller's delegate synchronously against a
/// validating recorder, so every bad argument fails on the caller's stack - as an
/// <see cref="ArgumentException"/>-family error, before anything is queued toward a platform.
/// On this neutral assembly the boundary is visible by type: validation failures are argument
/// errors, a validly recorded batch reaching the platform seam is the
/// <see cref="PlatformNotSupportedException"/>.
/// </summary>
public class RegistrationEditorTests
{
    private static IMarketingCloudRegistration Registration() => new MarketingCloudClient().Registration;

    [Fact]
    public async Task EditAsync_refuses_a_null_delegate()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Registration().EditAsync(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddTag_and_RemoveTag_refuse_blank_tags(string blank)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Registration().EditAsync(editor => editor.AddTag(blank)));
        await Assert.ThrowsAsync<ArgumentException>(
            () => Registration().EditAsync(editor => editor.RemoveTag(blank)));
    }

    [Fact]
    public async Task Tags_refuse_null()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Registration().EditAsync(editor => editor.AddTag(null!)));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Registration().EditAsync(editor => editor.RemoveTag(null!)));
    }

    [Fact]
    public async Task SetAttribute_refuses_a_blank_key_and_a_null_value()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Registration().EditAsync(editor => editor.SetAttribute(" ", "value")));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Registration().EditAsync(editor => editor.SetAttribute("tier", null!)));
    }

    [Fact]
    public async Task SetAttribute_accepts_an_empty_value()
    {
        // Empty is a legal attribute value (distinct from clearing); only null is refused. On
        // this neutral head acceptance shows up as the batch reaching the platform seam.
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => Registration().EditAsync(editor => editor.SetAttribute("tier", "")));
    }

    [Fact]
    public async Task A_delegate_exception_propagates_and_nothing_is_applied()
    {
        // The delegate is the caller's code; a throw from it must surface unchanged. That it
        // surfaces as this exact exception (not wrapped, not a platform error) is also the
        // proof nothing was applied: recording happens before the platform seam is touched.
        var thrown = new InvalidOperationException("caller's own failure");

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Registration().EditAsync(_ => throw thrown));

        Assert.Same(thrown, caught);
    }

    [Fact]
    public async Task An_empty_edit_still_reaches_the_platform_seam()
    {
        // Deliberate: an empty edit on a neutral head must still throw the documented
        // PlatformNotSupportedException rather than quietly succeed - a "success" that touched
        // no SDK would be indistinguishable from working push wiring in a test run.
        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => Registration().EditAsync(_ => { }));
    }

    [Fact]
    public async Task Validation_throws_synchronously_with_the_recording_not_from_the_returned_task()
    {
        // EditAsync is documented to validate on the caller's stack. Calling without awaiting
        // must already throw - the returned-task-only failure mode would let a fire-and-forget
        // caller drop the error.
        var registration = Registration();

        // The statement-bodied lambda is an Action, not a Func<Task>: the call must throw
        // before a task exists at all, which is precisely the behaviour under test.
        Assert.Throws<ArgumentException>(void () =>
        {
            _ = registration.EditAsync(editor => editor.AddTag(" "));
        });

        await Task.CompletedTask;
    }
}
