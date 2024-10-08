using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Reloaded.Hooks.Definitions;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;

using RNSReloaded.Interfaces;
using RNSReloaded.Interfaces.Structs;

/*
    🐀   🐀
🐀           🐀
  "TO ME, MY ALLIES!"
      🎷🐁
🐀           🐀
      🐀
*/

namespace RNSReloaded.Fullmetal;

public unsafe class Mod : IMod {
    private const int SCRIPTCONST = 100000;

    private WeakReference<IRNSReloaded>? rnsReloadedRef;
    private WeakReference<IReloadedHooks>? hooksRef;
    private ILoggerV1 logger = null!;
    private Random hallRand = new();

    private string[] hallkeys = ["hw_nest", "hw_arsenal", "hw_lighthouse", "hw_streets", "hw_lakeside"];
    private List<Notch>[] hallNotches = [];
    private int hallCount = 0;
    private int notchesCount = 0;

    private CInstance* potionSelf;
    private CInstance* potionOther;
    private bool potionStored = false;

    private static Dictionary<string, IHook<ScriptDelegate>> ScriptHooks = new();

    public void Start(IModLoaderV1 loader) {
        this.rnsReloadedRef = loader.GetController<IRNSReloaded>();
        this.hooksRef = loader.GetController<IReloadedHooks>()!;
        this.logger = loader.GetLogger();

        if (this.rnsReloadedRef.TryGetTarget(out IRNSReloaded? rnsReloaded)) {
            rnsReloaded.OnReady += this.Ready;
        }
    }

    public void Ready() {
        if (
            this.rnsReloadedRef != null
            && this.rnsReloadedRef.TryGetTarget(out IRNSReloaded? rnsReloaded)
        ) {
            rnsReloaded.LimitOnlinePlay();

            this.InitializeHooks();
        }
    }

    private bool IsReady(
        [MaybeNullWhen(false), NotNullWhen(true)] out IRNSReloaded rnsReloaded,
        [MaybeNullWhen(false), NotNullWhen(true)] out IReloadedHooks hooks,
        [MaybeNullWhen(false), NotNullWhen(true)] out IUtil utils,
        [MaybeNullWhen(false), NotNullWhen(true)] out IBattleScripts scrbp,
        [MaybeNullWhen(false), NotNullWhen(true)] out IBattlePatterns bp
    ) {
        if (
            this.rnsReloadedRef != null
            && this.rnsReloadedRef.TryGetTarget(out rnsReloaded)
            && this.hooksRef != null
            && this.hooksRef.TryGetTarget(out hooks)
        ) {
            utils = rnsReloaded.utils;
            scrbp = rnsReloaded.battleScripts;
            bp = rnsReloaded.battlePatterns;
            return rnsReloaded != null;
        }
        rnsReloaded = null;
        hooks = null;
        utils = null;
        scrbp = null;
        bp = null;
        return false;
    }

    private void CreateAndEnableHook(string scriptName, ScriptDelegate detour, out IHook<ScriptDelegate>? hook) {
        if (this.IsReady(out var rnsReloaded, out var hooks, out var utils, out var scrbp, out var bp)) {
            CScript* script = rnsReloaded.GetScriptData(rnsReloaded.ScriptFindId(scriptName) - SCRIPTCONST);
            hook = hooks.CreateHook(detour, script->Functions->Function);
            hook.Activate();
            hook.Enable();
            return;
        }
        hook = null;
    }

