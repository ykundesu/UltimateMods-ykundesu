using HarmonyLib;
using UltimateMods.Utilities;

namespace UltimateMods.Roles.Patches
{
    public static class SabotageButtonPatch
    {
        [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
        class VentButtonVisibilityPatch
        {
            static void Postfix(PlayerControl __instance)
            {
                if (__instance.AmOwner && COHelpers.ShowButtons)
                {
                    FastDestroyableSingleton<HudManager>.Instance.ImpostorVentButton.Hide();
                    FastDestroyableSingleton<HudManager>.Instance.SabotageButton.Hide();

                    if (COHelpers.ShowButtons)
                    {
                        if (__instance.RoleCanUseVents())
                            FastDestroyableSingleton<HudManager>.Instance.ImpostorVentButton.Show();

                        if (__instance.RoleCanSabotage())
                        {
                            FastDestroyableSingleton<HudManager>.Instance.SabotageButton.Show();
                            FastDestroyableSingleton<HudManager>.Instance.SabotageButton.gameObject.SetActive(true);
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(SabotageButton), nameof(SabotageButton.DoClick))]
        public static class SabotageButtonDoClickPatch
        {
            public static bool Prefix(SabotageButton __instance)
            {
                if (!PlayerControl.LocalPlayer.IsNeutral())
                    return true;

                HudManager.Instance.ShowMap((Il2CppSystem.Action<MapBehaviour>)((SaboMap) => { SaboMap.ShowSabotageMap(); }));
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(MapBehaviour), nameof(MapBehaviour.ShowSabotageMap))]
    class ShowImpostorMapPatch
    {
        public static void Prefix(ref RoleTeamTypes __state)
        {
            var player = PlayerControl.LocalPlayer;
            if (player.isRole(RoleType.Jester))
            {
                __state = player.Data.Role.TeamType;
                player.Data.Role.TeamType = RoleTeamTypes.Impostor;
            }
        }

        public static void Postfix(ref RoleTeamTypes __state)
        {
            var player = PlayerControl.LocalPlayer;
            if (player.isRole(RoleType.Jester))
            {
                player.Data.Role.TeamType = __state;
            }
        }
    }
}