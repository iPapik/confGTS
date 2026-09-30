package main

import (
	"fmt"
	"log"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"
)

// OptimizeRecording remuxes the completed browser MediaRecorder stream without
// re-encoding. ffmpeg rebuilds MP4/MKV container indexes and duration metadata,
// which makes downloaded recordings seekable in normal desktop players.
func (s *Store) OptimizeRecording(captureID string) {
	rec, ok := s.RecordingByCaptureID(captureID)
	if !ok || rec.FinishedAt == nil {
		return
	}
	s.optimizeRecordingFile(rec)
}

// OptimizeRecordingByID is used as a lazy safety net before viewing/downloading
// an older completed recording that has not been remuxed yet.
func (s *Store) OptimizeRecordingByID(id string) {
	rec, ok := s.RecordingByID(id)
	if !ok || rec.FinishedAt == nil {
		return
	}
	s.optimizeRecordingFile(rec)
}

func (s *Store) optimizeRecordingFile(rec Recording) {
	path := s.RecordingPath(rec)
	st, err := os.Stat(path)
	if err != nil || st.Size() == 0 {
		return
	}

	// Share the same per-recording mutex with AppendRecording so a late final
	// chunk and container remux can never modify the file at the same time.
	s.mu.Lock()
	lk := s.recLocks[rec.ID]
	if lk == nil {
		lk = &sync.Mutex{}
		s.recLocks[rec.ID] = lk
	}
	s.mu.Unlock()

	lk.Lock()
	defer lk.Unlock()

	st, err = os.Stat(path)
	if err != nil || st.Size() == 0 {
		return
	}
	marker := path + ".seekable"
	if markerInfo, markerErr := os.Stat(marker); markerErr == nil && !markerInfo.ModTime().Before(st.ModTime()) {
		return
	}

	ffmpeg, err := findFFmpeg()
	if err != nil {
		log.Printf("ConfGTS recording remux skipped: %v", err)
		return
	}

	ext := strings.ToLower(filepath.Ext(path))
	if ext != ".mp4" && ext != ".mkv" && ext != ".webm" {
		log.Printf("ConfGTS recording remux skipped for unsupported extension %q", ext)
		return
	}

	tmp := strings.TrimSuffix(path, ext) + ".remux" + ext
	backup := path + ".pre-remux"
	_ = os.Remove(tmp)
	_ = os.Remove(backup)

	args := []string{"-hide_banner", "-loglevel", "error", "-y", "-fflags", "+genpts", "-i", path, "-map", "0", "-c", "copy"}
	if ext == ".mp4" {
		args = append(args, "-movflags", "+faststart")
	}
	args = append(args, tmp)

	cmd := exec.Command(ffmpeg, args...)
	if output, runErr := cmd.CombinedOutput(); runErr != nil {
		_ = os.Remove(tmp)
		log.Printf("ConfGTS recording remux failed for %s: %v: %s", filepath.Base(path), runErr, strings.TrimSpace(string(output)))
		return
	}

	if err := os.Rename(path, backup); err != nil {
		_ = os.Remove(tmp)
		log.Printf("ConfGTS recording remux could not move source %s: %v", filepath.Base(path), err)
		return
	}
	if err := os.Rename(tmp, path); err != nil {
		_ = os.Rename(backup, path)
		_ = os.Remove(tmp)
		log.Printf("ConfGTS recording remux could not install final file %s: %v", filepath.Base(path), err)
		return
	}
	_ = os.Remove(backup)
	_ = os.WriteFile(marker, []byte(time.Now().UTC().Format(time.RFC3339Nano)), 0644)

	if finalInfo, statErr := os.Stat(path); statErr == nil {
		s.mu.Lock()
		for i := range s.state.Recordings {
			if s.state.Recordings[i].ID == rec.ID {
				s.state.Recordings[i].SizeBytes = finalInfo.Size()
				break
			}
		}
		_ = s.saveStateLocked()
		s.mu.Unlock()
	}

	log.Printf("ConfGTS recording remuxed for seeking: %s", filepath.Base(path))
}

func findFFmpeg() (string, error) {
	exe, err := os.Executable()
	if err == nil {
		local := filepath.Join(filepath.Dir(exe), "ffmpeg.exe")
		if _, statErr := os.Stat(local); statErr == nil {
			return local, nil
		}
	}

	if path, lookErr := exec.LookPath("ffmpeg.exe"); lookErr == nil {
		return path, nil
	}
	if path, lookErr := exec.LookPath("ffmpeg"); lookErr == nil {
		return path, nil
	}
	return "", fmt.Errorf("ffmpeg executable was not found")
}
