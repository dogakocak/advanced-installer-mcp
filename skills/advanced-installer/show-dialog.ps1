<#
.SYNOPSIS
  Opens one dialog of a built MSI with msiexec, without installing anything, and saves a screenshot of it.

.DESCRIPTION
  Works on a copy: removes both sequences, all custom actions and the embedded UI, puts only the chosen dialog in the
  UI sequence, makes every push button of it end the dialog, gives the copy a new ProductCode, then runs msiexec on
  the copy and captures the dialog window from the screen (PrintWindow misses some controls). The original MSI is not
  touched.

.EXAMPLE
  powershell -NoProfile -File show-dialog.ps1 -Msi .\Setup.msi -Dialog WelcomeDlg -Png .\welcome.png -Properties 'AI_REPLACE_PRODUCTS=1'
#>
param(
    [Parameter(Mandatory)] [string] $Msi,
    [Parameter(Mandatory)] [string] $Dialog,
    [Parameter(Mandatory)] [string] $Png,
    [string[]] $Properties = @()
)

$ErrorActionPreference = 'Stop'
$copy = Join-Path ([IO.Path]::GetTempPath()) ("show-dialog-" + [guid]::NewGuid().ToString('N') + '.msi')
Copy-Item -LiteralPath $Msi -Destination $copy

# Edit the copy in a child process: a live COM handle keeps the file open and msiexec then fails with 1619.
$edit = {
    param($path, $dialog)
    $ErrorActionPreference = 'Stop'
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $installer.OpenDatabase($path, 1) # transact
    # [void]: Execute() and Close() return $null, which would otherwise become part of a function's output.
    function Run($sql) {
        try { $v = $db.OpenView($sql); [void]$v.Execute(); [void]$v.Close() }
        catch { throw "$sql failed: $($_.Exception.Message)" }
    }
    function Rows($sql) {
        $v = $db.OpenView($sql); [void]$v.Execute()
        while ($r = $v.Fetch()) { $r.StringData(1) }
        [void]$v.Close()
    }
    function HasTable($name) { @(Rows "SELECT ``Name`` FROM ``_Tables`` WHERE ``Name`` = '$name'").Count -gt 0 }

    if (@(Rows "SELECT ``Dialog`` FROM ``Dialog`` WHERE ``Dialog`` = '$dialog'").Count -eq 0) { throw "No dialog '$dialog'." }
    foreach ($table in 'InstallExecuteSequence', 'InstallUISequence', 'CustomAction', 'MsiEmbeddedUI') {
        if (HasTable $table) { Run "DELETE FROM ``$table``" }
    }
    Run "INSERT INTO ``InstallUISequence`` (``Action``, ``Sequence``) VALUES ('$dialog', 100)"
    Run "DELETE FROM ``ControlEvent`` WHERE ``Dialog_`` = '$dialog'"
    foreach ($button in Rows "SELECT ``Control`` FROM ``Control`` WHERE ``Dialog_`` = '$dialog' AND ``Type`` = 'PushButton'") {
        Run "INSERT INTO ``ControlEvent`` (``Dialog_``, ``Control_``, ``Event``, ``Argument``, ``Condition``, ``Ordering``) VALUES ('$dialog', '$button', 'EndDialog', 'Exit', '1', 1)"
    }
    $code = '{' + [guid]::NewGuid().ToString().ToUpper() + '}'
    Run "UPDATE ``Property`` SET ``Value`` = '$code' WHERE ``Property`` = 'ProductCode'"
    # A new package code too: with the original one, msiexec takes the copy for the installed product's cached
    # package and runs that product's own sequences instead.
    $summary = $db.SummaryInformation(1)
    $summary.Property(9) = '{' + [guid]::NewGuid().ToString().ToUpper() + '}'
    $summary.Persist()
    $db.Commit()
}
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class Win {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);

    // True when the window itself is what the screen shows at these points (nothing covers it).
    public static bool OnTop(IntPtr h, RECT r) {
        int[][] points = { new[] { r.Left + 8, r.Top + 8 }, new[] { r.Right - 8, r.Bottom - 8 }, new[] { (r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2 } };
        foreach (var p in points) {
            var at = WindowFromPoint(new POINT { X = p[0], Y = p[1] });
            if (GetAncestor(at, 2 /* GA_ROOT */) != h) return false;
        }
        return true;
    }
}
'@

$process = $null
try {
    powershell -NoProfile -Command $edit -args $copy, $Dialog
    if ($LASTEXITCODE) { throw "Editing the copy failed." }

    $process = Start-Process msiexec.exe -ArgumentList (@('/i', "`"$copy`"") + $Properties) -PassThru
    $window = [IntPtr]::Zero
    for ($i = 0; $i -lt 60 -and $window -eq [IntPtr]::Zero; $i++) {
        Start-Sleep -Milliseconds 500
        $window = (Get-Process -Id $process.Id).MainWindowHandle
    }
    if ($window -eq [IntPtr]::Zero) { throw 'The dialog did not open.' }
    # Topmost instead of SetForegroundWindow, which Windows refuses to a background process. Then check that nothing
    # covers the dialog before saving, so a capture never contains whatever else is on the screen.
    [Win]::SetWindowPos($window, [IntPtr](-1), 0, 0, 0, 0, 0x0043) | Out-Null # HWND_TOPMOST; NOSIZE|NOMOVE|SHOWWINDOW
    Start-Sleep -Milliseconds 1000 # let it paint
    $r = New-Object Win+RECT
    [Win]::GetWindowRect($window, [ref]$r) | Out-Null
    if (-not [Win]::OnTop($window, $r)) { throw 'Another window covers the dialog; nothing was saved.' }
    $bitmap = New-Object Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($r.Left, $r.Top, 0, 0, $bitmap.Size)
    $bitmap.Save($Png, [Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
    "Saved $Png"
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    Start-Sleep -Milliseconds 500
    Remove-Item -LiteralPath $copy -ErrorAction SilentlyContinue
}
