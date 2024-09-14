using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;

using Reloaded.Hooks.Definitions;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;

using RNSReloaded.BunnyExtinction.Config;
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

namespace RNSReloaded.BunnyExtinction;

public unsafe class Mod : IMod {
    private const int SCRIPTCONST = 100000;

    private WeakReference<IRNSReloaded>? rnsReloadedRef;
    private WeakReference<IReloadedHooks>? hooksRef;
    private ILoggerV1 logger = null!;

    private Configurator configurator = null!;
    private Config.Config config = null!;

    private int deadPlayers = 0; // bitmask representing dead players

    private static Dictionary<string, IHook<ScriptDelegate>> ScriptHooks = new();

    public void StartEx(IModLoaderV1 loader, IModConfigV1 modConfig) {
        this.rnsReloadedRef = loader.GetController<IRNSReloaded>()!;
        this.hooksRef = loader.GetController<IReloadedHooks>()!;
        this.logger = loader.GetLogger();

        if (this.rnsReloadedRef.TryGetTarget(out var rnsReloaded)) {
            rnsReloaded.OnReady += this.Ready;
        }

        this.configurator = new Configurator(((IModLoader) loader).GetModConfigDirectory(modConfig.ModId));
        this.config = this.configurator.GetConfiguration<Config.Config>(0);
        this.config.ConfigurationUpdated += this.ConfigurationUpdated;
    }

    private void ConfigurationUpdated(IUpdatableConfigurable newConfig) {
        this.config = (Config.Config) newConfig;
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
            { "scrdt_encounter", this.EncounterDetour }, // UNUSED
            { "scr_kotracker_can_revive", this.ReviveDetour }, // UNUSED

            { "scr_charselect2_start_run", this.StartRunDetour },
            { "scr_diffswitch", this.DiffSwitchDetour},
            { "scr_player_charspeed_calc", this.SpeedCalcDetour }, // max speed

            { "scrbp_erase_radius", this.EraseRadiusDetour }, // bullet deletion
            // invuln control
            { "scr_player_invuln", this.InvulnDetour },
            { "scr_pattern_deal_damage_ally", this.PlayerDmgDetour },
            { "scr_hbsflag_check", this.AddHbsFlagCheckDetour },
            // permadeath
            { "scr_kotracker_draw_timer", this.KOTimerDetour },
            { "scrbp_time_repeating", this.TimeRepeatingDetour },
            // shira invuln
            { "scrbp_warning_msg_enrage", this.SteelWarningDetour },
            { "bp_rabbit_queen1_pt4", this.CreatePostSteelDetour("bp_rabbit_queen1_pt4")},
            { "bp_rabbit_queen1_pt4_s", this.CreatePostSteelDetour("bp_rabbit_queen1_pt4_s")},
            { "bp_rabbit_queen1_pt6", this.CreatePostSteelDetour("bp_rabbit_queen1_pt6")},
            { "bp_rabbit_queen1_pt6_s", this.CreatePostSteelDetour("bp_rabbit_queen1_pt6_s")},
            { "bp_rabbit_queen1_pt8", this.CreatePostSteelDetour("bp_rabbit_queen1_pt8")},
            { "bp_rabbit_queen1_pt8_s", this.CreatePostSteelDetour("bp_rabbit_queen1_pt8_s")},
        };

        foreach (var detourPair in detourMap) {
            this.CreateAndEnableHook(detourPair.Key, detourPair.Value, out var hook);
            if (hook != null) {
                ScriptHooks[detourPair.Key] = hook;
            }
        }

