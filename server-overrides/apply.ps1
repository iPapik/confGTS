$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

Write-Host "Applying ConfGTS Server 0.18.7 overlays..." -ForegroundColor Cyan

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

# When the final participant leaves (or expires by TTL), close any open recording
# metadata for the conference session. Client-side MediaRecorder normally calls
# /api/recordings/finalize, but this server-side guard prevents an unfinished
# archive entry after a crash, power loss, or abrupt client termination.
$sessionEndPattern = '(?s)(func \(s \*Store\) endIfEmptyLocked\(roomID string, lr \*LiveRoom\) \{.*?)(\r?\n\tlr\.Session = nil\r?\n\tlr\.Recorder = "")'
$sessionEndRegex = [regex]::new($sessionEndPattern)
$sessionEndMatches = $sessionEndRegex.Matches($store)
if ($sessionEndMatches.Count -ne 1) {
    throw "Expected exactly one ConfGTS endIfEmptyLocked recording anchor, found $($sessionEndMatches.Count)."
}
$store = $sessionEndRegex.Replace(
    $store,
    {
        param($m)
        $m.Groups[1].Value +
        [Environment]::NewLine +
        "`tfor i := range s.state.Recordings {" + [Environment]::NewLine +
        "`t`tif s.state.Recordings[i].SessionID == id && s.state.Recordings[i].FinishedAt == nil {" + [Environment]::NewLine +
        "`t`t`ts.state.Recordings[i].FinishedAt = &now" + [Environment]::NewLine +
        "`t`t}" + [Environment]::NewLine +
        "`t}" +
        $m.Groups[2].Value
    },
    1)

Set-Content $storePath $store -Encoding UTF8 -NoNewline

$mainPath = Join-Path $root "server\main.go"
$main = Get-Content $mainPath -Raw -Encoding UTF8
$main = $main.Replace('const Version = "0.16.0"', 'const Version = "0.18.7"')
$main = $main.Replace('const Version = "0.16.1"', 'const Version = "0.18.7"')
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

