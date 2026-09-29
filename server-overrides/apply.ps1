$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

Write-Host "Applying ConfGTS Server 0.18.10 overlays..." -ForegroundColor Cyan

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
# Keep server-side recording metadata consistent when a room is stopped.
$stopRoomAnchor = 'sid := lr.Session.ID'
if ($store.Contains($stopRoomAnchor) -and -not $store.Contains('Finalize unfinished recordings for the stopped session.')) {
    $stopRoomExpanded = @'
sid := lr.Session.ID
	// Finalize unfinished recordings for the stopped session.
	for i := range s.state.Recordings {
		if s.state.Recordings[i].SessionID == sid && s.state.Recordings[i].FinishedAt == nil {
			s.state.Recordings[i].FinishedAt = &now
		}
	}
'@
    $store = $store.Replace($stopRoomAnchor, $stopRoomExpanded.TrimEnd())
}

$emptyRoomPattern = '(?m)^\s*id := lr\.Session\.ID\s*$'
if ([regex]::IsMatch($store, $emptyRoomPattern) -and -not $store.Contains('Finalize unfinished recordings when the room becomes empty.')) {
    $emptyRoomExpanded = @'
	id := lr.Session.ID
	// Finalize unfinished recordings when the room becomes empty.
	for i := range s.state.Recordings {
		if s.state.Recordings[i].SessionID == id && s.state.Recordings[i].FinishedAt == nil {
			s.state.Recordings[i].FinishedAt = &now
		}
	}
'@
    $store = [regex]::Replace($store, $emptyRoomPattern, $emptyRoomExpanded.Trim(), 1)
}

if (-not $store.Contains('func (s *Store) RecordingByCaptureID(')) {
    $recordingByIdAnchor = 'func (s *Store) RecordingByID(id string) (Recording, bool) {'
    $recordingByCapture = @'
func (s *Store) RecordingByCaptureID(captureID string) (Recording, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	for _, r := range s.state.Recordings {
		if r.CaptureID == captureID {
			return r, true
		}
	}
	return Recording{}, false
}

func (s *Store) CanUploadEndedSessionRecording(sessionID, username string, maxAge time.Duration) bool {
	s.mu.RLock()
	defer s.mu.RUnlock()
	for _, sess := range s.state.Sessions {
		if sess.ID == sessionID &&
			sess.EndedAt != nil &&
			time.Since(*sess.EndedAt) <= maxAge &&
			normUser(sess.StartedBy) == normUser(username) {
			return true
		}
	}
	return false
}

'@
    if ($store.Contains($recordingByIdAnchor)) {
        $store = $store.Replace($recordingByIdAnchor, $recordingByCapture + $recordingByIdAnchor)
    } else {
        throw 'RecordingByID anchor not found.'
    }
}

# 0.18.10 stores browser MP4 directly. If MP4 recording is unavailable,
# Chromium falls back to WebM; WebM is a Matroska subset and is stored as .mkv.
$recordingContainerPattern = '(?ms)^\s*fn := sessionID \+ "_" \+ safe \+ "\.webm"\r?\n\s*r := Recording\{ID: newID\("rec_"\), RoomID: roomID, SessionID: sessionID, CaptureID: safe, Recorder: recorder, FileName: fn, StartedAt: time\.Now\(\), ContentType: contentType\}'
$recordingContainerReplacement = @'
	ext := ".mkv"
	if strings.Contains(strings.ToLower(contentType), "mp4") {
		ext = ".mp4"
	}
	fn := sessionID + "_" + safe + ext
	r := Recording{ID: newID("rec_"), RoomID: roomID, SessionID: sessionID, CaptureID: safe, Recorder: recorder, FileName: fn, StartedAt: time.Now(), ContentType: contentType}
