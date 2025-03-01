using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using System;
using System.Linq;
using UnityEngine;

namespace mennowar.mods
{
    [BepInPlugin(BlockTweaks.PluginGUID, BlockTweaks.SharedName, "1.0.0")]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class BlockTweaks : BaseUnityPlugin
    {
        public const string PluginGUID = "mennowar.mods.BlockTweaks";
        public const string SharedName = "BlockTweaks";

        private Harmony harmony = new Harmony(PluginGUID);
        private static ManualLogSource DebugLogSource = null;  // just a logger instance

        public static ConfigEntry<bool> WriteDebugOutput;
        public static ConfigEntry<bool> RemoveDeflectionForce;
        public static ConfigEntry<bool> IgnoreBlockArmorExceedingDamage;

        private void Awake()
        {
            try
            {
                CreateConfigValues();

                harmony.PatchAll();

                Debug("Harmony patch finished");
            }
            catch (Exception ex)
            {
                Debug("Error running Harmony Patch:\n{ex}", true);
            }
        }

        /// <summary>
        /// Creates the configuration values / initial config file
        /// </summary>
        private void CreateConfigValues()
        {
            WriteDebugOutput = Config.Bind<bool>("Debug", "writeDebug", true,
                new ConfigDescription("Write Debug Informations to the console?", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            RemoveDeflectionForce = Config.Bind<bool>("General", "Remove Deflection force", true,
                new ConfigDescription("Remove the push-back of enemies when successfully deflecting", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            IgnoreBlockArmorExceedingDamage = Config.Bind<bool>("General", "Ignore BlockArmor exceeding Damage", true,
                new ConfigDescription("Block even when the damage exceeds your block armor and the exceeding damage fills your stagger threshold", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
            
            Debug("Debug-Messages enabled");
            Debug($"Pushback tweak: {RemoveDeflectionForce.Value}");
            Debug($"Block armor tweak: {IgnoreBlockArmorExceedingDamage.Value}");
        }

        /// <summary>
        /// When WriteDebugOutput is enabled, writes to the console as message
        /// </summary>
        /// <param name="value"></param>
        public static void Debug(string value, bool forceOutput = false)
        {
            if (WriteDebugOutput.Value || forceOutput)
            {
                if (DebugLogSource == null)
                {
                    DebugLogSource = BepInEx.Logging.Logger.CreateLogSource(SharedName);
                }

                if (DebugLogSource is not null)
                {
                    DebugLogSource.LogMessage(value);
                }
            }
        }

        /// <summary>
        /// Removes the DeflectionForce when picking up or equipping an item
        /// </summary>
        [HarmonyPatch(typeof(ItemDrop.ItemData), "GetDeflectionForce", new Type[] { typeof(int) })]
        public class BlockheimPatch1
        {
            private static void Postfix(ref ItemDrop.ItemData __instance, ref float __result)
            {
                if (RemoveDeflectionForce.Value)
                {
                    Debug("Removing Deflection Force");
                    
                    __result = 0.0f;
                }
            }
        }

        /// <summary>
        /// In vanilla Valheim there are 2 situations in which your block/parry is completely ignored:
        /// 1. When you don't have enough stamina
        /// 2. When the damage exceeds your block armor and the exceeding damage fills your stagger treshold
        ///  This simply removes the latter check.When you block/parry, it will always count as long as you have stamina.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid))]
        [HarmonyPatch("BlockAttack")]
        private class BlockPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> source = new List<CodeInstruction>(instructions);
                
                if (!IgnoreBlockArmorExceedingDamage.Value)
                {
                    Debug("Not patching block attack");
                    return source.AsEnumerable<CodeInstruction>();
                }
                
                var index1 = 0;
                try
                {
                    var addStaggerDamageIndex = source.FindIndex((Predicate<CodeInstruction>)(p => p.opcode == OpCodes.Call && p.operand.ToString().Contains("AddStaggerDamage")));
                    var operand = (LocalBuilder)source[addStaggerDamageIndex + 1].operand;
                    var blockDamageIndex = source.FindIndex((Predicate<CodeInstruction>)(p => p.opcode == OpCodes.Callvirt && p.operand.ToString().Contains("BlockDamage")));
                    if (blockDamageIndex > -1)
                    {
                        for (int index4 = blockDamageIndex; index4 > addStaggerDamageIndex; --index4)
                        {
                            if (CodeInstructionExtensions.IsLdloc(source[index4], operand) && (source[index4 + 1].opcode == OpCodes.Brtrue || source[index4 + 1].opcode == OpCodes.Brtrue_S))
                            {
                                index1 = index4;
                                break;
                            }
                        }

                        if (index1 > 0)
                        {
                            source.RemoveRange(index1, 2);
                            Debug("Patching block attack successful");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug($"Block attack Exception\n{ex}", true);
                }

                if (index1 == 0)
                    Debug("Patching block attack failed.", true);

                return source.AsEnumerable<CodeInstruction>();
            }
        }
    }
}