# Client 0.18.3 can explicitly choose local ConfGTS authentication or
# LDAP/Active Directory. Older clients remain compatible because the default
# "auto" mode preserves the existing local-first behaviour.
$webPath = Join-Path $root "server\web.go"
$web = Get-Content $webPath -Raw -Encoding UTF8
if (-not $web.Contains('AuthType string `json:"auth_type"`')) {
    $web = $web.Replace(
        'Password string `json:"password"`',
        'Password string `json:"password"`' + [Environment]::NewLine + "`t`tAuthType string ``json:`"auth_type`"``")
}

$apiAuthPattern = '(?ms)\tlocalOK := false\r?\n\tif a\.store\.HasLocalUser\(username\) \{.*?\r?\n\t\}\r?\n\tif err != nil \|\|'
$apiAuthReplacement = @'
	localOK := false
	authType := strings.ToLower(strings.TrimSpace(in.AuthType))
	if authType == "" {
		authType = "auto"
	}
	switch authType {
	case "local":
		if !a.store.HasLocalUser(username) {
			err = fmt.Errorf("local ConfGTS user not found")
		} else if lu, ok := a.store.AuthenticateLocal(username, in.Password); ok {
			u, localOK = lu, true
		} else {
			err = fmt.Errorf("invalid local credentials")
		}
	case "domain":
		if a.store.Config().LDAP.Enabled {
			u, err = ldapAuthenticate(a.store.Config().LDAP, username, in.Password)
		} else {
			err = fmt.Errorf("LDAP / Active Directory is not configured")
		}
	default:
		if a.store.HasLocalUser(username) {
			if lu, ok := a.store.AuthenticateLocal(username, in.Password); ok {
				u, localOK = lu, true
			} else {
				err = fmt.Errorf("invalid local credentials")
			}
		} else if dev := os.Getenv("CONFGTS_DEV_USER"); dev != "" && normUser(dev) == username {
			u, localOK = User{Username: username, DisplayName: username + " (DEV)"}, true
		} else if a.store.Config().LDAP.Enabled {
			u, err = ldapAuthenticate(a.store.Config().LDAP, username, in.Password)
		} else {
			err = fmt.Errorf("LDAP / Active Directory is not configured")
		}
	}
	if err != nil ||
'@
$apiAuthRegex = [regex]::new($apiAuthPattern)
if ($apiAuthRegex.IsMatch($web)) {
    $web = $apiAuthRegex.Replace($web, $apiAuthReplacement.TrimEnd("`r","`n"), 1)
} elseif (-not $web.Contains('authType := strings.ToLower(strings.TrimSpace(in.AuthType))')) {
    throw "ConfGTS API login authentication block was not found."
}
Set-Content $webPath $web -Encoding UTF8 -NoNewline

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

# A participant must be able to join even when camera/microphone APIs are
# unavailable, permissions are denied, or the page is opened from an HTTP
# origin where Chromium does not expose navigator.mediaDevices.
$ensureMediaPattern = '(?s)async function ensureMedia\(\)\{.*?\}\r?\nasync function startHomePreview'
$ensureMediaReplacement = @'
async function ensureMedia(){if(localStream)return localStream;if(!navigator.mediaDevices||typeof navigator.mediaDevices.getUserMedia!=='function'){localStream=new MediaStream();console.info('ConfGTS: media API unavailable, continuing without local media');return localStream}try{let p=mediaPrefs();let ac=p.audioInput?{deviceId:{exact:p.audioInput},echoCancellation:true,noiseSuppression:true,autoGainControl:true}:{echoCancellation:true,noiseSuppression:true,autoGainControl:true};let vc=p.videoInput?{deviceId:{exact:p.videoInput}}:true;rawStream=await navigator.mediaDevices.getUserMedia({video:vc,audio:ac});let tracks=[];rawStream.getVideoTracks().forEach(t=>tracks.push(t));let at=rawStream.getAudioTracks()[0];if(at){mediaAudioCtx=new AudioContext();let src=mediaAudioCtx.createMediaStreamSource(new MediaStream([at]));let gain=mediaAudioCtx.createGain();gain.gain.value=Number(p.micVolume??1);let dst=mediaAudioCtx.createMediaStreamDestination();src.connect(gain).connect(dst);dst.stream.getAudioTracks().forEach(t=>tracks.push(t))}localStream=new MediaStream(tracks);return localStream}catch(e){console.warn('ConfGTS: local media unavailable; joining without it',e);localStream=new MediaStream();return localStream}}
async function startHomePreview
'@
$ensureMediaRegex = [regex]::new($ensureMediaPattern)
if ($ensureMediaRegex.IsMatch($ui)) {
    $ui = $ensureMediaRegex.Replace($ui, $ensureMediaReplacement.TrimEnd("`r","`n"), 1)
} elseif (-not $ui.Contains('media API unavailable, continuing without local media')) {
    throw 'ConfGTS ensureMedia function was not found.'
}

$joinRoomPattern = '(?s)async function joinRoom\(\)\{.*?\}\r?\nasync function leaveRoom'
$joinRoomReplacement = @'
async function joinRoom(){if(!currentRoom)return;await ensureMedia();roomState=await api('/api/rooms/'+currentRoom+'/join',{method:'POST'});$('#joinBtn').style.display='none';$('#leaveBtn').style.display='inline-flex';$('#conference').style.display='block';addVideo('me',localStream,ME.display_name+' (Вы)',true);startLoops();await reconcilePeers();updateStateUI()}
async function leaveRoom
'@
$joinRoomRegex = [regex]::new($joinRoomPattern)
if ($joinRoomRegex.IsMatch($ui)) {
    $ui = $joinRoomRegex.Replace($ui, $joinRoomReplacement.TrimEnd("`r","`n"), 1)
} elseif ($ui.Contains("alert('Не удалось получить камеру/микрофон")) {
    throw 'ConfGTS joinRoom media blocking logic was not replaced.'
}

# Flush and finalize the active MediaRecorder before telling the server that
# this participant left. The recording upload endpoint only accepts the currently
# elected recorder, so sending /leave first could reject the final WebM chunk.
$stopRecordingPattern = '(?s)function stopRecording\(\)\{.*?\}\r?\nwindow\.addEventListener'
$stopRecordingReplacement = @'
async function stopRecording(){let active=recorder;if(active&&active.state!=='inactive'){await new Promise(resolve=>{let done=false;let settle=()=>{if(done)return;done=true;Promise.resolve(recordQueue).catch(()=>{}).finally(resolve)};let previous=active.onstop;active.onstop=()=>{try{if(previous)previous()}finally{setTimeout(settle,0)}};try{active.requestData()}catch{};try{active.stop()}catch{settle()};setTimeout(settle,6500)})}else{await Promise.resolve(recordQueue).catch(()=>{})}}
window.addEventListener
'@
$stopRecordingRegex = [regex]::new($stopRecordingPattern)
if ($stopRecordingRegex.IsMatch($ui)) {
    $ui = $stopRecordingRegex.Replace($ui, $stopRecordingReplacement.TrimEnd("`r","`n"), 1)
} elseif (-not $ui.Contains("async function stopRecording(){let active=recorder")) {
    throw 'ConfGTS stopRecording function was not found.'
}

$leaveRecordingPattern = '(?s)async function leaveRoom\(\)\{.*?\}\r?\nfunction addVideoPreviewHome'
$leaveRecordingReplacement = @'
async function leaveRoom(){if(!currentRoom)return;await stopRecording();try{await api('/api/rooms/'+currentRoom+'/leave',{method:'POST'})}catch{};stopLoops(false);for(let [k,p] of peers){p.close()}peers.clear();document.querySelectorAll('.video-tile').forEach(x=>x.remove());addVideoPreviewHome();roomState=null;$('#conference').style.display='none';$('#joinBtn').style.display='inline-flex';$('#leaveBtn').style.display='none';await loadRooms()}
function addVideoPreviewHome
'@
$leaveRecordingRegex = [regex]::new($leaveRecordingPattern)
if ($leaveRecordingRegex.IsMatch($ui)) {
    $ui = $leaveRecordingRegex.Replace($ui, $leaveRecordingReplacement.TrimEnd("`r","`n"), 1)
} elseif (-not $ui.Contains("async function leaveRoom(){if(!currentRoom)return;await stopRecording();")) {
    throw 'ConfGTS leaveRoom recording flush patch was not applied.'
}

# The native client can request a room immediately after WebView navigation.
# Harden the server page against that startup race: selectRoom must not dereference
# an undefined room while loadRooms() is still in flight.
$selectRoomPattern = '(?s)async function selectRoom\(id\)\{.*?\}\r?\nasync function ensureMedia'
$selectRoomReplacement = @'
async function selectRoom(id){currentRoom=id;if(!Array.isArray(rooms)||!rooms.some(x=>x&&x.id===id)){try{await loadRooms()}catch(e){console.warn(e)}}let r=(rooms||[]).find(x=>x&&x.id===id);if(!r){console.warn('ConfGTS: room not found',id);return false}renderRooms();$('#roomTitle').textContent=r.name;$('#roomDesc').textContent=r.description||'Постоянная корпоративная конференция';$('#joinBtn').style.display='inline-flex';$('#leaveBtn').style.display='none';$('#conference').style.display='none';await refreshRoomState();return true}
async function ensureMedia
'@
$selectRoomRegex = [regex]::new($selectRoomPattern)
if ($selectRoomRegex.IsMatch($ui)) {
    $ui = $selectRoomRegex.Replace($ui, $selectRoomReplacement.TrimEnd("`r","`n"), 1)
} elseif (-not $ui.Contains("ConfGTS: room not found")) {
    throw 'ConfGTS selectRoom startup-race guard was not applied.'
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
        $text = $text.Replace("0.16.0", "0.18.7").Replace("0.16.1", "0.18.7").Replace("0.17.0", "0.18.7").Replace("0.18.0", "0.18.7").Replace("0.18.1", "0.18.7").Replace("0.18.0", "0.18.7").Replace("0.18.1", "0.18.7")
        Set-Content $target $text -Encoding UTF8 -NoNewline
    } elseif (Test-Path $target -PathType Container) {
        Get-ChildItem $target -Recurse -File -Include *.go,*.cs,*.xaml,*.csproj,*.wxs,*.wixproj,*.ps1 | ForEach-Object {
            $text = Get-Content $_.FullName -Raw -Encoding UTF8
            if ($text.Contains("0.16.0") -or $text.Contains("0.16.1") -or $text.Contains("0.17.0") -or $text.Contains("0.18.0") -or $text.Contains("0.18.1")) {
                $text = $text.Replace("0.16.0", "0.18.7").Replace("0.16.1", "0.18.7").Replace("0.17.0", "0.18.7")
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
        $bundle = $bundle.Replace('          Version="0.18.7"', '          Version="0.18.7"' + [Environment]::NewLine + '          IconSourceFile="!(bindpath.assets)\ConfGTS.ico"')
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