'@
$recordingContainerRegex = [regex]::new($recordingContainerPattern)
if ($recordingContainerRegex.IsMatch($store)) {
    $store = $recordingContainerRegex.Replace($store, $recordingContainerReplacement.Trim(), 1)
} elseif (-not $store.Contains('ext := ".mkv"')) {
    throw 'Recording container selection patch was not applied.'
}

Set-Content $storePath $store -Encoding UTF8 -NoNewline

$mainPath = Join-Path $root "server\main.go"
$main = Get-Content $mainPath -Raw -Encoding UTF8
$main = $main.Replace('const Version = "0.16.0"', 'const Version = "0.18.10"')
$main = $main.Replace('const Version = "0.16.1"', 'const Version = "0.18.10"')
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
# After an administrator stops a room, allow the elected recorder a short
# grace period to upload the final MediaRecorder chunk that was already being
# captured. The finished recording remains stored only on the server.
if (-not $web.Contains('"time"')) {
    $web = [regex]::Replace(
        $web,
        'import\s*\(',
        'import (' + [Environment]::NewLine + [char]9 + '"time"',
        1)
}

$recordAuthPattern = '(?ms)^\s*sess, _, recorder := a\.store\.RoomStatus\(roomID\)\r?\n\s*if sess == nil \|\| sess\.ID != sessionID \|\| recorder != normUser\(u\.Username\) \{\r?\n\s*http\.Error\(w, "not recorder", 403\)\r?\n\s*return\r?\n\s*\}'
$recordAuthNew = @'
	sess, _, recorder := a.store.RoomStatus(roomID)
	authorized := sess != nil && sess.ID == sessionID && recorder == normUser(u.Username)
	if !authorized && capture != "" {
		if existing, ok := a.store.RecordingByCaptureID(capture); ok &&
			existing.RoomID == roomID &&
			existing.SessionID == sessionID &&
			normUser(existing.Recorder) == normUser(u.Username) &&
			existing.FinishedAt != nil &&
			time.Since(*existing.FinishedAt) <= 30*time.Second {
			authorized = true
		}
		if !authorized && a.store.CanUploadEndedSessionRecording(sessionID, u.Username, 30*time.Second) {
			authorized = true
		}
	}
	if !authorized {
		http.Error(w, "not recorder", 403)
		return
	}
'@
$recordAuthRegex = [regex]::new($recordAuthPattern)
if ($recordAuthRegex.IsMatch($web)) {
    $web = $recordAuthRegex.Replace($web, $recordAuthNew.Trim(), 1)
} elseif (-not $web.Contains('time.Since(*existing.FinishedAt) <= 30*time.Second')) {
    throw 'Recording upload authorization block was not found.'
}

