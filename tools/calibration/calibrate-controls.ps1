# Calibrates the CheckBox and PushButton margins of ADR 0003 against the real Windows Installer UI.
#
# Builds a throw-away MSI whose only dialog shows the same labels in check boxes and push buttons of increasing width,
# opens it with msiexec (the UI sequence shows the dialog; there is no execute sequence, so nothing is installed),
# captures the dialog from the screen and compares the "ink" inside every control with a wide reference control of
# the same text. The narrowest width with the full ink is the smallest control that still shows the whole label.
#
# Usage (Windows PowerShell 5.1, from any folder):  powershell -NoProfile -File tools\calibration\calibrate-controls.ps1
# Output: one line per label and control type with the text width (plain DrawText, as TextMeasurer) and the narrowest
# width that fits, plus the PNG next to the script's temp MSI for a visual check.

param(
    [string]$Face = 'Tahoma',
    [int]$Points = 8,
    [string]$OutDir = (Join-Path $env:TEMP 'advinst-mcp-calibration')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int count);
    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);

    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateFontW(int h, int w, int e, int o, int weight, uint i, uint u, uint s, uint cs, uint op, uint cp, uint q, uint pf, string face);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int DrawTextW(IntPtr dc, string text, int count, ref RECT rect, uint format);

    public static string Text(IntPtr hwnd) { var sb = new StringBuilder(512); GetWindowTextW(hwnd, sb, sb.Capacity); return sb.ToString(); }
    public static string Class(IntPtr hwnd) { var sb = new StringBuilder(256); GetClassNameW(hwnd, sb, sb.Capacity); return sb.ToString(); }

    public static List<IntPtr> Children(IntPtr parent)
    {
        var list = new List<IntPtr>();
        EnumChildWindows(parent, (h, l) => { list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }

    // Same call as TextMeasurer: plain DrawText, DT_CALCRECT | DT_SINGLELINE, mnemonic '&' hidden.
    public static int TextWidth(string text, string face, int points, int dpi)
    {
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        int em = (int)Math.Round(points * dpi / 72.0, MidpointRounding.AwayFromZero);
        SelectObject(dc, CreateFontW(-em, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, face));
        var r = new RECT { Right = 10000 };
        DrawTextW(dc, text, text.Length, ref r, 0x420);
        return r.Right - r.Left;
    }
}
"@

[void][Native]::SetProcessDPIAware()
New-Item -ItemType Directory -Force $OutDir | Out-Null
$msiPath = Join-Path $OutDir 'calibration.msi'
Remove-Item $msiPath -ErrorAction SilentlyContinue

$title = 'AIMCP Calibration'
$labels = @('Trust server certificate', 'Remember me', 'Create a desktop shortcut', 'I accept the terms')
$buttonLabels = @('&Next >', 'Cancel', 'Install now', '&Browse...')

# --- Build the MSI --------------------------------------------------------------------------------------------------
$installer = New-Object -ComObject WindowsInstaller.Installer
$db = $installer.OpenDatabase($msiPath, 3)

function Exec([string]$sql) { $v = $db.OpenView($sql); $v.Execute(); $v.Close() }
function Insert([string]$table, [string]$columns, [object[]]$values) {
    $rec = $installer.CreateRecord($values.Count)
    for ($i = 0; $i -lt $values.Count; $i++) {
        if ($values[$i] -is [int]) { $rec.IntegerData($i + 1) = $values[$i] }
        elseif ($null -ne $values[$i]) { $rec.StringData($i + 1) = [string]$values[$i] }
    }
    $marks = (@('?') * $values.Count) -join ', '
    $sql = "INSERT INTO ``$table`` ($columns) VALUES ($marks)"
    try { $v = $db.OpenView($sql) } catch { throw "Bad SQL: $sql" }
    $v.Execute($rec); $v.Close()
}

Exec 'CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` CHAR(0) NOT NULL LOCALIZABLE PRIMARY KEY `Property`)'
Exec 'CREATE TABLE `TextStyle` (`TextStyle` CHAR(72) NOT NULL, `FaceName` CHAR(32) NOT NULL, `Size` SHORT NOT NULL, `Color` LONG, `StyleBits` SHORT PRIMARY KEY `TextStyle`)'
Exec 'CREATE TABLE `Dialog` (`Dialog` CHAR(72) NOT NULL, `HCentering` SHORT NOT NULL, `VCentering` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Title` CHAR(128) LOCALIZABLE, `Control_First` CHAR(50) NOT NULL, `Control_Default` CHAR(50), `Control_Cancel` CHAR(50) PRIMARY KEY `Dialog`)'
Exec 'CREATE TABLE `Control` (`Dialog_` CHAR(72) NOT NULL, `Control` CHAR(50) NOT NULL, `Type` CHAR(20) NOT NULL, `X` SHORT NOT NULL, `Y` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Property` CHAR(72), `Text` CHAR(0) LOCALIZABLE, `Control_Next` CHAR(50), `Help` CHAR(50) LOCALIZABLE PRIMARY KEY `Dialog_`, `Control`)'
Exec 'CREATE TABLE `ControlEvent` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, `Event` CHAR(50) NOT NULL, `Argument` CHAR(255) NOT NULL, `Condition` CHAR(255), `Ordering` SHORT PRIMARY KEY `Dialog_`, `Control_`, `Event`, `Argument`, `Condition`)'
Exec 'CREATE TABLE `InstallUISequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)'

foreach ($p in @(
        @('ProductCode', ('{' + [guid]::NewGuid().ToString().ToUpper() + '}')), @('ProductName', 'AIMCP Calibration'),
        @('ProductVersion', '1.0.0'), @('ProductLanguage', '1033'), @('Manufacturer', 'advanced-installer-mcp'),
        @('DefaultUIFont', 'DlgFont'))) {
    Insert 'Property' '`Property`, `Value`' $p
}
Insert 'TextStyle' '`TextStyle`, `FaceName`, `Size`' @('DlgFont', $Face, $Points)
Insert 'TextStyle' '`TextStyle`, `FaceName`, `Size`, `StyleBits`' @('TitleFont', $Face, 9, 1)

# Columns of controls; every row is one width in installer units. The last row of each column is the wide reference.
$rows = @()
$x = 10
foreach ($label in $labels) {
    $px = [Native]::TextWidth($label, $Face, $Points, 96)
    $start = [int][Math]::Floor($px * 3 / 4)
    $widths = @($start..($start + 16)) + 200
    $rows += [pscustomobject]@{ Type = 'CheckBox'; Label = $label; TextPx = $px; X = $x; Widths = $widths }
    $x += 210
}
foreach ($label in $buttonLabels) {
    $px = [Native]::TextWidth($label, $Face, $Points, 96)
    $start = [int][Math]::Floor($px * 3 / 4) - 2
    $widths = @($start..($start + 16)) + 120
    $rows += [pscustomobject]@{ Type = 'PushButton'; Label = $label; TextPx = $px; X = $x; Widths = $widths }
    $x += 130
}

$dialogWidth = $x + 10
$dialogHeight = 18 * 14 + 40 + 80
Insert 'Dialog' '`Dialog`, `HCentering`, `VCentering`, `Width`, `Height`, `Attributes`, `Title`, `Control_First`, `Control_Cancel`' `
    @('CalDlg', 50, 50, $dialogWidth, $dialogHeight, 3, $title, 'Close', 'Close')
# Focus sits on Close, away from the measured controls, so no focus rectangle disturbs the ink.
Insert 'Control' '`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Text`' `
    @('CalDlg', 'Close', 'PushButton', 10, ($dialogHeight - 24), 56, 17, 3, 'Close')
Insert 'ControlEvent' '`Dialog_`, `Control_`, `Event`, `Argument`, `Condition`, `Ordering`' @('CalDlg', 'Close', 'EndDialog', 'Exit', '1', 1)

$controls = @()
$c = 0
foreach ($row in $rows) {
    $y = 10
    $height = if ($row.Type -eq 'CheckBox') { 12 } else { 17 }
    foreach ($w in $row.Widths) {
        $name = "C$c"; $c++
        $prop = if ($row.Type -eq 'CheckBox') { "P$name" } else { $null }
        Insert 'Control' '`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Property`, `Text`' `
            @('CalDlg', $name, $row.Type, $row.X, $y, $w, $height, 3, $prop, $row.Label)
        $controls += [pscustomobject]@{ Row = $row; Units = $w; X = $row.X; Y = $y; Height = $height }
        $y += 14
    }
}
# Text controls for a visual check of the wrap/height rule (not measured by this script): the ADR 0003 hint at 18
# and 20 units high (lint: 18 is cut, 20 fits) and a 9 pt bold title in a 200x15 control with a long product name
# (lint: two lines, cut).
$hint = 'Enter the server name and port of the database. The connection is tested first.'
$textY = 18 * 14 + 14
Insert 'Control' '`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Text`' @('CalDlg', 'Hint18', 'Text', 80, $textY, 230, 18, 3, $hint)
Insert 'Control' '`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Text`' @('CalDlg', 'Hint20', 'Text', 330, $textY, 230, 20, 3, $hint)
Insert 'Control' '`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Text`' @('CalDlg', 'Title', 'Text', 580, $textY, 200, 15, 3, '{\TitleFont}Ready to install Microsoft ASP.NET Diagnostic Pack for Visual Studio')
Insert 'Control' '`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Text`' @('CalDlg', 'TitleTall', 'Text', 800, $textY, 200, 22, 3, '{\TitleFont}Ready to install Microsoft ASP.NET Diagnostic Pack for Visual Studio')
Insert 'InstallUISequence' '`Action`, `Sequence`' @('CalDlg', 100)

$si = $db.SummaryInformation(10)
$si.Property(2) = $title           # Title
$si.Property(7) = 'Intel;1033'     # Template
$si.Property(9) = '{' + [guid]::NewGuid().ToString().ToUpper() + '}'  # Package code
$si.Property(14) = 200             # Minimum installer version
$si.Property(15) = 2               # Compressed=0, short names off
$si.Persist()
$db.Commit()
# Every COM object still alive keeps the file open and msiexec then fails to open the package.
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($si)
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($db)
$si = $null; $db = $null
[GC]::Collect(); [GC]::WaitForPendingFinalizers(); [GC]::Collect()

# --- Show it and capture ---------------------------------------------------------------------------------------------
$proc = Start-Process msiexec.exe -ArgumentList "/i `"$msiPath`"" -PassThru
try {
    $hwnd = [IntPtr]::Zero
    for ($i = 0; $i -lt 100 -and $hwnd -eq [IntPtr]::Zero; $i++) {
        Start-Sleep -Milliseconds 200
        $proc.Refresh()
        if ($proc.MainWindowTitle -eq $title) { $hwnd = $proc.MainWindowHandle }
    }
    if ($hwnd -eq [IntPtr]::Zero) { throw "Dialog '$title' did not appear." }
    # From the screen, not PrintWindow: PrintWindow returned some static and button controls unpainted.
    [void][Native]::SetForegroundWindow($hwnd)
    Start-Sleep -Seconds 3

    $wr = New-Object Native+RECT
    [void][Native]::GetWindowRect($hwnd, [ref]$wr)
    $bmp = New-Object System.Drawing.Bitmap(($wr.Right - $wr.Left), ($wr.Bottom - $wr.Top))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($wr.Left, $wr.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $png = Join-Path $OutDir 'calibration.png'
    $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)

    # Child windows carry the real pixel rectangles; match them to controls by position.
    $children = foreach ($h in [Native]::Children($hwnd)) {
        $r = New-Object Native+RECT
        [void][Native]::GetWindowRect($h, [ref]$r)
        [pscustomobject]@{ Text = [Native]::Text($h); Class = [Native]::Class($h); L = $r.Left - $wr.Left; T = $r.Top - $wr.Top; W = $r.Right - $r.Left; H = $r.Bottom - $r.Top }
    }
}
finally {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
}

# Text pixels of a control as a string mask cropped to their bounding box. Dark = text (and the check box frame);
# the dialog background and the light button border stay out. A wrapped or cut label gives a different mask than the
# wide reference; a centred label in a wider button gives the same mask once cropped.
function TextMask($rect, [int]$inset) {
    $lines = @()
    for ($yy = $rect.T + $inset; $yy -lt $rect.T + $rect.H - $inset; $yy++) {
        $line = New-Object System.Text.StringBuilder
        for ($xx = $rect.L + $inset; $xx -lt $rect.L + $rect.W - $inset; $xx++) {
            $p = $bmp.GetPixel($xx, $yy)
            [void]$line.Append($(if (($p.R + $p.G + $p.B) -lt 384) { '#' } else { '.' }))
        }
        $lines += $line.ToString()
    }
    $rowsWithInk = @(for ($k = 0; $k -lt $lines.Count; $k++) { if ($lines[$k].Contains('#')) { $k } })
    if ($rowsWithInk.Count -eq 0) { return [pscustomobject]@{ Mask = ''; Left = -1 } }
    $kept = $lines[$rowsWithInk[0]..$rowsWithInk[-1]]
    $left = ($kept | ForEach-Object { $_.IndexOf('#') } | Where-Object { $_ -ge 0 } | Measure-Object -Minimum).Minimum
    $right = ($kept | ForEach-Object { $_.LastIndexOf('#') } | Measure-Object -Maximum).Maximum
    [pscustomobject]@{ Mask = (($kept | ForEach-Object { $_.Substring($left, $right - $left + 1) }) -join "`n"); Left = $left + $inset; Lines = $lines }
}

$buttons = @($children | Where-Object { $_.Class -eq 'Button' -and $_.Text -ne 'Close' })
""
'Type        Label                        TextPx  MinFitUnits  MinFitPx  Overhead(px)  TextLeft(px)'

foreach ($row in $rows) {
    $own = @($buttons | Where-Object { $_.Text -eq $row.Label } | Sort-Object T)
    if ($own.Count -ne $row.Widths.Count) { throw "Expected $($row.Widths.Count) windows for '$($row.Label)', found $($own.Count)." }
    $inset = if ($row.Type -eq 'PushButton') { 3 } else { 0 }
    $reference = TextMask $own[-1] $inset
    $fit = $null
    $verdicts = @()
    for ($i = 0; $i -lt $own.Count - 1; $i++) {
        $full = (TextMask $own[$i] $inset).Mask -eq $reference.Mask
        $verdicts += "$($row.Widths[$i])u/$($own[$i].W)px:" + $(if ($full) { 'ok' } else { 'cut' })
        if ($full -and $null -eq $fit) { $fit = $i }
    }

    # Check box: first text column after the box frame and the empty gap, from the control's left edge.
    $textLeft = ''
    if ($row.Type -eq 'CheckBox') {
        $lines = $reference.Lines
        $width = $lines[0].Length
        $ink = @(for ($xx = 0; $xx -lt $width; $xx++) { [bool]($lines | Where-Object { $_[$xx] -eq '#' }) })
        $k = 0
        while ($k -lt $width -and -not $ink[$k]) { $k++ }   # left of the box
        while ($k -lt $width -and $ink[$k]) { $k++ }        # box frame (outer edge)
        while ($k -lt $width -and -not $ink[$k]) { $k++ }   # box interior
        while ($k -lt $width -and $ink[$k]) { $k++ }        # box frame (inner edge)
        while ($k -lt $width -and -not $ink[$k]) { $k++ }   # gap
        $textLeft = $k
    }

    $minUnits = if ($null -ne $fit) { $row.Widths[$fit] } else { '-' }
    $minPx = if ($null -ne $fit) { $own[$fit].W } else { '-' }
    $overhead = if ($null -ne $fit) { $own[$fit].W - $row.TextPx } else { '-' }
    '{0,-11} {1,-28} {2,6}  {3,11}  {4,8}  {5,12}  {6,12}' -f $row.Type, $row.Label, $row.TextPx, $minUnits, $minPx, $overhead, $textLeft
    '            ' + ($verdicts -join ' ')
}
""
"Screenshot: $png"
