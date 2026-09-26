# Génère Assets/DIYB.ico : carré arrondi bleu portant un symbole de mise sous
# tension. Toutes les résolutions sont dessinées séparément plutôt que réduites
# depuis la plus grande, sinon le trait disparaît à 16 px.
#
#   powershell -ExecutionPolicy Bypass -File tools/make-icon.ps1

Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 48, 64, 128, 256
$output = Join-Path $PSScriptRoot "..\src\DIYB.App\Assets\DIYB.ico"
$assets = Split-Path $output -Parent
if (-not (Test-Path $assets)) { New-Item -ItemType Directory -Path $assets | Out-Null }

function New-IconBitmap([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Fond : carré arrondi, dégradé du bleu accent vers un bleu plus profond.
    $pad = [Math]::Max(1, [int]($size * 0.06))
    $box = New-Object System.Drawing.Rectangle($pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad))
    $radius = [Math]::Max(2, [int]($size * 0.22))

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($box.X, $box.Y, $d, $d, 180, 90)
    $path.AddArc(($box.Right - $d), $box.Y, $d, $d, 270, 90)
    $path.AddArc(($box.Right - $d), ($box.Bottom - $d), $d, $d, 0, 90)
    $path.AddArc($box.X, ($box.Bottom - $d), $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $box,
        [System.Drawing.Color]::FromArgb(255, 32, 110, 190),
        [System.Drawing.Color]::FromArgb(255, 16, 62, 120),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillPath($brush, $path)

    # Symbole de mise sous tension : arc ouvert vers le haut et barre verticale.
    $stroke = [Math]::Max(1.6, $size * 0.11)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $stroke)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $inset = $size * 0.28
    $arc = New-Object System.Drawing.RectangleF($inset, ($inset * 1.05), ($size - 2 * $inset), ($size - 2 * $inset))
    $g.DrawArc($pen, $arc, -60, 300)

    $cx = $size / 2.0
    $g.DrawLine($pen, $cx, ($size * 0.20), $cx, ($size * 0.47))

    $pen.Dispose(); $brush.Dispose(); $path.Dispose(); $g.Dispose()
    return $bitmap
}

# Assemblage ICO : chaque image est stockée en PNG, accepté depuis Windows Vista
# et seul moyen d'embarquer proprement le 256 px.
$streams = @()
foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size
    $memory = New-Object System.IO.MemoryStream
    $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
    $streams += , @{ Size = $size; Bytes = $memory.ToArray() }
    $bitmap.Dispose(); $memory.Dispose()
}

$file = [System.IO.File]::Create((New-Item -ItemType File -Path $output -Force).FullName)
$writer = New-Object System.IO.BinaryWriter($file)

$writer.Write([UInt16]0)                 # réservé
$writer.Write([UInt16]1)                 # type : icône
$writer.Write([UInt16]$streams.Count)

$offset = 6 + (16 * $streams.Count)
foreach ($entry in $streams) {
    $dimension = if ($entry.Size -ge 256) { 0 } else { $entry.Size }
    $writer.Write([Byte]$dimension)      # largeur
    $writer.Write([Byte]$dimension)      # hauteur
    $writer.Write([Byte]0)               # palette
    $writer.Write([Byte]0)               # réservé
    $writer.Write([UInt16]1)             # plans
    $writer.Write([UInt16]32)            # bits par pixel
    $writer.Write([UInt32]$entry.Bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $entry.Bytes.Length
}

foreach ($entry in $streams) { $writer.Write($entry.Bytes) }

$writer.Flush(); $writer.Dispose(); $file.Dispose()

$info = Get-Item $output
"Icone generee : $($info.FullName) ($([int]$info.Length) octets, $($streams.Count) resolutions)"