        this.ConfigSetupHooks();
    }

    private void ConfigSetupHooks() {
        // function to enable/disable certain hooks depending on config
        if (!this.config.InfernalBBQ) {
            // playing BEX
            ScriptHooks["scrbp_erase_radius"].Disable();
            ScriptHooks["scr_player_charspeed_calc"].Disable();
            this.EnableInvuls();
            ScriptHooks["scrbp_warning_msg_enrage"].Disable();
            ScriptHooks["scr_kotracker_draw_timer"].Disable(); // permadeath
            ScriptHooks["scr_kotracker_can_revive"].Disable();
            ScriptHooks["scrbp_time_repeating"].Disable();
        } else {
            // playing BBQ
            ScriptHooks["scrbp_erase_radius"].Enable();
            ScriptHooks["scr_player_charspeed_calc"].Enable();
            this.DisableInvuls();
            ScriptHooks["scrbp_warning_msg_enrage"].Enable();
            ScriptHooks["scr_kotracker_draw_timer"].Enable(); // permadeath
            ScriptHooks["scr_kotracker_can_revive"].Enable();
            ScriptHooks["scrbp_time_repeating"].Enable();
        }
    }


    private RValue* StartRunDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scr_charselect2_start_run"];
        this.ConfigSetupHooks(); // update settings at the start of every run
        this.deadPlayers = 0; // reset mask
        return hook!.OriginalFunction(self, other, returnValue, argc, argv);
    }

    // PERMADEATH
    private RValue* KOTimerDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scr_kotracker_draw_timer"];
        if (this.IsReady(out var rnsReloaded, out var hooks, out var utils, out var scrbp, out var bp)) {
            // if KOtimer is drawn, add player to mask
            int id = (int) utils.RValueToLong(argv[0]);
            this.deadPlayers |= (1 << id);
        }
        return hook!.OriginalFunction(self, other, returnValue, argc, argv);
    }

    private RValue* ReviveDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scr_kotracker_can_revive"];
        RValue result = new RValue(0);
        return &result;
    }

    private void EnforceDeath(CInstance* self, CInstance* other) {
        if (this.IsReady(out var rnsReloaded, out var hooks, out var utils, out var scrbp, out var bp)) {
            // setting hp to smite players
            RValue* playerHp = rnsReloaded.FindValue(rnsReloaded.GetGlobalInstance(), "playerHp");
            for (int i = 0; i < 4; i++) { // iterate through bitmask
                if ((this.deadPlayers & (1 << i)) != 0) { // check if player has died
                    *rnsReloaded.ArrayGetEntry(rnsReloaded.ArrayGetEntry(playerHp, 0), i) = new RValue(0);
                }
            }
        }
    }

    private RValue* TimeRepeatingDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scrbp_time_repeating"];
        this.EnforceDeath(self, other);
        return hook!.OriginalFunction(self, other, returnValue, argc, argv);
    }

    // MAX HEALTH/SPEED
    // originally for PERMADEATH unused
    private RValue* EncounterDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scrdt_encounter"];
        returnValue = hook!.OriginalFunction(self, other, returnValue, argc, argv);
        this.LimitHealth();
        return returnValue;
    }

    private RValue* DiffSwitchDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scr_diffswitch"];
        returnValue = hook!.OriginalFunction(self, other, returnValue, argc, argv);
        //return returnValue;
        RValue newHealth = new RValue(1);
        return &newHealth;
    }

    private void LimitHealth() {
        // health is recalculated very often, so this doesnt work
        if (this.IsReady(out var rnsReloaded, out var hooks, out var utils, out var scrbp, out var bp)) {
            /*RValue* playerStat = rnsReloaded.FindValue(rnsReloaded.GetGlobalInstance(), "playerStat");
            RValue* playerArr = playerStat->Get(0);
            int[] playerInd = [0, 1, 2, 3];
            foreach (int i in playerInd) {
                RValue* player = playerArr->Get(i);
                double health = utils.RValueToDouble(player->Get(1));
                RValue* playerHealth = player->Get(1);
                if (health > 1) {
                    RValue newHealth = new RValue(1);
                    *(player->Get(1)) = newHealth;
                    //rnsReloaded.ExecuteScript("tpat_player_set_stat")
                    
                }
            }*/
            /*RValue* itemStat = rnsReloaded.FindValue(rnsReloaded.GetGlobalInstance(), "itemStat");
            itemStat*/
        }
    }

    private RValue* SpeedCalcDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scr_player_charspeed_calc"];
        returnValue = hook!.OriginalFunction(self, other, returnValue, argc, argv);
        if (this.IsReady(out var rnsReloaded, out var hooks, out var utils, out var scrbp, out var bp)) {
            if (utils.RValueToDouble(returnValue) > 7.70) {
                *returnValue = new RValue(7.70);
            }
        }
        return returnValue;
    }

    // BULLET DELETION
    private RValue* EraseRadiusDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scrbp_erase_radius"];
        RValue newRadius = new RValue(-1);
        argv[2] = &newRadius;
        return hook!.OriginalFunction(self, other, returnValue, argc, argv);
    }

    // INVUL CONTROL
    private bool isTakingDamage = false;

    private void EnableInvuls() {
        ScriptHooks["scr_pattern_deal_damage_ally"].Disable();
        ScriptHooks["scr_player_invuln"].Disable();
        ScriptHooks["scr_hbsflag_check"].Disable();
    }

    private void DisableInvuls() {
        ScriptHooks["scr_pattern_deal_damage_ally"].Enable();
        ScriptHooks["scr_player_invuln"].Enable();
        ScriptHooks["scr_hbsflag_check"].Enable();
    }

    // steel hooks
    private RValue* SteelWarningDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scrbp_warning_msg_enrage"];
        if (argv[1]->ToString() == "eff_steelyourself") {
            if (this.config.InfernalBBQ) {
                this.EnableInvuls();
            }
        }
        hook!.OriginalFunction(self, other, returnValue, argc, argv);
        return returnValue;
    }
    
    private ScriptDelegate CreatePostSteelDetour(string scriptName) {
        return (CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) => {
            var hook = ScriptHooks[scriptName];
            if (this.config.InfernalBBQ) {
                this.DisableInvuls();
            }
            returnValue = hook!.OriginalFunction(self, other, returnValue, argc, argv);
            return returnValue;
        };
    }

    // invuln hooks
    private RValue* PlayerDmgDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scr_pattern_deal_damage_ally"];
        this.isTakingDamage = true;
        returnValue = hook!.OriginalFunction(self, other, returnValue, argc, argv);
        this.isTakingDamage = false;
        return returnValue;
    }

    private RValue* InvulnDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scr_player_invuln"];
        // this is basically steelheart's implementation
        if (!this.isTakingDamage) { argv[0]->Real = -30000; }
        returnValue = hook!.OriginalFunction(self, other, returnValue, argc, argv);
        return returnValue;
    }

    private RValue* AddHbsFlagCheckDetour(CInstance* self, CInstance* other, RValue* returnValue, int argc, RValue** argv) {
        var hook = ScriptHooks["scr_hbsflag_check"];
        returnValue = hook!.OriginalFunction(self, other, returnValue, argc, argv);
        if (argv[2]->Real == 1 || argv[2]->Real == 2 || argv[2]->Real == 32) { // Vanish/Ghost, Stoneskin, Super
            returnValue->Real = 0;
        }
        return returnValue;
    }

    public void Suspend() {}

    public void Resume() {}

    public bool CanSuspend() => true;

    public void Unload() { }
    public bool CanUnload() => false;

    public Action Disposing => () => { };
}
