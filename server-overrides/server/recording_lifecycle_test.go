package main

import (
	"time"
	"os"
	"path/filepath"
	"testing"
)

func newRecordingTestStore(t *testing.T) (*Store, Room) {
	t.Helper()
	s, err := NewStore(t.TempDir())
	if err != nil {
		t.Fatalf("NewStore: %v", err)
	}
	rooms := s.Rooms()
	if len(rooms) == 0 {
		t.Fatal("expected at least one default conference room")
	}
	return s, rooms[0]
}

func createTestRecording(t *testing.T, s *Store, room Room, captureID string) (Recording, User) {
	t.Helper()
	u := User{Username: `TEPLO\recorder`, DisplayName: "Recorder"}
	session, _, recorder, err := s.Join(room.ID, u)
	if err != nil {
		t.Fatalf("Join: %v", err)
	}
	if session == nil {
		t.Fatal("Join returned nil session")
	}
	if recorder != "recorder" {
		t.Fatalf("recorder normalization mismatch: got %q", recorder)
	}

	rec, err := s.StartOrGetRecording(room.ID, session.ID, captureID, u.Username, "video/webm")
	if err != nil {
		t.Fatalf("StartOrGetRecording: %v", err)
	}
	if ext := filepath.Ext(rec.FileName); ext != ".mkv" {
		t.Fatalf("WebM fallback must be stored as MKV: got %q", rec.FileName)
	}
	if err := s.AppendRecording(rec, []byte("confgts-recording-test")); err != nil {
		t.Fatalf("AppendRecording: %v", err)
	}
	return rec, u
}

func assertRecordingFinalized(t *testing.T, s *Store, captureID string) {
	t.Helper()
	rec, ok := s.RecordingByCaptureID(captureID)
	if !ok {
		t.Fatalf("recording %q not found", captureID)
	}
	if rec.FinishedAt == nil {
		t.Fatalf("recording %q was not finalized", captureID)
	}
	if rec.SizeBytes == 0 {
		t.Fatalf("recording %q has zero persisted size", captureID)
	}
	path := s.RecordingPath(rec)
	if _, err := os.Stat(path); err != nil {
		t.Fatalf("recording file was not stored on server: %v", err)
	}
	if filepath.Dir(path) == "." {
		t.Fatalf("recording path is not server storage: %q", path)
	}
}

func TestRecordingFinalizedWhenLastParticipantLeaves(t *testing.T) {
	s, room := newRecordingTestStore(t)
	_, u := createTestRecording(t, s, room, "cap_leave_test")
	s.Leave(room.ID, u.Username)
	assertRecordingFinalized(t, s, "cap_leave_test")
}

func TestRecordingFinalizedWhenAdminStopsRoom(t *testing.T) {
	s, room := newRecordingTestStore(t)
	createTestRecording(t, s, room, "cap_admin_stop_test")
	if err := s.StopRoom(room.ID, "administrator"); err != nil {
		t.Fatalf("StopRoom: %v", err)
	}
	assertRecordingFinalized(t, s, "cap_admin_stop_test")

	session, participants, recorder := s.RoomStatus(room.ID)
	if session != nil || len(participants) != 0 || recorder != "" {
		t.Fatalf("room remained active after StopRoom: session=%v participants=%d recorder=%q", session, len(participants), recorder)
	}
}


func TestImmediateAdminStopAllowsStarterFinalChunk(t *testing.T) {
	s, room := newRecordingTestStore(t)
	u := User{Username: `TEPLO\recorder`, DisplayName: "Recorder"}
	session, _, recorder, err := s.Join(room.ID, u)
	if err != nil {
		t.Fatalf("Join: %v", err)
	}
	if session == nil || recorder != "recorder" {
		t.Fatalf("unexpected session/recorder: session=%v recorder=%q", session, recorder)
	}
	if err := s.StopRoom(room.ID, "administrator"); err != nil {
		t.Fatalf("StopRoom: %v", err)
	}
	if !s.CanUploadEndedSessionRecording(session.ID, u.Username, 30*time.Second) {
		t.Fatal("session starter was not allowed to upload the final chunk after immediate admin stop")
	}
}

func TestRecordingUsesMP4Extension(t *testing.T) {
	s, room := newRecordingTestStore(t)
	u := User{Username: `TEPLO\recorder`, DisplayName: "Recorder"}
	session, _, _, err := s.Join(room.ID, u)
	if err != nil {
		t.Fatalf("Join: %v", err)
	}
	rec, err := s.StartOrGetRecording(room.ID, session.ID, "cap_mp4_test", u.Username, "video/mp4;codecs=avc1.42E01E,mp4a.40.2")
	if err != nil {
		t.Fatalf("StartOrGetRecording: %v", err)
	}
	if ext := filepath.Ext(rec.FileName); ext != ".mp4" {
		t.Fatalf("MP4 recording must use .mp4 extension: got %q", rec.FileName)
	}
	if err := s.AppendRecording(rec, []byte("mp4-media-bytes")); err != nil {
		t.Fatalf("AppendRecording: %v", err)
	}
	stored, ok := s.RecordingByCaptureID("cap_mp4_test")
	if !ok || stored.SizeBytes == 0 {
		t.Fatalf("MP4 recording bytes were not persisted: ok=%v size=%d", ok, stored.SizeBytes)
	}
}


func TestRecordingUsesConfiguredDirectory(t *testing.T) {
	s, room := newRecordingTestStore(t)
	customDir := filepath.Join(t.TempDir(), "conference-recordings")
	cfg := s.Config()
	cfg.RecordingDir = customDir
	if err := s.SaveConfig(cfg); err != nil {
		t.Fatalf("SaveConfig custom recording dir: %v", err)
	}

	rec, _ := createTestRecording(t, s, room, "cap_custom_dir_test")
	path := s.RecordingPath(rec)
	if filepath.Clean(filepath.Dir(path)) != filepath.Clean(customDir) {
		t.Fatalf("recording stored in wrong directory: got %q want %q", filepath.Dir(path), customDir)
	}
	if _, err := os.Stat(path); err != nil {
		t.Fatalf("recording was not written to configured directory: %v", err)
	}
}