    public void InitializeHooks() {
        var detourMap = new Dictionary<string, ScriptDelegate>{
            { "scr_hallwaygen_outskirts", this.OutskirtsDetour},
            { "scr_hallwaygen_outskirts_n", this.OutskirtsDetour},
            { "scr_hallwaygen_nest", this.MakeMainAreaDetour("scr_hallwaygen_nest")},
            { "scr_hallwaygen_arsenal", this.MakeMainAreaDetour("scr_hallwaygen_arsenal")},
            { "scr_hallwaygen_lighthouse", this.MakeMainAreaDetour("scr_hallwaygen_lighthouse")},
            { "scr_hallwaygen_streets", this.MakeMainAreaDetour("scr_hallwaygen_streets")},
            { "scr_hallwaygen_lakeside", this.MakeMainAreaDetour("scr_hallwaygen_lakeside")},
            { "scr_hallwaygen_keep", this.KeepDetour},
            { "scr_hallwaygen_pinnacle", this.PinnacleDetour},
            { "scr_hallwayprogress_choose_halls", this.ChooseHallsDetour},
            { "scr_hallwayprogress_start_hallway", this.StartHallwayDetour},
            { "scr_hallwayprogress_move_next", this.MoveNextDetour},
            { "scr_rankbar_give_rewards", this.GiveRewardsDetour},
            { "scr_itemsys_erase_potions", this.ErasePotionsDetour}
        };

        foreach (var detourPair in detourMap) {
            this.CreateAndEnableHook(detourPair.Key, detourPair.Value, out var hook);
            if (hook != null) {
                ScriptHooks[detourPair.Key] = hook;
            }
        }
    }

    private int getRandInt() => this.hallRand.Next(0, maxValue: int.MaxValue);

    private string[] EncFromKey(string key) {
        // returns list of encounters based on area
        string[] enc0 = [];
        string[] enc1 = [];
        string[] enc2 = [];
        switch (key) {
            case "hw_nest":
                enc0 = [ "enc_bird_student0", "enc_bird_student1" ];
                enc1 = ["enc_bird_whispering0", "enc_bird_whispering1"];
                enc2 = ["enc_bird_archon0", "enc_bird_valedictorian0"];
                break;
            case "hw_arsenal":
                enc0 = ["enc_wolf_greyeye0", "enc_wolf_greyeye1"];
                enc1 = ["enc_wolf_bluepaw0", "enc_wolf_bluepaw1"];
                enc2 = ["enc_wolf_snowfur0", "enc_wolf_steeltooth0"];
                break;
            case "hw_lighthouse":
                enc0 = ["enc_dragon_gold0", "enc_dragon_gold1"];
                enc1 = ["enc_dragon_emerald0", "enc_dragon_emerald1"];
                enc2 = ["enc_dragon_ruby0", "enc_dragon_mythril0"];
                break;
            case "hw_streets":
                enc0 = ["enc_mouse_archer0", "enc_mouse_archer1"];
                enc1 = ["enc_mouse_oakspear0", "enc_mouse_oakspear1"];
                enc2 = ["enc_mouse_rosemage0", "enc_mouse_paladin0"];
                break;
            case "hw_lakeside":
                enc0 = ["enc_frog_seamstress0", "enc_frog_seamstress1"];
                enc1 = ["enc_frog_songstress0", "enc_frog_songstress1"];
                enc2 = ["enc_frog_painter0", "enc_frog_idol0"];
                break;
        }
        this.hallRand.Shuffle(enc0);
        this.hallRand.Shuffle(enc1);

        return enc0.Concat(enc1).Concat(enc2).ToArray();
    }

    private List<Notch>[] CreateNotches(string[] hallkeys) {
        string[][] hallEnc = hallkeys.Select(key => this.EncFromKey(key)).ToArray();
        List<Notch> notches0 = [ // first and second area (first hall)
            new(NotchType.Shop, "", this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[0][0], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[0][1], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[0][2], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[0][3], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[0][4], this.getRandInt(), 0),
            new(NotchType.Chest, "", this.getRandInt(), 0),
            new(NotchType.Boss, hallEnc[0][5], 0, Notch.BOSS_FLAG),
            new(NotchType.Shop, "", 0, 0),
            new(NotchType.Encounter, hallEnc[1][0], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[1][1], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[1][2], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[1][3], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[1][4], this.getRandInt(), 0),
            new(NotchType.Boss, hallEnc[1][5], this.getRandInt(), Notch.BOSS_FLAG)
        ];
        List<Notch> notches1 = [ // third and fourth area (second hall)
            new(NotchType.Shop, "", this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[2][0], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[2][1], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[2][2], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[2][3], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[2][4], this.getRandInt(), 0),
            new(NotchType.Chest, "", this.getRandInt(), 0),
            new(NotchType.Boss, hallEnc[2][5], this.getRandInt(), Notch.BOSS_FLAG),
            new(NotchType.Shop, "", this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[3][0], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[3][1], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[3][2], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[3][3], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[3][4], this.getRandInt(), 0),
            new(NotchType.Boss, hallEnc[3][5], this.getRandInt(), Notch.BOSS_FLAG)
        ];
        List<Notch> notches2 = [ // fifth area (third hall)
            new(NotchType.Shop, "", this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[4][0], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[4][1], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[4][2], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[4][3], this.getRandInt(), 0),
            new(NotchType.Encounter, hallEnc[4][4], this.getRandInt(), 0),
            new(NotchType.Chest, "", this.getRandInt(), 0),
            new(NotchType.Boss, hallEnc[4][5], this.getRandInt(), Notch.BOSS_FLAG)
        ];

        return [notches0, notches1, notches2];
    }

