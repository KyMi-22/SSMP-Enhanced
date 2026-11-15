using System;
using System.Collections.Generic;
using System.Linq;

enum WaitingRoomEntry { Joined, SteamLobby, LobbyId, DirectIp }
readonly struct WaitingRoomMember
{
    public readonly string Name; public readonly bool Ready;
    public WaitingRoomMember(string name,bool ready){Name=name;Ready=ready;}
}
sealed class Panel
{
    public bool Visible;
    public void SetRoom(List<WaitingRoomMember> members,bool ready,WaitingRoomEntry entry,string detail){Visible=true;}
    public void Hide(){Visible=false;}
}
sealed class NameInput { public string Name; public string GetInput()=>Name; }
partial class Room
{
    public readonly string Name;
    bool _waitingRoomActive,_waitingRoomLocalReady,_waitingRoomProceeded;
    WaitingRoomEntry _waitingRoomEntry;
    string _waitingRoomEntryDetail="";
    int _waitingRoomLobbyId,_activeTab;
    readonly List<string> _waitingRoomPlayers=new List<string>();
    readonly HashSet<string> _waitingRoomReadyNames=new HashSet<string>();
    readonly Panel _waitingRoomPanel=new Panel();
    readonly NameInput _usernameInput;
    bool WaitingRoomHosting=>_waitingRoomEntry!=WaitingRoomEntry.Joined;
    public event Action HostSaveSelectionRequested,JoinSaveSelectionRequested;
    public event Action<bool> WaitingRoomReadyToggled;
    public int SaveSelections,ReadyBroadcasts;
    bool saveMenuOpen;
    public bool CanPressReady=>_waitingRoomPanel.Visible && !saveMenuOpen;
    public Room(string name)
    {
        Name=name;_usernameInput=new NameInput{Name=name};
        HostSaveSelectionRequested+=()=>{SaveSelections++;saveMenuOpen=true;};
        JoinSaveSelectionRequested+=()=>{SaveSelections++;saveMenuOpen=true;};
    }
    public void Open(bool host,IReadOnlyList<string> others)
    {if(host){ShowHostWaitingRoom(WaitingRoomEntry.DirectIp,"26950");foreach(var other in others)OnPlayerJoined(other);}else ShowJoinWaitingRoom(others);}
    public void PressReady()
    {
        if(!CanPressReady)throw new Exception("Ready button no longer accessible");
        // The exact three statements of the upstream WaitingRoomPanel.SetOnReady callback.
        _waitingRoomLocalReady=!_waitingRoomLocalReady;
        WaitingRoomReadyToggled?.Invoke(_waitingRoomLocalReady);
        RefreshWaitingRoom();
    }
    public void OldBypass(){SaveSelections++;saveMenuOpen=true;}
    void ShowRoomTab(){saveMenuOpen=false;} void HideNoInviteNote(){} void SwitchTab(int tab){}
}
sealed class Hub
{
    public readonly List<Room> Rooms=new List<Room>();
    public Room Add(string name)
    {
        var r=new Room(name);Rooms.Add(r);
        r.WaitingRoomReadyToggled+=ready=>{
            r.ReadyBroadcasts++;
            foreach(var other in Rooms.ToArray())if(other!=r)other.OnPartnerReadyChanged(r.Name,ready);
        };
        return r;
    }
    public static Hub Create(int count)
    {
        var hub=new Hub();for(int i=0;i<count;i++)hub.Add(i==0?"Host":"Peer"+i);
        foreach(var r in hub.Rooms)r.Open(r==hub.Rooms[0],hub.Rooms.Where(p=>p!=r).Select(p=>p.Name).ToArray());
        return hub;
    }
}
static class LobbyReadyTests
{
    static int checks;
    static void Check(bool condition,string why){checks++;if(!condition)throw new Exception(why);}
    static void Counts(Hub hub,int n,string why){foreach(var r in hub.Rooms)Check(r.SaveSelections==n,why+" "+r.Name);}
    static int Main()
    {
        try{Run();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    static void Run()
    {
        foreach(var order in new[]{new[]{0,1},new[]{1,0}})
        {
            var hub=Hub.Create(2);Counts(hub,0,"connect does not open any save menu");
            Check(hub.Rooms.All(r=>r.CanPressReady),"both players retain ready buttons");
            hub.Rooms[order[0]].PressReady();Counts(hub,0,"only one ready cannot advance");
            hub.Rooms[order[1]].PressReady();Counts(hub,1,"both ready advances both sides exactly once");
            foreach(var r in hub.Rooms)foreach(var p in hub.Rooms.Where(p=>p!=r))r.OnPartnerReadyChanged(p.Name,true);
            Counts(hub,1,"replayed ready messages do not reopen save menus");
        }
        foreach(var order in new[]{new[]{0,1,2},new[]{0,2,1},new[]{1,0,2},new[]{1,2,0},new[]{2,0,1},new[]{2,1,0}})
        {
            var hub=Hub.Create(3);
            hub.Rooms[order[0]].PressReady();hub.Rooms[order[1]].PressReady();Counts(hub,0,"third player not ready");
            hub.Rooms[order[2]].PressReady();Counts(hub,1,"all three advance independently of ready order");
        }
        {
            var hub=Hub.Create(3);hub.Rooms[0].PressReady();hub.Rooms[1].PressReady();hub.Rooms[1].PressReady();hub.Rooms[2].PressReady();
            Counts(hub,0,"cancelled readiness must be respected");hub.Rooms[1].PressReady();Counts(hub,1,"re-ready advances all sides");
        }
        {
            var hub=Hub.Create(3);hub.Rooms[0].PressReady();hub.Rooms[1].PressReady();var departed=hub.Rooms[2];hub.Rooms.Remove(departed);
            foreach(var r in hub.Rooms)r.OnPlayerLeft(departed.Name);
            Counts(hub,1,"unready departed member does not block remaining ready players");
        }
        {
            var hub=Hub.Create(2);hub.Rooms[0].PressReady();hub.Rooms[1].PressReady();hub.Rooms[0].OnHostSaveLoaded();
            var late=hub.Add("LatePeer");late.Open(false,new[]{"Host","Peer1"});
            foreach(var r in hub.Rooms.Where(r=>r!=late))r.OnPlayerJoined(late.Name);
            Check(late.SaveSelections==0 && late.CanPressReady,"late joiner still chooses readiness");
            late.PressReady();Counts(hub,1,"in-game peers announce readiness; late joiner advances without reopening others");
        }
        {
            var hub=Hub.Create(2);hub.Rooms[0].PressReady();hub.Rooms[1].PressReady();var host=hub.Rooms[0];
            host.OnHostSaveSelectionClosed();Check(host.CanPressReady && host.SaveSelections==1,"back from host save menu returns to ready button");
            host.PressReady();Check(host.SaveSelections==2 && hub.Rooms[1].SaveSelections==1,"host can ready again without reopening peer save selection");
        }
        {
            var hub=Hub.Create(2);hub.Rooms[1].OldBypass();hub.Rooms[0].PressReady();
            Check(hub.Rooms[1].SaveSelections==1 && !hub.Rooms[1].CanPressReady,"reproduce old client-only bypass");
            Check(hub.Rooms[0].SaveSelections==0 && hub.Rooms[1].ReadyBroadcasts==0,"old bypass leaves host blocked waiting for an unsent ready message");
        }
        Console.WriteLine("Passed "+checks+" lobby checks using pinned upstream waiting-room method bodies: two/three players, ready order/cancel/replay/departure/late join/back, old-bypass reproduction. UI/network simulated; no Unity or sockets.");
    }
}
