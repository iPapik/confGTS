package main

import (
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
