using System.Net;
using System.Text;
using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Lint;

namespace AdvancedInstallerMcp.Mock;

// The page the user approves before the project changes (ADR 0008): every proposed page, its inputs and how each is
// set without the UI. Generated from the spec, so its format never varies.
public static class ApprovalPage
{
    private const string Style =
        "body{font:14px/1.45 'Segoe UI',system-ui,sans-serif;margin:0 auto;max-width:880px;padding:24px 16px;color:#1f1f1f;background:#fafafa}" +
        "section{background:#fff;border:1px solid #ddd;border-radius:6px;padding:16px;margin:0 0 24px}" +
        "img{display:block;max-width:100%;border:1px solid #ccc;margin:8px 0}" +
        "table{border-collapse:collapse;width:100%;margin:8px 0}th,td{border:1px solid #ddd;padding:4px 8px;text-align:left;vertical-align:top}" +
        "code,pre{font:13px Consolas,monospace}pre{white-space:pre-wrap;background:#f3f3f3;padding:8px;border-radius:4px}" +
        ".Error{color:#b00020}.Warning{color:#8a5a00}.Info{color:#555}";

    public static string Html(MockSpec spec, IReadOnlyList<Finding> findings)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>Setup pages for review</title><style>").Append(Style).Append("</style></head><body>")
            .Append("<h1>Setup pages for review</h1>");

        foreach (var dialog in spec.Dialogs)
        {
            html.Append("<section><h2>").Append(Encode(dialog.Name)).Append("</h2>")
                .Append("<img src=\"").Append(Encode(dialog.Name)).Append(".png\" alt=\"").Append(Encode(dialog.Name)).Append("\">");
            if (dialog.Description is { } description)
            {
                html.Append("<p>").Append(Encode(description)).Append("</p>");
            }

            var inputs = dialog.Controls.Where(IsInput).ToList();
            if (inputs.Count > 0)
            {
                html.Append("<table><tr><th>Control</th><th>Property</th><th>Default</th><th>Why this control</th><th>Command line</th></tr>");
                foreach (var control in inputs)
                {
                    var property = control.Property!;
                    html.Append("<tr><td>").Append(Encode(control.Type)).Append("</td><td><code>").Append(Encode(property))
                        .Append("</code></td><td>").Append(Encode(Default(spec, property) ?? "(empty)"))
                        .Append("</td><td>").Append(Encode(control.Rationale ?? ""))
                        .Append("</td><td>").Append(string.Join("<br>", Forms(control).Select(Encode))).Append("</td></tr>");
                }

                html.Append("</table>");
            }

            var own = findings.Where(f => f.Dialog == dialog.Name).ToList();
            if (own.Count > 0)
            {
                html.Append("<ul>");
                foreach (var finding in own)
                {
                    html.Append("<li class=\"").Append(finding.Severity).Append("\"><code>").Append(Encode(finding.Rule)).Append("</code> ")
                        .Append(Encode(finding.Control is null ? "" : finding.Control + ": ")).Append(Encode(finding.Message)).Append("</li>");
                }

                html.Append("</ul>");
            }

            html.Append("</section>");
        }

        html.Append("<section><h2>Unattended install</h2><pre>").Append(Encode(CommandLine(spec))).Append("</pre></section>")
            .Append("</body></html>");
        return html.ToString();
    }

    public static string CommandLine(MockSpec spec)
    {
        var parts = new List<string> { "msiexec /i Setup.msi /qn /l*v install.log" };
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var control in spec.Dialogs.SelectMany(d => d.Controls).Where(IsInput))
        {
            var property = control.Property!;
            if (!seen.Add(property))
            {
                continue;
            }

            var value = Default(spec, property)
                ?? control.Items?.FirstOrDefault()?.Value
                ?? (control.Type == "CheckBox" ? "1" : null);
            parts.Add(value is null ? $"{property}=\"<value>\"" : $"{property}={Quote(value)}");
        }

        return string.Join(" ", parts);
    }

    private static bool IsInput(MockControl control) =>
        control.Property is not null && MockSpecValidator.InputTypes.Contains(control.Type);

    private static IEnumerable<string> Forms(MockControl control)
    {
        var property = control.Property!;
        if (control.Items is { Count: > 0 } items)
        {
            return items.Select(i => $"{property}={Quote(i.Value)}");
        }

        return control.Type switch
        {
            "CheckBox" => [$"{property}=1 (checked)", "omit for unchecked"],
            "Edit" when ((control.Attributes ?? 3) & ControlAttributes.Password) != 0 =>
                [$"{property}=\"…\"", "hidden in logs only when listed in MsiHiddenProperties"],
            _ => [$"{property}=\"…\""],
        };
    }

    private static string? Default(MockSpec spec, string property) => spec.Properties?.GetValueOrDefault(property);

    private static string Quote(string value) => value.Any(c => char.IsWhiteSpace(c) || c == '"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