    private List<Notch> CreateOutskirtsNotches() {
        // randomizes encounter pairs
        string[] encOutskirts0 = ["enc_bird_sophomore1", "enc_bird_sophomore2"];
        string[] encOutskirts1 = ["enc_wolf_blackear1", "enc_wolf_blackear2"];
        string[] encOutskirts2 = ["enc_dragon_granite1", "enc_dragon_granite2"];
        string[] encOutskirts3 = ["enc_mouse_cadet1", "enc_mouse_cadet2"];
        string[] encOutskirts4 = ["enc_frog_tinkerer1", "enc_frog_tinkerer2"];
        this.hallRand.Shuffle(encOutskirts0);
        this.hallRand.Shuffle(encOutskirts1);
        this.hallRand.Shuffle(encOutskirts2);
        this.hallRand.Shuffle(encOutskirts3);
        this.hallRand.Shuffle(encOutskirts4);

        // randomizes order of pairs
        string[][] encOutskirts = [encOutskirts0, encOutskirts1, encOutskirts2, encOutskirts3, encOutskirts4];
        this.hallRand.Shuffle(encOutskirts);

        // creates notches for encounters
        return [
            new(NotchType.IntroRoom, "", this.getRandInt(), 0),
            new(NotchType.Encounter, encOutskirts[0][0], this.getRandInt(), 0),
            new(NotchType.Encounter, encOutskirts[0][1], this.getRandInt(), 0),
            new(NotchType.Chest, "", this.getRandInt(), 0),
            new(NotchType.Encounter, encOutskirts[1][0], this.getRandInt(), 0),
            new(NotchType.Encounter, encOutskirts[1][1], this.getRandInt(), 0),
            new(NotchType.Encounter, encOutskirts[2][0], this.getRandInt(), 0),
            new(NotchType.Encounter, encOutskirts[2][1], this.getRandInt(), 0),
            new(NotchType.Chest, "", this.getRandInt(), 0),
            new(NotchType.Encounter, encOutskirts[3][0], this.getRandInt(), 0),
            new(NotchType.Encounter, encOutskirts[3][1], this.getRandInt(), 0),
            new(NotchType.Encounter, encOutskirts[4][0], this.getRandInt(), 0),
            new(NotchType.Encounter, encOutskirts[4][1], this.getRandInt(), 0),
        ];
    }

    private List<Notch> CreateKeepNotches() {
        // randomizes keep encounters
        string[] encKeep = [
            "enc_queens_axe0",
            "enc_queens_harp0",
            "enc_queens_knife0",
            "enc_queens_spear0",
            "enc_queens_staff0"
        ];
        this.hallRand.Shuffle(encKeep);

        // creates notches for encounters
        return [
            new(NotchType.Shop, "", this.getRandInt(), 0),
            new(NotchType.Encounter, encKeep[0], this.getRandInt(), 0),
            new(NotchType.Encounter, encKeep[1], this.getRandInt(), 0),
            new(NotchType.Encounter, encKeep[2], this.getRandInt(), 0),
            new(NotchType.Encounter, encKeep[3], this.getRandInt(), 0),
            new(NotchType.Encounter, encKeep[4], this.getRandInt(), 0),
            new(NotchType.Chest, "", this.getRandInt(), 0),
        ];
    }

