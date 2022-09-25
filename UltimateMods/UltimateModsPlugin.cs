using BepInEx;
using BepInEx.Configuration;
using BepInEx.IL2CPP;
using HarmonyLib;
using Hazel;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Linq;
using System.Net;
using System.IO;
using System;
using System.Reflection;
using UnhollowerBaseLib;
using UnityEngine;

namespace UltimateMods
{
    [BepInPlugin(Id, "UltimateMods", VersionString)]
    [BepInProcess("Among Us.exe")]
    public class UltimateModsPlugin : BasePlugin
    {
        public const string Id = "jp.DekoKiyo.UltimateMods";

        public const string VersionString = "0.0.1";

        public static System.Version Version = System.Version.Parse(VersionString);
        internal static BepInEx.Logging.ManualLogSource Logger;
        public static int optionsPage = 1;
        public Harmony Harmony { get; } = new Harmony(Id);
        public static UltimateModsPlugin Instance;

        public override void Load()
        {
            Logger = Log;
            Instance = this;
            // ModTranslation.Load();

            Harmony.PatchAll();
        }
    }

    [HarmonyPatch(typeof(StatsManager), nameof(StatsManager.AmBanned), MethodType.Getter)]
    public static class AmBannedPatch
    {
        public static void Postfix(out bool __result)
        {
            __result = false;
        }
    }

    [HarmonyPatch(typeof(ChatController), nameof(ChatController.Awake))]
    public static class ChatControllerAwakePatch
    {
        private static void Prefix()
        {
            if (!EOSManager.Instance.isKWSMinor)
            {
                SaveManager.chatModeType = 1;
                SaveManager.isGuest = false;
            }
        }
    }

    // Debugging tools
    [HarmonyPatch(typeof(KeyboardJoystick), nameof(KeyboardJoystick.Update))]
    //DebugModeがONの時使用可
    public static class DebugManager
    {
        private static readonly System.Random random = new System.Random((int)DateTime.Now.Ticks);
        private static List<PlayerControl> bots = new List<PlayerControl>();

        // Source Code with TheOtherRoles
        public static void Postfix(KeyboardJoystick __instance)
        {
            // if (!UltimateModsPlugin.DebugMode.Value) return;

            if (Input.GetKeyDown(KeyCode.F))
            {
                var playerControl = UnityEngine.Object.Instantiate(AmongUsClient.Instance.PlayerPrefab);
                var i = playerControl.PlayerId = (byte)GameData.Instance.GetAvailableId();

                bots.Add(playerControl);
                GameData.Instance.AddPlayer(playerControl);
                AmongUsClient.Instance.Spawn(playerControl, -2, InnerNet.SpawnFlags.None);

                int hat = random.Next(HatManager.Instance.allHats.Count);
                int pet = random.Next(HatManager.Instance.allPets.Count);
                int skin = random.Next(HatManager.Instance.allSkins.Count);
                int visor = random.Next(HatManager.Instance.allVisors.Count);
                int color = random.Next(Palette.PlayerColors.Length);
                int nameplate = random.Next(HatManager.Instance.allNamePlates.Count);

                playerControl.transform.position = PlayerControl.LocalPlayer.transform.position;
                playerControl.GetComponent<DummyBehaviour>().enabled = true;
                playerControl.NetTransform.enabled = false;
                playerControl.SetName(RandomString(10));
                playerControl.SetColor(color);
                playerControl.SetHat(HatManager.Instance.allHats[hat].ProductId, color);
                playerControl.SetPet(HatManager.Instance.allPets[pet].ProductId, color);
                playerControl.SetVisor(HatManager.Instance.allVisors[visor].ProductId);
                playerControl.SetSkin(HatManager.Instance.allSkins[skin].ProductId, color);
                playerControl.SetNamePlate(HatManager.Instance.allNamePlates[nameplate].ProductId);
                GameData.Instance.RpcSetTasks(playerControl.PlayerId, new byte[0]);
            }
        }

        public static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }

    [HarmonyPatch(typeof(ChatController), nameof(ChatController.Update))]
    public static class ChatControllerUpdatePatch
    {
        public static void Prefix()
        {
            SaveManager.chatModeType = 1;
            SaveManager.isGuest = false;
        }
        public static void Postfix(ChatController __instance)
        {
            SaveManager.chatModeType = 1;
            SaveManager.isGuest = false;
            if (Input.GetKeyDown(KeyCode.F1))
            {
                if (!__instance.isActiveAndEnabled) return;
                __instance.SetVisible(false);
                new LateTask(() =>
                {
                    __instance.SetVisible(true);
                }, 0f, "AntiChatBug");
            }
        }
    }
}