# Old 0.18.9 clients send an empty part=-1 request before MediaRecorder has
# produced any bytes. Do not turn that handshake into a zero-byte archive entry.
$recordingStartAnchor = 'rec, err := a.store.StartOrGetRecording(roomID, sessionID, capture, u.Username, r.Header.Get("Content-Type"))'
if ($web.Contains($recordingStartAnchor) -and -not $web.Contains('zero-byte recording upload ignored')) {
    $emptyUploadGuard = @'
	if len(data) == 0 {
		// zero-byte recording upload ignored
		writeJSON(w, map[string]any{"ok": true, "ignored": true})
		return
	}
'@
    $web = $web.Replace($recordingStartAnchor, $emptyUploadGuard.TrimEnd() + [Environment]::NewLine + [char]9 + $recordingStartAnchor)
} elseif (-not $web.Contains('zero-byte recording upload ignored')) {
    throw 'Zero-byte recording upload guard was not applied.'
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

# The upload queue must not read mutable roomState/currentRoom values after a
# conference leave/stop. Add one immutable context pointer for the active capture.
$ui = $ui.Replace(
    'captureId=null, recordPart=0;',
    'captureId=null, recordPart=0, recordingContext=null;'
)

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

# Recording is assigned to the first participant by Store.Join. The elected
# recorder composites every visible video/screen stream plus all audio into a
# WebM stream and uploads chunks to the server. Flush and finalize the recording
# BEFORE the recorder leaves the room; otherwise the final chunk can be rejected
# after Store.Leave clears/reassigns Recorder.
$stopRecordingPattern = '(?s)function stopRecording\(\)\{.*?\}\r?\nwindow\.addEventListener'
$stopRecordingReplacement = @'
async function stopRecording(){if(!recorder||recorder.state==='inactive')return recordQueue;let active=recorder;return new Promise(resolve=>{let oldStop=active.onstop;active.onstop=()=>{try{if(oldStop)oldStop()}finally{Promise.resolve(recordQueue).finally(resolve)}};try{active.requestData()}catch{};try{active.stop()}catch{resolve()}})}
window.addEventListener
'@
$stopRecordingRegex = [regex]::new($stopRecordingPattern)
if ($stopRecordingRegex.IsMatch($ui)) {
    $ui = $stopRecordingRegex.Replace($ui, $stopRecordingReplacement.TrimEnd("`r","`n"), 1)
} elseif (-not $ui.Contains('Promise.resolve(recordQueue).finally(resolve)')) {
    throw 'ConfGTS stopRecording function was not found.'
}

$leaveRoomPattern = '(?s)async function leaveRoom\(\)\{.*?\}\r?\nfunction addVideoPreviewHome'
$leaveRoomReplacement = @'
async function leaveRoom(){if(!currentRoom)return;stopLoops(false);await stopRecording();try{await api('/api/rooms/'+currentRoom+'/leave',{method:'POST'})}catch{};for(let [k,p] of peers){p.close()}peers.clear();document.querySelectorAll('.video-tile').forEach(x=>x.remove());addVideoPreviewHome();roomState=null;$('#conference').style.display='none';$('#joinBtn').style.display='inline-flex';$('#leaveBtn').style.display='none';await loadRooms()}
function addVideoPreviewHome
'@
$leaveRoomRegex = [regex]::new($leaveRoomPattern)
if ($leaveRoomRegex.IsMatch($ui)) {
    $ui = $leaveRoomRegex.Replace($ui, $leaveRoomReplacement.TrimEnd("`r","`n"), 1)
} elseif (-not $ui.Contains('stopLoops(false);await stopRecording();try{await api')) {
    throw 'ConfGTS leaveRoom recording-flush patch was not applied.'
}


# Store.Join normalizes domain usernames; normalize the browser-side username too
# so the first participant actually starts the automatic recorder.
$updateStateOld = @'
function updateStateUI(){if(!roomState)return;let ps=roomState.participants||[];$('#participants').innerHTML=participantHTML(ps);$('#previewParticipants').innerHTML=participantHTML(ps);let isRec=roomState.recorder===ME.username.toLowerCase();$('#recState').innerHTML=isRec?'<span class="pill rec"><span class="rec-dot"></span>Автозапись с этого ПК</span>':'<span class="pill"><span class="rec-dot"></span>Автозапись конференции</span>';if(isRec&&!recorder)startRecording();if(!isRec&&recorder)stopRecording();}
'@
$updateStateNew = @'
function updateStateUI(){if(!roomState)return;let ps=roomState.participants||[];$('#participants').innerHTML=participantHTML(ps);$('#previewParticipants').innerHTML=participantHTML(ps);let norm=u=>{u=String(u||'').trim().toLowerCase();if(u.includes('\\'))u=u.split('\\').pop();if(u.endsWith('@teplo.local'))u=u.slice(0,-'@teplo.local'.length);return u};let isRec=norm(roomState.recorder)===norm(ME.username);$('#recState').innerHTML=isRec?'<span class="pill rec"><span class="rec-dot"></span>Автозапись с этого ПК</span>':'<span class="pill"><span class="rec-dot"></span>Автозапись конференции</span>';if(isRec&&!recorder)startRecording();if(!isRec&&recorder)stopRecording();}
'@
if ($ui.Contains($updateStateOld.Trim())) {
    $ui = $ui.Replace($updateStateOld.Trim(), $updateStateNew.Trim())
} elseif (-not $ui.Contains('norm(roomState.recorder)===norm(ME.username)')) {
    throw 'Recorder username normalization patch was not applied.'
}

# Recording must also work when this PC has no microphone. In that case the
# server still receives the conference video/screen composition without audio.
$startRecordingPattern = '(?s)async function startRecording\(\)\{.*?\}\r?\nfunction drawRecording'
$startRecordingReplacement = @'
async function startRecording(){if(recorder||!roomState?.session)return;if(typeof MediaRecorder==='undefined'){console.warn('ConfGTS: MediaRecorder unavailable');return}let ctx={room:String(currentRoom||''),session:String(roomState.session.id||''),capture:'cap_'+Date.now()+'_'+Math.random().toString(16).slice(2),startedAt:Date.now(),bytes:0,mime:''};if(!ctx.room||!ctx.session)return;recordingContext=ctx;captureId=ctx.capture;recordPart=0;recordQueue=Promise.resolve();recorderCanvas=document.createElement('canvas');recorderCanvas.width=1280;recorderCanvas.height=720;recorderCtx=recorderCanvas.getContext('2d');audioCtx=null;audioDest=null;audioSeen=new WeakSet();let cs=recorderCanvas.captureStream(15);try{let AC=window.AudioContext||window.webkitAudioContext;if(AC){audioCtx=new AC();audioDest=audioCtx.createMediaStreamDestination();document.querySelectorAll('.video-tile video').forEach(v=>{if(v.srcObject)addAudioStream(v.srcObject)});audioDest.stream.getAudioTracks().forEach(t=>cs.addTrack(t))}}catch(e){console.warn('ConfGTS recording audio mix unavailable',e);audioCtx=null;audioDest=null}let hasAudio=cs.getAudioTracks().length>0;let candidates=hasAudio?['video/mp4;codecs=avc1.42E01E,mp4a.40.2','video/mp4','video/webm;codecs=vp8,opus','video/webm']:['video/mp4;codecs=avc1.42E01E','video/mp4','video/webm;codecs=vp8','video/webm'];let mime=candidates.find(x=>MediaRecorder.isTypeSupported(x))||'';try{let opts={videoBitsPerSecond:1800000};if(mime)opts.mimeType=mime;recorder=new MediaRecorder(cs,opts)}catch(e){console.error('ConfGTS recorder start failed',e);recordingContext=null;recorder=null;return}ctx.mime=recorder.mimeType||mime||'video/webm';console.info('ConfGTS recording started',ctx.mime);let active=recorder;recorder.onerror=e=>console.error('ConfGTS MediaRecorder error',e?.error||e);recorder.ondataavailable=e=>{if(e.data&&e.data.size>0){let part=recordPart++,chunk=e.data;ctx.bytes+=chunk.size;recordQueue=recordQueue.then(()=>uploadChunk(chunk,part,ctx)).catch(e=>console.warn('ConfGTS recording chunk failed',e))}};recorder.onstop=()=>{recordQueue=recordQueue.then(()=>api('/api/recordings/finalize',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({capture_id:ctx.capture})})).catch(e=>console.warn('ConfGTS recording finalize failed',e));if(ctx.bytes===0)console.error('ConfGTS recording produced no media chunks');if(audioCtx)audioCtx.close();audioCtx=null;audioDest=null;if(recorder===active)recorder=null;if(recordingContext===ctx)recordingContext=null};recorder.start(2000);drawRecording();setTimeout(()=>{try{if(active.state==='recording')active.requestData()}catch{}},1000)}
function drawRecording
'@
$startRecordingRegex = [regex]::new($startRecordingPattern)
if ($startRecordingRegex.IsMatch($ui)) {
    $ui = $startRecordingRegex.Replace($ui, $startRecordingReplacement.Trim(), 1)
} elseif (-not $ui.Contains('ConfGTS recording audio mix unavailable')) {
    throw 'ConfGTS startRecording patch was not applied.'
}

