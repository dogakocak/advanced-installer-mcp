using AdvancedInstallerMcp.Mock;

namespace AdvancedInstallerMcp.Tests.Mock;

public sealed class WizardFrameTests
{
    [Fact]
    public void The_captured_frame_has_its_roles_and_buttons()
    {
        var frame = WizardFrame.Load();
        var names = frame.Controls.Select(c => c.Name).ToHashSet();

        names.ShouldContain(frame.BannerTitle);
        names.ShouldContain(frame.BannerText);
        names.ShouldContain(frame.BannerBitmap);
        names.ShouldContain(frame.DefaultControl);
        names.ShouldContain(frame.CancelControl);
        frame.BannerBinary.ShouldNotBeNullOrEmpty();
        frame.Controls.Count(c => c.Type == "PushButton").ShouldBeGreaterThanOrEqualTo(3);
        frame.BodyTop.ShouldBeLessThan(frame.BodyBottom);
        frame.Properties.ShouldContainKey("DefaultUIFont");
        frame.TextStyles.ShouldContain(s => s.Name == frame.Properties["DefaultUIFont"]);
    }

    [Fact]
    public void The_frame_controls_form_a_closed_tab_cycle()
    {
        var frame = WizardFrame.Load();
        var next = frame.Controls.Where(c => c.Next is not null).ToDictionary(c => c.Name, c => c.Next!);

        var current = frame.FirstControl;
        var seen = new HashSet<string>();
        while (seen.Add(current))
        {
            next.ShouldContainKey(current);
            current = next[current];
        }

        current.ShouldBe(frame.FirstControl);
    }

    // The banner texts are the mock's to fill; everything else keeps Advanced Installer's text.
    [Fact]
    public void The_banner_texts_are_placeholders()
    {
        var frame = WizardFrame.Load();

        frame.Controls.Single(c => c.Name == frame.BannerTitle).Text.ShouldEndWith("[MockBannerTitle]");
        frame.Controls.Single(c => c.Name == frame.BannerText).Text.ShouldBe("[MockBannerText]");
    }
}
