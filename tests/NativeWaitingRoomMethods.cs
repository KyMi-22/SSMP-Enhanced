// SSMP 0.4.142 e5623d5 ConnectInterface.cs waiting-room methods; UI/network boundaries simulated.
// Source SHA256: dde0c401f6f90b755afa660441b047b06c6ddd0412f0c592c9553862f69a3efe
using System;
using System.Collections.Generic;
partial class Room {
private void ShowHostWaitingRoom(WaitingRoomEntry entry, string entryDetail) {
        OpenWaitingRoom(entry, entryDetail, [_usernameInput.GetInput()]);
    }
public void ShowJoinWaitingRoom(IReadOnlyList<string> others) {
        var players = new List<string> { _usernameInput.GetInput() };
        players.AddRange(others);
        OpenWaitingRoom(WaitingRoomEntry.Joined, "", players);
    }
private void OpenWaitingRoom(WaitingRoomEntry entry, string entryDetail, List<string> players) {
        _waitingRoomActive = true;
        _waitingRoomEntry = entry;
        _waitingRoomEntryDetail = entryDetail;
        _waitingRoomLocalReady = false;
        _waitingRoomProceeded = false;
        _waitingRoomReadyNames.Clear();

        _waitingRoomPlayers.Clear();
        _waitingRoomPlayers.AddRange(players);

        ShowRoomTab();
        RefreshWaitingRoom();
    }
private void RefreshWaitingRoom() {
        if (!_waitingRoomActive) {
            return;
        }

        // By position, not by name: the local player is always put in first, and two players who happened to pick the
        // same name would otherwise both be read as the local one, so one of them pressing ready would count for both
        var members = new List<WaitingRoomMember>();
        for (var i = 0; i < _waitingRoomPlayers.Count; i++) {
            var name = _waitingRoomPlayers[i];
            members.Add(
                new WaitingRoomMember(name, i == 0 ? _waitingRoomLocalReady : _waitingRoomReadyNames.Contains(name))
            );
        }

        _waitingRoomPanel.SetRoom(members, _waitingRoomLocalReady, _waitingRoomEntry, _waitingRoomEntryDetail);

        if (_waitingRoomProceeded || members.Count < 2) {
            return;
        }

        foreach (var member in members) {
            if (!member.Ready) {
                return;
            }
        }

        _waitingRoomProceeded = true;
        if (WaitingRoomHosting) {
            // Put away rather than torn down, because backing out of the save menu has to come back here: the game
            // is still hosted and the teammate is still connected at that point
            SuspendWaitingRoom();
            HostSaveSelectionRequested?.Invoke();
        } else {
            HideWaitingRoom();
            JoinSaveSelectionRequested?.Invoke();
        }
    }
public void OnPartnerReadyChanged(string username, bool ready) {
        if (!_waitingRoomActive) {
            return;
        }

        if (ready) {
            _waitingRoomReadyNames.Add(username);
        } else {
            _waitingRoomReadyNames.Remove(username);
        }

        RefreshWaitingRoom();
    }
private void HideWaitingRoom() {
        _waitingRoomActive = false;
        _waitingRoomEntry = WaitingRoomEntry.Joined;
        _waitingRoomEntryDetail = "";
        _waitingRoomLocalReady = false;
        _waitingRoomProceeded = false;
        _waitingRoomLobbyId = default;
        _waitingRoomPlayers.Clear();
        _waitingRoomReadyNames.Clear();
        _waitingRoomPanel.Hide();

        // Whatever the open tab shows without a room comes back, however the room closed: the room had hidden it
        SwitchTab(_activeTab);
    }
private void SuspendWaitingRoom() {
        _waitingRoomPanel.Hide();
    }
public void OnHostSaveSelectionClosed() {
        if (!_waitingRoomActive) {
            return;
        }

        _waitingRoomProceeded = false;

        // Backing out of the save menu means the host is not ready after all. Without this the room would see both
        // players still ready the moment it reappeared and send the host straight back into the save menu, over and
        // over, with no way to return here.
        if (_waitingRoomLocalReady) {
            _waitingRoomLocalReady = false;
            WaitingRoomReadyToggled?.Invoke(false);
        }

        ShowRoomTab();
        RefreshWaitingRoom();
    }
public void OnHostSaveLoaded() {
        if (_waitingRoomActive) {
            HideWaitingRoom();
        }
    }
public void OnPlayerJoined(string username) {
        // Past the room, choosing a save or playing, so ready for whoever joins now, who waits in a room of their own
        // for everyone in it to be ready: they waited for this player forever, as one who joined again after dropping
        // out did (USER 10-25: "如果有三个玩家，两个玩家先点准备，进游戏了，第三个玩家就会卡住吗？")
        if (!_waitingRoomActive) {
            WaitingRoomReadyToggled?.Invoke(true);
            return;
        }

        if (_waitingRoomPlayers.Contains(username)) {
            return;
        }

        _waitingRoomPlayers.Add(username);

        // Someone came, so whatever Steam said of friends to invite is past
        HideNoInviteNote();

        // A ready said before they came reached nobody, so they hear it now: a host who readied while alone was never
        // ready for the teammate who joined, whose save menu then never opened
        if (_waitingRoomLocalReady) {
            WaitingRoomReadyToggled?.Invoke(true);
        }

        RefreshWaitingRoom();
    }
public void OnPlayerLeft(string username) {
        if (!_waitingRoomActive || !_waitingRoomPlayers.Remove(username)) {
            return;
        }

        // Their readiness leaves with them, or a player who readied up and then dropped would still be counted
        _waitingRoomReadyNames.Remove(username);
        RefreshWaitingRoom();
    }
}