# Upload every chunk against the room/session/capture snapshot taken when the
# recorder starts. Retry transient failures so a short network hiccup does not
# silently remove the whole recording from the server archive.
$uploadChunkPattern = '(?s)async function uploadChunk\(blob,part\)\{.*?\}\r?\nasync function stopRecording'
$uploadChunkReplacement = @'
async function uploadChunk(blob,part,ctx=recordingContext){if(!ctx?.room||!ctx?.session||!ctx?.capture)throw new Error('recording context missing');let u='/api/recordings/upload?room='+encodeURIComponent(ctx.room)+'&session='+encodeURIComponent(ctx.session)+'&capture='+encodeURIComponent(ctx.capture)+'&part='+part;let lastError=null;for(let attempt=0;attempt<4;attempt++){try{let r=await fetch(u,{method:'POST',credentials:'same-origin',cache:'no-store',headers:{'Content-Type':blob.type||ctx.mime||'video/webm'},body:blob});if(r.ok)return;let detail='';try{detail=await r.text()}catch{}lastError=new Error(('record upload '+r.status+' '+detail).trim());if(r.status>=400&&r.status<500&&r.status!==403&&r.status!==408&&r.status!==429)break}catch(e){lastError=e}if(attempt<3)await new Promise(resolve=>setTimeout(resolve,250*Math.pow(2,attempt)))}throw lastError||new Error('record upload failed')}
async function stopRecording
'@
$uploadChunkRegex = [regex]::new($uploadChunkPattern)
if ($uploadChunkRegex.IsMatch($ui)) {
    $ui = $uploadChunkRegex.Replace($ui, $uploadChunkReplacement.TrimEnd("`r","`n"), 1)
} elseif (-not $ui.Contains('recording context missing')) {
    throw 'ConfGTS uploadChunk reliability patch was not applied.'
}

