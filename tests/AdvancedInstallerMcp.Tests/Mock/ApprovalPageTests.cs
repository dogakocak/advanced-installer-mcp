using AdvancedInstallerMcp.Lint;
using AdvancedInstallerMcp.Mock;

namespace AdvancedInstallerMcp.Tests.Mock;

public sealed class ApprovalPageTests
{
    private static readonly MockSpec Spec = new(
        new Dictionary<string, string> { ["CERT_MODE"] = "pfx" },
        null,
        [new MockDialogSpec("CertificateDlg",
            [
                new MockControl("Mode", "RadioButtonGroup", 20, 60, 300, 30, Property: "CERT_MODE", Rationale: "Two fixed choices.",
                    Items: [new MockItem("pfx", "&PFX file"), new MockItem("store", "Certificate &store")]),
                new MockControl("Pwd", "Edit", 20, 100, 200, 18, Attributes: 3 | 0x00200000, Property: "CERT_PASSWORD"),
                new MockControl("Trust", "CheckBox", 20, 130, 200, 15, Property: "TRUST_CERT", Text: "Trust <it>"),
                new MockControl("Hint", "Text", 20, 150, 200, 15, Text: "Not an input"),
            ],
            Description: "Where the service gets its certificate.")]);

    [Fact]
    public void Lists_every_input_with_its_command_line_values()
    {
        var html = ApprovalPage.Html(Spec, []);

        html.ShouldContain("<img src=\"CertificateDlg.png\"");
        html.ShouldContain("Where the service gets its certificate.");
        html.ShouldContain("CERT_MODE=pfx");
        html.ShouldContain("CERT_MODE=store");
        html.ShouldContain("Two fixed choices.");
        html.ShouldContain("TRUST_CERT=1");
        html.ShouldContain("MsiHiddenProperties");
        html.ShouldNotContain("<td>Text</td>"); // the Hint control is not an input, so it has no row
    }

    [Fact]
    public void Encodes_text()
    {
        ApprovalPage.Html(Spec with { Dialogs = [Spec.Dialogs[0] with { Description = "a <b> & c" }] }, [])
            .ShouldContain("a &lt;b&gt; &amp; c");
    }

    [Fact]
    public void Shows_the_findings_of_each_page()
    {
        var html = ApprovalPage.Html(Spec, [new Finding("choice-without-default", Severity.Warning, "No default.", "CertificateDlg", "Mode")]);

        html.ShouldContain("choice-without-default");
        html.ShouldContain("No default.");
    }

    [Fact]
    public void The_unattended_line_has_every_input_once()
    {
        ApprovalPage.CommandLine(Spec)
            .ShouldBe("msiexec /i Setup.msi /qn /l*v install.log CERT_MODE=pfx CERT_PASSWORD=\"<value>\" TRUST_CERT=1");
    }

    [Fact]
    public void Package_level_findings_get_their_own_section_before_the_pages()
    {
        var html = ApprovalPage.Html(Spec, [new Finding("font-missing", Severity.Warning, "Tahoma is not installed.")]);

        html.ShouldContain("Tahoma is not installed.");
        html.IndexOf("Tahoma is not installed.", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("<h2>CertificateDlg</h2>", StringComparison.Ordinal));
    }
}
