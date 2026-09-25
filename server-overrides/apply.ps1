$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

Write-Host "Applying ConfGTS Server 0.18.2 overlays..." -ForegroundColor Cyan

Copy-Item (Join-Path $PSScriptRoot "src\*") (Join-Path $root "src") -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot "server\*") (Join-Path $root "server") -Recurse -Force

$storePath = Join-Path $root "server\store.go"
$store = Get-Content $storePath -Raw -Encoding UTF8
$store = $store.Replace('ListenAddr:   "127.0.0.1:8090"', 'ListenAddr:   "0.0.0.0:8090"')

$legacyPattern = '(?ms)\s*// 0\.10\.4: wildcard listeners from older builds are migrated to loopback\..*?\r?\n\s*if legacyLDAPConfig \{'
$legacyReplacement = [Environment]::NewLine + '    if legacyLDAPConfig {'
$patchedStore = [regex]::Replace($store, $legacyPattern, $legacyReplacement)
if ($patchedStore -ne $store) {
    $store = $patchedStore
    $store = [regex]::Replace($store, '(?m)^\s*"net"\s*\r?\n', '')
} else {
    Write-Host "Legacy loopback migration block was not present; continuing." -ForegroundColor Yellow
}
Set-Content $storePath $store -Encoding UTF8 -NoNewline

$mainPath = Join-Path $root "server\main.go"
$main = Get-Content $mainPath -Raw -Encoding UTF8
$main = $main.Replace('const Version = "0.16.0"', 'const Version = "0.18.2"')
$main = $main.Replace('const Version = "0.16.1"', 'const Version = "0.18.2"')
$main = $main.Replace('cfg.ListenAddr == ":8090" {', 'cfg.ListenAddr == ":8090" || cfg.ListenAddr == "0.0.0.0:8090" {')
$main = $main.Replace('cfg.ListenAddr = "127.0.0.1:" + strconv.Itoa(n)', 'cfg.ListenAddr = "0.0.0.0:" + strconv.Itoa(n)')
$main = $main.Replace('addr = "127.0.0.1:8090"', 'addr = "0.0.0.0:8090"')
$httpsPattern = '(?m)^\tcfg := store\.Config\(\)\r?\n\taddr := strings\.TrimSpace\(cfg\.ListenAddr\)'
$httpsBlock = @'
	cfg := store.Config()
	if cfg.HTTPS.Enabled {
		cfgWithCert, certErr := ensureHTTPSCertificate(cfg)
		if certErr != nil {
			return certErr
		}
		cfg = cfgWithCert
		_ = store.SaveConfig(cfg)
	}
	addr := strings.TrimSpace(cfg.ListenAddr)
'@
$httpsBlock = $httpsBlock -replace "`r`n", "`n"
$httpsRegex = [regex]::new($httpsPattern)
if ($httpsRegex.IsMatch($main)) {
    $main = $httpsRegex.Replace($main, $httpsBlock.TrimEnd("`r","`n"), 1)
} else {
    Write-Host "HTTPS runServer anchor not found; continuing." -ForegroundColor Yellow
}
Set-Content $mainPath $main -Encoding UTF8 -NoNewline

$adminPath = Join-Path $root "server\admin.go"
$admin = Get-Content $adminPath -Raw -Encoding UTF8
$admin = $admin.Replace('c.ListenAddr = "127.0.0.1:8090"', 'c.ListenAddr = "0.0.0.0:8090"')

# Local users in ConfGTS do not require e-mail. Keep the storage/API call
# compatible by passing an empty string and remove the field from the form.
$admin = $admin.Replace(
    'a.store.CreateLocalUser(r.FormValue("username"), r.FormValue("display_name"), r.FormValue("email"), r.FormValue("password"), u.Username)',
    'a.store.CreateLocalUser(r.FormValue("username"), r.FormValue("display_name"), "", r.FormValue("password"), u.Username)')
$admin = $admin.Replace(
    '<div class=field><label>E-mail</label><input name=email type=email></div>',
    '')
Set-Content $adminPath $admin -Encoding UTF8 -NoNewline

$uiPath = Join-Path $root "server\ui.go"
$ui = Get-Content $uiPath -Raw -Encoding UTF8
$replacements = [ordered]@{
    '#2b2b2d' = '#0B2F5B'
    '#3a3a3d' = '#164C79'
    '#f2a21b' = '#168CB8'
    '#ffc72c' = '#16B6C2'
    '#ef9c12' = '#168CB8'
    '#f4f3ef' = '#EEF7FC'
    '#262628' = '#28445F'
    '#747474' = '#6A7E93'
    '#e5e1d7' = '#D8E6EF'
    '#ece9e1' = '#E8F4F9'
    '#f0eee8' = '#E8F4F9'
    '#f8f7f3' = '#F6FBFE'
    '#d8d4ca' = '#C7DCE8'
    '#7d766b' = '#5D7891'
    '#766f65' = '#607A91'
    '#5b554b' = '#42637E'
    '#f6f4ee' = '#F1F8FC'
    '#fff9ed' = '#F0FAFC'
    '#a46700' = '#0E769B'
}
foreach ($entry in $replacements.GetEnumerator()) {
    $ui = $ui.Replace($entry.Key, $entry.Value)
}
Set-Content $uiPath $ui -Encoding UTF8 -NoNewline