# A server-side Stop must end the active conference on every connected client.
$stateLoopPattern = '(?s)async function heartbeat\(\)\{.*?\}\r?\nasync function refreshRoomState\(\)\{.*?\}\r?\nfunction participantHTML'
$stateLoopReplacement = @'
async function handleConferenceStopped(){stopLoops(false);await stopRecording();for(let [k,p] of peers){try{p.close()}catch{}}peers.clear();document.querySelectorAll('.video-tile').forEach(x=>x.remove());roomState=null;$('#conference').style.display='none';$('#joinBtn').style.display='inline-flex';$('#leaveBtn').style.display='none';if(window.chrome?.webview){window.chrome.webview.postMessage('leave-conference')}else{await loadRooms()}}
async function heartbeat(){if(!currentRoom||!roomState)return;try{let s=await api('/api/rooms/'+currentRoom+'/heartbeat',{method:'POST'});if(!s.session){await handleConferenceStopped();return}roomState=s;updateStateUI();await reconcilePeers()}catch(e){console.warn(e)}}
async function refreshRoomState(){if(!currentRoom)return;try{let s=await api('/api/rooms/'+currentRoom+'/state');if(roomState){if(!s.session){await handleConferenceStopped();return}roomState=s;updateStateUI();await reconcilePeers()}else{let st={};st[currentRoom]={active:!!s.session,participants:(s.participants||[]).length};renderRooms(st);$('#previewParticipants').innerHTML=participantHTML(s.participants||[])}}catch(e){console.warn(e)}}
function participantHTML
'@
$stateLoopRegex = [regex]::new($stateLoopPattern)
if ($stateLoopRegex.IsMatch($ui)) {
    $ui = $stateLoopRegex.Replace($ui, $stateLoopReplacement.Trim(), 1)
} elseif (-not $ui.Contains("window.chrome?.webview.postMessage('leave-conference')")) {
    throw 'Server stop client-disconnect patch was not applied.'
}