    private RValue* ChooseHallsDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scr_hallwayprogress_choose_halls"];
        if (this.IsReady(out var rnsReloaded, out var hooks, out var utils, out var scrbp, out var bp)) {
            returnValue = hook!.OriginalFunction(self, other, returnValue, argc, argv);
            // resets tracking variables
            this.potionStored = false;
            this.hallCount = -1;
            
            // check if Toybox is on
            RValue* hallkey = rnsReloaded.FindValue(self, "hallkey");
            RValue hallkeyLength = rnsReloaded.ArrayGetLength(hallkey) ?? new RValue(0);
            if (utils.RValueToLong(&hallkeyLength) != 1) {
                // randomizes hallkeys based on seed from host
                RValue* mapSeedR = rnsReloaded.FindValue(rnsReloaded.GetGlobalInstance(), "mapSeed");
                long mapSeed = utils.RValueToLong(mapSeedR); // mapSeed is a different datatype for host/client

                string firstArea = hallkey->Get(1)->ToString();
                this.hallkeys = ["hw_nest", "hw_arsenal", "hw_lighthouse", "hw_streets", "hw_lakeside"];

                this.hallRand = new Random((int) mapSeed);
                this.hallRand.Shuffle(this.hallkeys);

                int index = Array.IndexOf(this.hallkeys, firstArea);
                if (index > 0) {
                    // Swap the first element with the element at the found index
                    string temp = this.hallkeys[0];
                    this.hallkeys[0] = this.hallkeys[index];
                    this.hallkeys[index] = temp;
                }

                // determines notches for every area
                this.hallNotches = this.CreateNotches(this.hallkeys);

                // sets hallkeys in game
                rnsReloaded.CreateString(rnsReloaded.ArrayGetEntry(hallkey, 0), "hw_outskirts");
                rnsReloaded.CreateString(rnsReloaded.ArrayGetEntry(hallkey, 1), this.hallkeys[0]);
                rnsReloaded.CreateString(rnsReloaded.ArrayGetEntry(hallkey, 2), this.hallkeys[2]);
                rnsReloaded.CreateString(rnsReloaded.ArrayGetEntry(hallkey, 3), this.hallkeys[4]);
                rnsReloaded.CreateString(rnsReloaded.ArrayGetEntry(hallkey, 4), "hw_keep");
                rnsReloaded.CreateString(rnsReloaded.ArrayGetEntry(hallkey, 5), "hw_pinnacle");
            }
        }
        return returnValue;
    }

    private RValue* StartHallwayDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scr_hallwayprogress_start_hallway"];
        this.notchesCount = 0;
        return hook!.OriginalFunction(self, other, returnValue, argc, argv);
    }

    private RValue* GenericAreaDetour(
        IHook<ScriptDelegate>? hook, Func<int, List<Notch>> createNotches,
        CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv
    ) {
        if (this.IsReady(out var rnsReloaded, out var hooks, out var utils, out var scrbp, out var bp)) {
            returnValue = hook!.OriginalFunction(self, other, returnValue, argc, argv);
            utils.setHallway(createNotches(this.hallCount), self, rnsReloaded);
            this.hallCount += 1;
        }
        return returnValue;
    }

    private RValue* OutskirtsDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scr_hallwaygen_outskirts"];
        return this.GenericAreaDetour(hook, _ => this.CreateOutskirtsNotches(), self, other, returnValue, argc, argv);
    }

    private ScriptDelegate MakeMainAreaDetour(string hookName) {
        return (CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) => {
            return this.GenericAreaDetour(ScriptHooks[hookName], count => this.hallNotches[count], self, other, returnValue, argc, argv);
        };
    }

    private RValue* KeepDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scr_hallwaygen_keep"];
        return this.GenericAreaDetour(hook, _ => this.CreateKeepNotches(), self, other, returnValue, argc, argv);
    }

    private RValue* PinnacleDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scr_hallwaygen_pinnacle"];
        this.hallCount++;
        return hook!.OriginalFunction(self, other, returnValue, argc, argv);
    }

    private static int stageFromKey(string key) {
        return key switch {
            "hw_nest" => 2,
            "hw_arsenal" => 3,
            "hw_lighthouse" => 4,
            "hw_streets" => 5,
            "hw_lakeside" => 6,
            _ => 0,
        };
    }

    private void UpdateLevel(int level) {
        if (this.IsReady(out var rnsReloaded, out var hooks, out var utils, out var scrbp, out var bp)) {
            RValue* enemyLevel = rnsReloaded.FindValue(rnsReloaded.GetGlobalInstance(), "enemyLevel");
            *enemyLevel = new RValue(level);
        }
    }

    private void UpdateLevelByHall() {
        // dictionaries to store levels by halls
        var hallLevelMap = new Dictionary<int, int> {
            { 1, 14 },
            { 2, 28 },
            { 3, 45 },
            { 4, 63 },
            { 5, 68 }
        };
        var midhallLevelMap = new Dictionary<int, int> {
            { 1, 21 },
            { 2, 36 }
        };

        if (this.notchesCount <= 3 && hallLevelMap.TryGetValue(this.hallCount, out int level)) {
            // updates enemy level for hallway start
            this.UpdateLevel(level);
        } else if (this.notchesCount == 11) {
            // updates enemy level for midhall switch
            if (midhallLevelMap.TryGetValue(this.hallCount, out level)) {
                this.UpdateLevel(level);
            }
        }
    }

    private RValue* MoveNextDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scr_hallwayprogress_move_next"];
        if (this.IsReady(out var rnsReloaded, out var hooks, out var utils, out var scrbp, out var bp)) {
            returnValue = hook!.OriginalFunction(self, other, returnValue, argc, argv);
            this.notchesCount += 1; // counts notch

            // updates enemy level upon hall entry
            this.UpdateLevelByHall();
            if (this.hallCount == 0) {
                this.UpdateLevel(this.notchesCount);
            } else if (this.notchesCount == 10 && (this.hallCount == 1 || this.hallCount == 2)) {
                // changes stage midhall
                int stage = stageFromKey(this.hallkeys[this.hallCount * 2 - 1]);
                rnsReloaded.ExecuteScript("scr_itemsys_erase_potions", this.potionSelf, this.potionOther, []);
                rnsReloaded.ExecuteScript("scr_stage_change", self, other, [new(stage), new(3000)]);
                rnsReloaded.ExecuteScript("scr_stage_play_music", self, other, [new(stage)]);
                RValue[] moveNextArgs = [new(1500), new(3000)];
                rnsReloaded.ExecuteScript("scr_players_move_next_position", self, other, moveNextArgs);
            }
        }

        return returnValue;
    }

    private double GetEnemyScale() {
        // scales enemy rewards based on how many enemies there are compared to regular hall
        return this.hallCount switch {
            0 => 3 / 10.0,
            >= 1 and < 3 => 2 / 6.0,
            3 => 4 / 6.0,
            4 => 3 / 5.0,
            _ => 1.0
        };
    }

    private RValue* GiveRewardsDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scr_rankbar_give_rewards"];
        // gets scale factor based on hall
        double enemyScale = this.GetEnemyScale();
        RValue*[] newArgv = new RValue*[3];
        newArgv[0] = argv[0]; // gold value
        newArgv[1] = argv[1]; // exp value

        if (this.IsReady(out var rnsReloaded, out var hooks, out var utils, out var scrbp, out var bp)) {
            // scales and stores values
            double val1 = utils.RValueToDouble(argv[1]);
            double val2 = utils.RValueToDouble(argv[2]);
            RValue rval1 = new RValue(Math.Ceiling(val1 * enemyScale));
            RValue rval2 = new RValue(Math.Ceiling(val2 * enemyScale));
            newArgv[1] = &rval1;
            newArgv[2] = &rval2;
        }

        // updates reward values
        fixed (RValue** newArgv2 = newArgv) {
            returnValue = hook!.OriginalFunction(self, other, returnValue, argc, newArgv2);
        }
        return returnValue;
    }

    private RValue* ErasePotionsDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scr_itemsys_erase_potions"];
        // only need to erase potions past outskirts
        if (!this.potionStored) {
            this.potionStored = true;
            this.potionSelf = self;
            this.potionOther = other;
        }
        return hook!.OriginalFunction(self, other, returnValue, argc, argv);
    }

    public void Suspend() {}

    public void Resume() {}

    public bool CanSuspend() => true;

    public void Unload() { }
    public bool CanUnload() => false;

    public Action Disposing => () => { };
}
