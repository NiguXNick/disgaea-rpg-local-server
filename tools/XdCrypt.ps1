# Helpers for the game's settings files (XD.tool.XDCryptor): gzip, then RC4. The RC4 key is not
# stored here: Initialize-XdCrypt reads it from the installed game's XDDLL.dll
# (XD.tool.FastCryptUtil.key_string) without running any game code.
# Dot-source this file: . .\XdCrypt.ps1, then call Initialize-XdCrypt -GameDir <game folder>.

function Invoke-Rc4([byte[]]$Data, [byte[]]$Key) {
    $s = New-Object byte[] 256
    for ($i = 0; $i -lt 256; $i++) { $s[$i] = [byte]$i }
    $j = 0
    for ($i = 0; $i -lt 256; $i++) {
        $j = ($j + $s[$i] + $Key[$i % $Key.Length]) % 256
        $t = $s[$i]; $s[$i] = $s[$j]; $s[$j] = $t
    }
    $out = New-Object byte[] $Data.Length
    $i = 0; $j = 0
    for ($n = 0; $n -lt $Data.Length; $n++) {
        $i = ($i + 1) % 256
        $j = ($j + $s[$i]) % 256
        $t = $s[$i]; $s[$i] = $s[$j]; $s[$j] = $t
        $out[$n] = $Data[$n] -bxor $s[($s[$i] + $s[$j]) % 256]
    }
    return , $out
}

function Initialize-XdCrypt([string]$GameDir) {
    $dll = Join-Path $GameDir "DISGAEA RPG_Data\Managed\XDDLL.dll"
    if (-not (Test-Path $dll)) { throw "XDDLL.dll not found in $GameDir." }
    $asm = [Reflection.Assembly]::ReflectionOnlyLoadFrom($dll)
    $field = $asm.GetType("XD.tool.FastCryptUtil").GetField("key_string", [Reflection.BindingFlags]"NonPublic,Static")
    $script:XdKey = [Text.Encoding]::UTF8.GetBytes([string]$field.GetRawConstantValue())
}

function ConvertFrom-XdSettings([byte[]]$Bytes) {
    $plain = Invoke-Rc4 $Bytes $script:XdKey
    $gz = New-Object IO.Compression.GZipStream((New-Object IO.MemoryStream(, $plain)), [IO.Compression.CompressionMode]::Decompress)
    (New-Object IO.StreamReader($gz, [Text.Encoding]::UTF8)).ReadToEnd()
}

function ConvertTo-XdSettings([string]$Text) {
    $raw = (New-Object Text.UTF8Encoding($false)).GetBytes($Text)
    $ms = New-Object IO.MemoryStream
    $gz = New-Object IO.Compression.GZipStream($ms, [IO.Compression.CompressionMode]::Compress)
    $gz.Write($raw, 0, $raw.Length); $gz.Close()
    Invoke-Rc4 $ms.ToArray() $script:XdKey
}

# Only the global Steam release, client 3.2.10, is supported: app.info names the publisher
# (Boltrend) and globalgamemanagers holds the app version as a length-prefixed string.
function Assert-SupportedGame([string]$GameDir) {
    $data = Join-Path $GameDir "DISGAEA RPG_Data"
    $info = Join-Path $data "app.info"
    if (-not (Test-Path $info)) { throw "No DISGAEA RPG install found in $GameDir. Pass -GameDir with the game folder." }
    $publisher = (Get-Content $info -TotalCount 1).Trim()
    $bytes = [IO.File]::ReadAllBytes((Join-Path $data "globalgamemanagers"))
    $text = [Text.Encoding]::GetEncoding(28591).GetString($bytes)
    $versioned = $text.Contains([string][char]6 + [char]0 + [char]0 + [char]0 + "3.2.10")
    if ($publisher -ne "Boltrend" -or -not $versioned) {
        throw "Unsupported game build (publisher '$publisher'). Only the global Steam release, client 3.2.10, is supported; nothing was changed."
    }
}