# Reduce chunk duration so an unexpected process loss can only lose a small tail.
$ui = $ui.Replace('recorder.start(15000)', 'recorder.start(2000)').Replace('recorder.start(5000)', 'recorder.start(2000)')

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

# Keep the Participants tab on the server conference page live without forcing
# administrators to refresh the whole page. The poller re-renders only the tab
# counter and the participant table from the current server-side HTML snapshot.
$adminPath = Join-Path $root "server\admin.go"
if (Test-Path $adminPath) {
    $admin = Get-Content $adminPath -Raw -Encoding UTF8
    if (-not $admin.Contains('confgts-live-participants')) {
        $liveParticipantsScript = @'
<script id="confgts-live-participants">(function(){if(location.pathname!='/server/conferences')return;let busy=false;const findTab=root=>Array.from(root.querySelectorAll('a,button,[role="tab"]')).find(el=>/^Участники\s*\(\d+\)/i.test((el.textContent||'').trim()));const findTable=root=>Array.from(root.querySelectorAll('table')).find(t=>{let s=t.textContent||'';return s.includes('Пользователь')&&(s.includes('Вошёл')||s.includes('Вошел'))});async function refreshParticipants(){if(busy||document.hidden)return;busy=true;try{let r=await fetch(location.href,{credentials:'same-origin',cache:'no-store',headers:{'X-ConfGTS-Live':'participants'}});if(!r.ok)return;let next=new DOMParser().parseFromString(await r.text(),'text/html');let a=findTab(document),b=findTab(next);if(a&&b)a.textContent=b.textContent;let currentTable=findTable(document),nextTable=findTable(next);if(currentTable&&nextTable)currentTable.replaceWith(nextTable)}catch(e){console.debug('ConfGTS participant refresh',e)}finally{busy=false}}window.__confgtsParticipantsRefresh&&clearInterval(window.__confgtsParticipantsRefresh);window.__confgtsParticipantsRefresh=setInterval(refreshParticipants,1500);window.addEventListener('focus',refreshParticipants);refreshParticipants()})();</script>
'@
        $admin = $admin.Replace('</body></html>', $liveParticipantsScript.Trim() + '</body></html>')
        Set-Content $adminPath $admin -Encoding UTF8 -NoNewline
    }
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
        $text = $text.Replace("0.16.0", "0.18.10").Replace("0.16.1", "0.18.10").Replace("0.17.0", "0.18.10").Replace("0.18.7", "0.18.10").Replace("0.18.0", "0.18.10").Replace("0.18.1", "0.18.10").Replace("0.18.2", "0.18.10").Replace("0.18.0", "0.18.10").Replace("0.18.1", "0.18.10").Replace("0.18.2", "0.18.10")
        Set-Content $target $text -Encoding UTF8 -NoNewline
    } elseif (Test-Path $target -PathType Container) {
        Get-ChildItem $target -Recurse -File -Include *.go,*.cs,*.xaml,*.csproj,*.wxs,*.wixproj,*.ps1 | ForEach-Object {
            $text = Get-Content $_.FullName -Raw -Encoding UTF8
            if ($text.Contains("0.16.0") -or $text.Contains("0.16.1") -or $text.Contains("0.17.0") -or $text.Contains("0.18.0") -or $text.Contains("0.18.1") -or $text.Contains("0.18.2") -or $text.Contains("0.18.7")) {
                $text = $text.Replace("0.16.0", "0.18.10").Replace("0.16.1", "0.18.10").Replace("0.17.0", "0.18.10")
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
        $bundle = $bundle.Replace('          Version="0.18.10"', '          Version="0.18.10"' + [Environment]::NewLine + '          IconSourceFile="!(bindpath.assets)\ConfGTS.ico"')
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