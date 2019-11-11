﻿using BepInEx;
using RoR2;
using UnityEngine;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using R2API.Utils;
using System;
using System.Reflection;
using EliteDef = RoR2.CombatDirector.EliteTierDef;
using System.Collections.Generic;

namespace Diluvian
{

    [BepInDependency(R2API.R2API.PluginGUID,BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.jarlyk.eso", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(GUID,NAME,VERSION)]
    public class Diluvian : BaseUnityPlugin
    {
        public const string
            NAME = "Diluvian",
            GUID = "com.harbingerofme." + NAME,
            VERSION = "0.0.5";

        private readonly Color DiluvianColor;
        private readonly DifficultyDef DiluvianDef;
        private DifficultyIndex DelugeIndex;
        private bool HooksApplied = false;

        private const string assetPrefix = "@HarbDiluvian";
        private const string assetString = assetPrefix+ ":Assets/Diluvian/DiluvianIcon.png";

        private bool ESOenabled = false;
        private float[] vanillaEliteMultipliers;
        private EliteDef[] CombatDirectorTierDefs;
        private readonly float DelugeEliteModifier = 0.8f;

        private Dictionary<string, string> defaultLanguage;

        private Diluvian()
        {
            DiluvianColor = new Color(0.61f, 0.07f, 0.93f);
            DiluvianDef = new DifficultyDef(
                            3.5f,
                            "DIFFICULTY_DILUVIAN_NAME",
                            assetString,
                            "DIFFICULTY_DILUVIAN_DESCRIPTION",
                            DiluvianColor
                            );
            defaultLanguage = new Dictionary<string, string>();
        }

        public void Awake()
        {

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Diluvian.diluvian"))
            {
                var bundle = AssetBundle.LoadFromStream(stream);
                var provider = new R2API.AssetBundleResourcesProvider(assetPrefix, bundle);
                R2API.ResourcesAPI.AddProvider(provider);
            }

            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.jarlyk.eso")){
                ESOenabled = true;
                Logger.LogWarning("ESO detected: Delegating Elite modifications to them. Future support planned.");
            }

            Logger.LogWarning("This is a prerelease!");

            CombatDirectorTierDefs = (EliteDef[]) typeof(CombatDirector).GetFieldCached("eliteTiers").GetValue(null);
            vanillaEliteMultipliers = new float[CombatDirectorTierDefs.Length];

            DelugeIndex = R2API.DifficultyAPI.AddDifficulty(DiluvianDef);

            R2API.AssetPlus.Languages.AddToken("DIFFICULTY_DILUVIAN_NAME", "Diluvian");
            string description = "For those found wanting. <style=cDeath>N'Kuhana</style> watches with interest.<style=cStack>\n";
            description = string.Join("\n",
                description,
                $">Difficulty Scaling: +{DiluvianDef.scalingValue*50-100}%",
                ">Player Health Regeneration: -50%",
                ">Monster Health Regeneration: +1% of MaxHP per second",
                ">Oneshot Protection: Also applies to monsters",
                ">Oneshot Protection: Hits do a maximum of 99%",
                ">Teleporter: Enemies don't stop after charge completion",
                $">Elites: {(1- DelugeEliteModifier)*100}% cheaper."
                );
            description += "</style>";
            R2API.AssetPlus.Languages.AddToken("DIFFICULTY_DILUVIAN_DESCRIPTION", description);


            Run.onRunStartGlobal += Run_onRunStartGlobal;
            Run.onRunDestroyGlobal += Run_onRunDestroyGlobal;
            
        }


        private void replaceString(string token, string newText)
        {
            defaultLanguage[token] = Language.GetString(token);
            R2API.AssetPlus.Languages.AddToken(token, newText);
        }


        private void Run_onRunStartGlobal(Run run)
        {
            ChatMessage.SendColored("A storm is brewing...", DiluvianColor);
            if (run.selectedDifficulty == DelugeIndex && HooksApplied == false)
            {
                HooksApplied = true;
                IL.RoR2.HealthComponent.TakeDamage += ChangeOSP;
                IL.RoR2.CharacterBody.RecalculateStats += AdjustRegen;
                IL.RoR2.TeleporterInteraction.StateFixedUpdate += NoSafetyAfterFinishCharging;
                TeleporterInteraction.onTeleporterFinishGlobal += MakeSureBonusDirectorDiesOnStageFinish;
                if (!ESOenabled)
                {
                    for (int i = 0; i < CombatDirectorTierDefs.Length; i++)
                    {
                        EliteDef tierDef = CombatDirectorTierDefs[i];
                        vanillaEliteMultipliers[i] = tierDef.costMultiplier;
                        tierDef.costMultiplier *= DelugeEliteModifier;
                    }
                }
                On.RoR2.ShrineBloodBehavior.FixedUpdate += BloodShrinesCost99Percent;


                replaceString("PAUSE_RESUME", "Entertain me");
                replaceString("PAUSE_SETTINGS", "Change your view.");
                replaceString("PAUSE_QUIT_TO_MENU", "Give up");
                replaceString("PAUSE_QUIT_TO_DESKTOP", "Don't come back");
            }
        }


        private void Run_onRunDestroyGlobal(Run obj)
        {
            if (HooksApplied)
            {
                IL.RoR2.HealthComponent.TakeDamage -= ChangeOSP;
                IL.RoR2.CharacterBody.RecalculateStats -= AdjustRegen;
                IL.RoR2.TeleporterInteraction.StateFixedUpdate -= NoSafetyAfterFinishCharging;
                TeleporterInteraction.onTeleporterFinishGlobal -= MakeSureBonusDirectorDiesOnStageFinish;
                if (!ESOenabled)
                {
                    for (int i = 0; i < CombatDirectorTierDefs.Length; i++)
                    {
                        EliteDef tierDef = CombatDirectorTierDefs[i];
                        tierDef.costMultiplier = vanillaEliteMultipliers[i];
                    }
                }
                On.RoR2.ShrineBloodBehavior.FixedUpdate += BloodShrinesCost99Percent;
                defaultLanguage.ForEachTry((pair) =>
                {
                    //Debug.Log($"Restoring {pair.Key}:{pair.Value} from {Language.GetString(pair.Key)}");
                    R2API.AssetPlus.Languages.AddToken(pair.Key, pair.Value);
                    
                });

                HooksApplied = false;
            }
        }

        private void BloodShrinesCost99Percent(On.RoR2.ShrineBloodBehavior.orig_FixedUpdate orig, ShrineBloodBehavior self)
        {
            orig(self);
            self.GetFieldValue<PurchaseInteraction>("purchaseInteraction").Networkcost = 99;
        }


        private void MakeSureBonusDirectorDiesOnStageFinish(TeleporterInteraction obj)
        {
            if(obj.bonusDirector && obj.bonusDirector.enabled)
            {
                obj.bonusDirector.enabled = false;
            }
        }

        private void NoSafetyAfterFinishCharging(ILContext il)
        {
            ILCursor c = new ILCursor(il);
            c.GotoNext(MoveType.After,
                x => x.MatchLdfld<TeleporterInteraction>("bonusDirector")
                );
            c.GotoNext(MoveType.Before,
                x => x.MatchLdfld<TeleporterInteraction>("bonusDirector"),
                x => x.MatchLdcI4(0)
                );
            c.Index +=1;
            c.RemoveRange(2);
            c.EmitDelegate<Action<CombatDirector>>((director) =>
            {
                director.expRewardCoefficient = 0f;
            }
            );
        }

        private void AdjustRegen(ILContext il)
        {
            ILCursor c = new ILCursor(il);
            int monsoonPlayerHelperCountIndex = 25;
            c.GotoNext(
                x => x.MatchLdcI4((int)ItemIndex.MonsoonPlayerHelper),
                x => x.MatchCallvirt<Inventory>("GetItemCount"),
                x => x.MatchStloc(out monsoonPlayerHelperCountIndex)
                );
            int regenMultiIndex = 37;
            c.GotoNext(MoveType.Before,
                x => x.MatchStloc(out regenMultiIndex),
                x => x.MatchLdloc(monsoonPlayerHelperCountIndex)
                );
            c.Index += 1;
            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Ldloc, regenMultiIndex);
            c.EmitDelegate<Func<CharacterBody, float, float>>((self, regenMulti) =>
            {
                if (self.isPlayerControlled)
                {
                    regenMulti -= 0.5f;
                    Debug.Log("Reduced RegenMulti to: " + regenMulti);
                }
                return regenMulti;
            });
            c.Emit(OpCodes.Stloc, regenMultiIndex);
            int regenIndex = 36;
            c.GotoPrev(MoveType.Before,
                x => x.MatchStloc(out regenIndex),
                x => x.MatchLdcR4(1),
                x => x.MatchStloc(regenMultiIndex)
                ); ;
            c.Index += 1;
            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Ldloc, regenIndex);
            c.EmitDelegate<Func<CharacterBody, float, float>>((self, regen) =>
            {
                if (self.teamComponent.teamIndex == TeamIndex.Monster){
                    regen += self.maxHealth * 0.01f;
                }
                return regen;
            });
            c.Emit(OpCodes.Stloc, regenIndex);
        }
        private void ChangeOSP(ILContext il)
        {
            try
            {
                ILCursor c = new ILCursor(il);
                c.GotoNext(MoveType.Before,
                    x => x.MatchCallvirt<HealthComponent>("get_hasOneshotProtection")
                    );
                c.RemoveRange(2);
                c.Emit(OpCodes.Pop);
                c.GotoNext(MoveType.Before,
                        x => x.MatchLdcR4(0.9f)
                        );
                c.Remove();
                c.Emit(OpCodes.Ldc_R4,0.99f);
                    }
            catch (Exception e)
            {
                Logger.LogWarning("Couldn't modify OneShotProtection. Maybe you have a mod interfering. Game might act weird.");
                Logger.LogError(e);
            }
        }
    }
}