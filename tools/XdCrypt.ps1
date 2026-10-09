# Helpers for the game's settings files (XD.tool.XDCryptor): gzip, then RC4 with the key
# "<read from the installed game>". Dot-source this file: . .\XdCrypt.ps1

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

$script:XdKey = [Text.Encoding]::UTF8.GetBytes("<read from the installed game>")

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
