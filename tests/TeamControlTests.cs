using System;
using System.Linq;
using Silksong.CoopAssist;

// The policy is Unity-free; only the key enum it reasons about is standing in here.
namespace UnityEngine
{
    public enum KeyCode : int
    {
        None=0, Backspace=8, Escape=27, Space=32, Alpha0=48, Alpha9=57, A=97, K=107, Z=122, UpArrow=273,
        F1=282,F2,F3,F4,F5,F6,F7,F8,F9,F10,F11,F12,F13,F14,F15,
        Mouse0=323, JoystickButton0=330
    }
}

static class TeamControlTests
{
    static int checks;
    static void Check(bool condition,string label)
    {checks++;if(!condition)throw new Exception(label);}
    static void Main()
    {
        var reserved=new[]{UnityEngine.KeyCode.F2,UnityEngine.KeyCode.F7,UnityEngine.KeyCode.F8};
        var defaults=new[]{UnityEngine.KeyCode.F5,UnityEngine.KeyCode.F6,UnityEngine.KeyCode.F9,UnityEngine.KeyCode.F10,UnityEngine.KeyCode.F12,UnityEngine.KeyCode.F11};
        Check(BindingKeyPolicy.CanUse(UnityEngine.KeyCode.K,reserved) && BindingKeyPolicy.CanUse(UnityEngine.KeyCode.Space,reserved) &&
            BindingKeyPolicy.CanUse(UnityEngine.KeyCode.UpArrow,reserved),"Ordinary keyboard keys outside F1-F12 are usable");
        Check(!BindingKeyPolicy.CanUse(UnityEngine.KeyCode.None,reserved) && !BindingKeyPolicy.CanUse(UnityEngine.KeyCode.Escape,reserved),
            "Unset keys and Escape stay refused");
        Check(!BindingKeyPolicy.CanUse(UnityEngine.KeyCode.F2,reserved) && !BindingKeyPolicy.CanUse(UnityEngine.KeyCode.F7,reserved) &&
            !BindingKeyPolicy.CanUse(UnityEngine.KeyCode.F8,reserved),"DebugMod, the panel and the escape key stay reserved");
        Check(!BindingKeyPolicy.CanUse(UnityEngine.KeyCode.Mouse0,reserved) &&
            !BindingKeyPolicy.CanUse(UnityEngine.KeyCode.JoystickButton0,reserved),"Mouse and pad buttons are not keyboard keys");
        var current=BindingKeyPolicy.Normalize(new[]{UnityEngine.KeyCode.None,UnityEngine.KeyCode.None,UnityEngine.KeyCode.F9,
            UnityEngine.KeyCode.F10,UnityEngine.KeyCode.F12,UnityEngine.KeyCode.F11},defaults,reserved);
        Check(current.SequenceEqual(defaults),"Empty bindings fall back to the shipped defaults");
        current=BindingKeyPolicy.Normalize(new[]{UnityEngine.KeyCode.None,UnityEngine.KeyCode.K,UnityEngine.KeyCode.F9,
            UnityEngine.KeyCode.F10,UnityEngine.KeyCode.F12,UnityEngine.KeyCode.F11},defaults,reserved);
        Check(current[0]==UnityEngine.KeyCode.F5 && current[1]==UnityEngine.KeyCode.K,"An existing letter binding survives migration");
        current=BindingKeyPolicy.Normalize(new[]{UnityEngine.KeyCode.F5,UnityEngine.KeyCode.F9,UnityEngine.KeyCode.None,
            UnityEngine.KeyCode.F10,UnityEngine.KeyCode.F12,UnityEngine.KeyCode.F11},defaults,reserved);
        Check(current[1]==UnityEngine.KeyCode.F9 && current[2]!=UnityEngine.KeyCode.F9,"Keep an existing F9 ahead of a new default");
        // The production list is seven rows and its last default is F1; a taken F1 must still yield an F key.
        var seven=new[]{UnityEngine.KeyCode.F1,UnityEngine.KeyCode.F6,UnityEngine.KeyCode.F9,UnityEngine.KeyCode.F10,
            UnityEngine.KeyCode.F12,UnityEngine.KeyCode.F11,UnityEngine.KeyCode.None};
        var sevenDefaults=new[]{UnityEngine.KeyCode.F5,UnityEngine.KeyCode.F6,UnityEngine.KeyCode.F9,UnityEngine.KeyCode.F10,
            UnityEngine.KeyCode.F12,UnityEngine.KeyCode.F11,UnityEngine.KeyCode.F1};
        var migration=BindingKeyPolicy.Normalize(seven,sevenDefaults,reserved);
        Check(migration.Take(6).SequenceEqual(seven.Take(6)),"A taken F1 keeps every existing binding");
        Check(migration[6]>=UnityEngine.KeyCode.F1 && migration[6]<=UnityEngine.KeyCode.F12,"A displaced default lands on a free F key, not Backspace");
        Check(migration.Distinct().Count()==7,"Seven actions never share a key");
        for(int i=0;i<current.Length;i++)
        {
            Check(BindingKeyPolicy.CanAssign(current[i],i,current,reserved),"Reassign same action is allowed");
            Check(!BindingKeyPolicy.CanAssign(current[(i+1)%current.Length],i,current,reserved),"Reject another action's key");
        }
        var random=new Random(92);
        var pool=new[]{UnityEngine.KeyCode.None,UnityEngine.KeyCode.Escape,UnityEngine.KeyCode.F2,UnityEngine.KeyCode.F7,
            UnityEngine.KeyCode.F8,UnityEngine.KeyCode.F5,UnityEngine.KeyCode.F6,UnityEngine.KeyCode.F9,UnityEngine.KeyCode.F10,
            UnityEngine.KeyCode.F11,UnityEngine.KeyCode.F12,UnityEngine.KeyCode.K,UnityEngine.KeyCode.Space,UnityEngine.KeyCode.Mouse0};
        for(int trial=0;trial<200;trial++)
        {
            var requested=Enumerable.Range(0,6).Select(_=>pool[random.Next(pool.Length)]).ToArray();
            var normalized=BindingKeyPolicy.Normalize(requested,defaults,reserved);
            Check(normalized.Distinct().Count()==6,"No duplicate hotkeys after migration");
            Check(normalized.All(n=>BindingKeyPolicy.CanUse(n,reserved)),"Every migrated key is usable");
            Check(normalized.SequenceEqual(BindingKeyPolicy.Normalize(normalized,defaults,reserved)),"Migration is idempotent");
        }
        var picker=new TravelSelection();
        picker.Refresh(new[]{42,7,19,7});
        Check(picker.Rows.SequenceEqual(new[]{-1,7,19,42,-2}),"Nearest, stable ordered peers, cancel");
        Check(picker.Selected==TravelSelection.Nearest,"Opening defaults to nearest without requesting travel");
        picker.Move(1);Check(picker.Selected==7,"Down selects first teammate");
        picker.Move(1);Check(picker.Selected==19,"Down selects next teammate");
        picker.Refresh(new[]{1,42,19});Check(picker.Selected==19,"Roster updates keep selected identity");
        picker.Refresh(new[]{1,42});Check(picker.Selected==-1,"Departure does not retarget a different named peer");
        Check(picker.ConfirmationBlocked,"Enter after a departure must not select nearest implicitly");
        picker.Refresh(new[]{1,42});Check(picker.ConfirmationBlocked,"Repeated roster refresh does not clear the departure guard");
        picker.Move(-1);Check(picker.Selected==TravelSelection.Cancel,"Up wraps to cancel");
        Check(!picker.ConfirmationBlocked,"Deliberate navigation enables confirmation");
        picker.Move(1);Check(picker.Selected==TravelSelection.Nearest,"Down wraps to nearest");
        picker.Select(42);picker.Refresh(Array.Empty<int>());
        Check(picker.Selected==-1 && picker.Rows.Count==2,"Empty room keeps nearest and cancellation");
        picker.Select(TravelSelection.Nearest);Check(!picker.ConfirmationBlocked,"Clicking nearest explicitly permits confirmation");
        for(int trial=0;trial<100;trial++)
        {
            picker.Refresh(Enumerable.Range(1,20).OrderBy(_=>random.Next()));picker.Select(13);
            picker.Refresh(Enumerable.Range(1,20).OrderBy(_=>random.Next()));
            Check(picker.Selected==13,"Roster order cannot change the chosen peer");
        }
        Check(TravelSelection.FindNearest(Array.Empty<(int,double)>())==-1,"No usable nearest target");
        Check(TravelSelection.FindNearest(new[]{(3,9d),(2,1d),(1,1d)})==1,"Nearest uses distance then deterministic id");
        Check(TravelSelection.FindNearest(new[]{(-1,0d),(2,double.NaN),(3,double.PositiveInfinity),(4,-1d),(5,0d)})==5,"Invalid positions are excluded");
        Check(TravelSelection.FindNearest(new[]{(18,64d),(42,4d)})==42,"Recompute nearest when teammates move");
        var teammates = new ushort[] { 42, 7, 19, 7 };
        Check(CocoonViewSelection.Move(null, 1, teammates) == 7,"Down from self selects the first teammate");
        Check(CocoonViewSelection.Move(null, -1, teammates) == 42,"Up from self selects the last teammate");
        Check(CocoonViewSelection.Move(7, 1, teammates) == 19,"Down advances through the roster");
        Check(CocoonViewSelection.Move(42, -1, teammates) == 19,"Up reverses through the roster");
        Check(CocoonViewSelection.Move(42, 1, teammates) == null &&
            CocoonViewSelection.Move(7, -1, teammates) == null,"Both directions can return to self");
        Check(CocoonViewSelection.Move(7, 1, Array.Empty<ushort>()) == null,"No living teammates leaves the local view");
        Console.WriteLine("Passed "+checks+" control migration, collision, keyboard selection, roster and nearest-distance checks.");
    }
}
