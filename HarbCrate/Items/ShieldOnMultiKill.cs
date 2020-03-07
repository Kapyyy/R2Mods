﻿using R2API;
using RoR2;
using System;
using System.Reflection;
using UnityEngine;

namespace HarbCrate.Items
{
    [Item]
    internal sealed class ShieldOnMultiKill : Item
    {

        public const float ShieldPerMK = 10f;
        private const int MultikillCountNeeded = 3;
        private const int MultKillsNeededForMaxValue = 15;
        private const int PerStack = 15;

        private readonly FieldInfo StatsDirty;
        private ItemIndex helperIndex;

        public ShieldOnMultiKill() : base()
        {
            Tier = ItemTier.Tier2;
            Name = new TokenValue("HC_MAXSHIELDONMULTIKILL", "Obsidian Bouche");
            Description = new TokenValue(
                "HC_MAXSHIELDONMULTIKILL_DESC",
                $" Gain {ShieldPerMK} additional maximum shield on multikill."
                + $" Maximum shield tops of at an aditional {MultKillsNeededForMaxValue*ShieldPerMK}<style=cStack>(+{PerStack* ShieldPerMK} per stack)</style>.");
            PickupText = new TokenValue("HC_MAXSHIELDONMULTIKILL_PICKUP", "Gain maximum shield on multikill.");
            AssetPath = HarbCratePlugin.assetPrefix + "Assets/HarbCrate/Obsidian_Shield/GhorsWay.prefab";
            SpritePath = HarbCratePlugin.assetPrefix + "Assets/HarbCrate/Obsidian_Shield/Bouche.png";
            Tags = new ItemTag[2]
            {
                ItemTag.Utility,
                ItemTag.OnKillEffect
            };

            SetupDisplayRules();
            StatsDirty = typeof(CharacterBody).GetField("statsDirty", BindingFlags.NonPublic | BindingFlags.Instance);

            HarbCratePlugin.Started += HarbCratePlugin_Started;
        }

        private void SetupDisplayRules()
        {
            DisplayRules = new ItemDisplayRuleDict(
                new ItemDisplayRule()
                {
                    ruleType = ItemDisplayRuleType.ParentedPrefab,
                    followerPrefab = Resources.Load<GameObject>(AssetPath),
                    childName = "HandR",
                    localPos = new Vector3(0f, 0f, 0.2f),
                    localAngles = new Vector3(210, 0, 0f),
                    localScale = new Vector3(25, 25, 25)
                }
            );
            DisplayRules.Add("mdlHuntress", new ItemDisplayRule()
            {
                ruleType = ItemDisplayRuleType.ParentedPrefab,
                followerPrefab = Resources.Load<GameObject>(AssetPath),
                childName = "Chest",
                localPos = new Vector3(0f, 0f, 0f),
                localAngles = new Vector3(330, 0, 0f),
                localScale = new Vector3(25, 25, 25)
            });
        }

        private void HarbCratePlugin_Started(object sender, EventArgs e)
        {
            helperIndex = ((Item)HarbCratePlugin.AllPickups[nameof(ShieldInfusionHelper)]).Definition.itemIndex;
        }

        public override void Hook()
        {
            Inventory.onServerItemGiven += UpdateShieldInfusion;
            On.RoR2.Inventory.ResetItem += Inventory_ResetItem;
            On.RoR2.CharacterBody.AddMultiKill += CharacterBodyOnAddMultiKill;
        }

        private void Inventory_ResetItem(On.RoR2.Inventory.orig_ResetItem orig, Inventory self, ItemIndex itemIndex)
        {
            if(itemIndex == Definition.itemIndex)
            {
                ShieldInfusion si = self.GetComponent<ShieldInfusion>();
                if (si != null)
                {
                    si.Count = 0;
                    si.VerifyIntegrity();
                    ResetHelperCount(self, si);
                }
            }
        }

        private void ResetHelperCount(Inventory inventory, ShieldInfusion infusion)
        {
            inventory.ResetItem(helperIndex);
            inventory.GiveItem(helperIndex,infusion.MultiKills);
        }

        private void CharacterBodyOnAddMultiKill(On.RoR2.CharacterBody.orig_AddMultiKill orig, CharacterBody self, int kills)
        {
            orig(self, kills);
            if (self.inventory && self.multiKillCount % MultikillCountNeeded == 0 && self.inventory.GetItemCount(Definition.itemIndex) > 0)
            {
                var SI = self.inventory.GetComponent<ShieldInfusion>();
                SI.MultiKills+=1;
                ResetHelperCount(self.inventory, SI);
                StatsDirty.SetValue(self, true);
            }
        }

        private void UpdateShieldInfusion(Inventory inventory, ItemIndex index, int count)
        {
            if (index != Definition.itemIndex)
                return;

            ShieldInfusion si = inventory.GetComponent<ShieldInfusion>();
            if (si == null)
            {
                si = inventory.gameObject.AddComponent<ShieldInfusion>();
                si.body = inventory.GetComponentInParent<CharacterBody>();
            }
            si.Count = count;
            si.VerifyIntegrity();
            ResetHelperCount(inventory, si);
        }

        public class ShieldInfusion : MonoBehaviour
        {
            private int multiKills;
            private ItemIndex itemIndex;
            public int Count;
            public CharacterBody body;

            public int MultiKills
            {
                get => multiKills;
                set => multiKills = Math.Min(value, (MultKillsNeededForMaxValue * Math.Max(1, Count) + (Count - 1) * PerStack));
            }

            public void VerifyIntegrity()
            {
                MultiKills = MultiKills;
            }

            private void Start()
            {
                itemIndex = ((ShieldOnMultiKill)HarbCratePlugin.AllPickups[nameof(ShieldOnMultiKill)]).Definition.itemIndex;
                if (body)
                {
                    body.onInventoryChanged += () =>
                    {
                        Count = body.inventory.GetItemCount(itemIndex);
                    };
                }
            }


        }
    }

}