# Apply the same palette to server admin pages that contain a few
# hard-coded legacy orange/grey values outside ui.go.
Get-ChildItem (Join-Path $root "server") -Filter *.go -File | ForEach-Object {
    $text = Get-Content $_.FullName -Raw -Encoding UTF8
    foreach ($entry in $replacements.GetEnumerator()) {
        $text = $text.Replace($entry.Key, $entry.Value)
    }
    Set-Content $_.FullName $text -Encoding UTF8 -NoNewline
}

$versionTargets = @(
    (Join-Path $root "server"),
    (Join-Path $root "Installer\Server"),
    (Join-Path $root "src\ConfGTS.Server.Settings"),
    (Join-Path $root "build-server.ps1")
)
foreach ($target in $versionTargets) {
    if (Test-Path $target -PathType Leaf) {
        $text = Get-Content $target -Raw -Encoding UTF8
        $text = $text.Replace("0.16.0", "0.18.2").Replace("0.16.1", "0.18.2").Replace("0.17.0", "0.18.2").Replace("0.18.0", "0.18.2").Replace("0.18.1", "0.18.2").Replace("0.18.0", "0.18.2").Replace("0.18.1", "0.18.2")
        Set-Content $target $text -Encoding UTF8 -NoNewline
    } elseif (Test-Path $target -PathType Container) {
        Get-ChildItem $target -Recurse -File -Include *.go,*.cs,*.xaml,*.csproj,*.wxs,*.wixproj,*.ps1 | ForEach-Object {
            $text = Get-Content $_.FullName -Raw -Encoding UTF8
            if ($text.Contains("0.16.0") -or $text.Contains("0.16.1") -or $text.Contains("0.17.0") -or $text.Contains("0.18.0") -or $text.Contains("0.18.1")) {
                $text = $text.Replace("0.16.0", "0.18.2").Replace("0.16.1", "0.18.2").Replace("0.17.0", "0.18.2")
                Set-Content $_.FullName $text -Encoding UTF8 -NoNewline
            }
        }
    }
}


# Generate the shared ConfGTS icon before compiling the server settings app
# and before WiX resolves shortcut/bootstrapper icon paths.
$assetScript = Join-Path $root "prepare-assets.ps1"
if (Test-Path $assetScript) {
    & $assetScript
}

$serverPackage = Join-Path $root "Installer\Server\Package.wxs"
if (Test-Path $serverPackage) {
    $wxs = Get-Content $serverPackage -Raw -Encoding UTF8
    $wxs = $wxs.Replace('WorkingDirectory="INSTALLFOLDER" />', 'WorkingDirectory="INSTALLFOLDER"' + [Environment]::NewLine + '                  Icon="ConfGTSIcon" />')
    if (-not $wxs.Contains('Id="ConfGTSIcon"')) {
        $wxs = $wxs.Replace('    <Feature Id="MainFeature"', '    <Icon Id="ConfGTSIcon" SourceFile="!(bindpath.assets)\ConfGTS.ico" />' + [Environment]::NewLine + '    <Property Id="ARPPRODUCTICON" Value="ConfGTSIcon" />' + [Environment]::NewLine + [Environment]::NewLine + '    <Feature Id="MainFeature"')
    }
    Set-Content $serverPackage $wxs -Encoding UTF8 -NoNewline
}

$serverMsiProject = Join-Path $root "Installer\Server\ConfGTS.Server.Installer.wixproj"
if (Test-Path $serverMsiProject) {
    $proj = Get-Content $serverMsiProject -Raw -Encoding UTF8
    if (-not $proj.Contains('BindName="assets"')) {
        $proj = $proj.Replace('    <BindPath Include="$(PublishDir)" BindName="publish" />', '    <BindPath Include="$(PublishDir)" BindName="publish" />' + [Environment]::NewLine + '    <BindPath Include="$(MSBuildThisFileDirectory)..\Assets" BindName="assets" />')
    }
    Set-Content $serverMsiProject $proj -Encoding UTF8 -NoNewline
}

$serverBundle = Join-Path $root "Installer\Server\Bootstrapper\Bundle.wxs"
if (Test-Path $serverBundle) {
    $bundle = Get-Content $serverBundle -Raw -Encoding UTF8
    if (-not $bundle.Contains('IconSourceFile=')) {
        $bundle = $bundle.Replace('          Version="0.18.2"', '          Version="0.18.2"' + [Environment]::NewLine + '          IconSourceFile="!(bindpath.assets)\ConfGTS.ico"')
    }
    Set-Content $serverBundle $bundle -Encoding UTF8 -NoNewline
}

$serverBundleProject = Join-Path $root "Installer\Server\Bootstrapper\ConfGTS.Server.Bootstrapper.wixproj"
if (Test-Path $serverBundleProject) {
    $proj = Get-Content $serverBundleProject -Raw -Encoding UTF8
    if (-not $proj.Contains('BindName="assets"')) {
        $proj = $proj.Replace('    <BindPath Include="$(MsiDir)" BindName="msi" />', '    <BindPath Include="$(MsiDir)" BindName="msi" />' + [Environment]::NewLine + '    <BindPath Include="$(MSBuildThisFileDirectory)..\..\Assets" BindName="assets" />')
    }
    Set-Content $serverBundleProject $proj -Encoding UTF8 -NoNewline
}

Write-Host "ConfGTS Server overlays applied." -ForegroundColor Green
