using System.Drawing;
using System.Drawing.Imaging;
using AdvancedInstallerMcp.Dialogs;
using AdvancedInstallerMcp.Lint;
using AdvancedInstallerMcp.Msi;

namespace AdvancedInstallerMcp.Mock;

// Turns a validated MockSpec into the rows of a throw-away package (ADR 0008). Wizard pages get their own copy of
// Advanced Installer's frame, with the page's banner texts and the body's controls tabbed in before the buttons.
public static class MockPackageBuilder
{
    private const int DefaultAttributes = ControlAttributes.Visible | ControlAttributes.Enabled;
    private const int RadioButtonHeight = 15;

    public static PackageRows Build(MockSpec spec)
    {
        var frame = WizardFrame.Load();
        var rows = new PackageRows();

        var properties = new Dictionary<string, string>(frame.Properties, StringComparer.Ordinal);
        foreach (var (name, value) in spec.Properties ?? [])
        {
            properties[name] = value;
        }

        rows.Properties.AddRange(properties.Select(p => (p.Key, p.Value)));

        var styles = frame.TextStyles.ToDictionary(s => s.Name, StringComparer.Ordinal);
        foreach (var style in spec.TextStyles ?? [])
        {
            styles[style.Name] = new TextStyleRow(style.Name, style.FaceName, style.Size, style.Color, style.StyleBits);
        }

        rows.TextStyles.AddRange(styles.Values);

        if (spec.Dialogs.Any(IsWizard))
        {
            var bitmap = frame.Controls.Single(c => c.Name == frame.BannerBitmap);
            rows.Binaries.Add((frame.BannerBinary, PlainBitmap(bitmap.Width, bitmap.Height)));
        }

        foreach (var dialog in spec.Dialogs)
        {
            AddDialog(rows, frame, dialog);
        }

        return rows;
    }

    // A null Frame (absent in the JSON) means the default, "wizard".
    private static bool IsWizard(MockDialogSpec dialog) => dialog.Frame is null or "wizard";

    private static void AddDialog(PackageRows rows, WizardFrame frame, MockDialogSpec dialog)
    {
        var wizard = IsWizard(dialog);
        var body = dialog.Controls
            .Select(c => new ControlRow(dialog.Name, c.Name, c.Type, c.X, c.Y, c.Width, c.Height,
                c.Attributes ?? DefaultAttributes, c.Property, c.Text, c.Next))
            .ToList();
        var frameRows = wizard
            ? frame.Controls.Select(c => new ControlRow(dialog.Name, c.Name, c.Type, c.X, c.Y, c.Width, c.Height,
                c.Attributes, c.Property, BannerText(frame, c, dialog), c.Next)).ToList()
            : new List<ControlRow>();

        var focusable = body.Where(c => c.Next is null && LayoutRules.FocusableTypes.Contains(c.Type)).ToList();
        var first = focusable.FirstOrDefault()?.Name ?? (wizard ? frame.FirstControl : body.FirstOrDefault()?.Name ?? "");
        Chain(focusable, frameRows, wizard ? frame.FirstControl : null, body);

        rows.Dialogs.Add(new DialogRow(dialog.Name, dialog.Width ?? frame.Width, dialog.Height ?? frame.Height,
            DefaultAttributes, dialog.Title ?? frame.Title, first,
            wizard ? frame.DefaultControl : null, wizard ? frame.CancelControl : null));
        rows.Controls.AddRange(body);
        rows.Controls.AddRange(frameRows);

        foreach (var condition in dialog.Conditions ?? [])
        {
            rows.ControlConditions.Add(new ConditionRow(dialog.Name, condition.Control, condition.Action, condition.Condition));
        }

        foreach (var control in dialog.Controls.Where(c => c.Items is { Count: > 0 }))
        {
            AddItems(rows, control);
        }
    }

    // Body controls tab in spec order, right after the frame's first control (Next), as Advanced Installer chains
    // its pages: Next -> body... -> whatever followed Next. Without a frame they form their own cycle.
    private static void Chain(List<ControlRow> focusable, List<ControlRow> frameRows, string? frameFirst, List<ControlRow> body)
    {
        if (focusable.Count == 0)
        {
            return;
        }

        var first = frameFirst is null ? -1 : frameRows.FindIndex(c => c.Name == frameFirst);
        var after = first >= 0 ? frameRows[first].Next : null;

        for (var i = 0; i < focusable.Count; i++)
        {
            var next = i + 1 < focusable.Count ? focusable[i + 1].Name : after ?? focusable[0].Name;
            body[body.IndexOf(focusable[i])] = focusable[i] with { Next = next };
        }

        if (first >= 0)
        {
            frameRows[first] = frameRows[first] with { Next = focusable[0].Name };
        }
    }

    private static string? BannerText(WizardFrame frame, FrameControl control, MockDialogSpec dialog) =>
        control.Name == frame.BannerTitle ? control.Text?.Replace("[MockBannerTitle]", dialog.BannerTitle ?? "")
        : control.Name == frame.BannerText ? control.Text?.Replace("[MockBannerText]", dialog.BannerText ?? "")
        : control.Text;

    private static void AddItems(PackageRows rows, MockControl control)
    {
        var items = control.Items!;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            switch (control.Type)
            {
                case "RadioButtonGroup":
                    rows.RadioButtons.Add(new RadioButtonRow(control.Property!, i + 1, item.Value,
                        item.X ?? 0, item.Y ?? RadioButtonHeight * i, item.Width ?? control.Width, item.Height ?? RadioButtonHeight,
                        item.Text));
                    break;
                case "ComboBox":
                    rows.ComboBoxItems.Add(new ListItemRow(control.Property!, i + 1, item.Value, item.Text));
                    break;
                case "ListBox":
                    rows.ListBoxItems.Add(new ListItemRow(control.Property!, i + 1, item.Value, item.Text));
                    break;
            }
        }
    }

    // Stands in for the banner picture, which is Advanced Installer's and not ours to ship.
    private static byte[] PlainBitmap(int widthUnits, int heightUnits)
    {
        using var bitmap = new Bitmap(
            Math.Max(1, InstallerUnits.ToPixels(widthUnits, 96)), Math.Max(1, InstallerUnits.ToPixels(heightUnits, 96)),
            PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Bmp);
        return stream.ToArray();
    }
}
