using HarmonyLib;
using UltimateMods.Roles;
using static UltimateMods.GameHistory;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using System.Text;
using UltimateMods.Modules;
using UltimateMods.Utilities;
using static UltimateMods.Roles.NeutralRoles;
using static UltimateMods.CustomRolesH;

namespace UltimateMods.EndGame
{
    public enum CustomGameOverReason
    {
        JesterExiled = 10,

        SabotageReactor,
        SabotageO2,
        ForceEnd,
        EveryoneLose
    }

    public enum FinalStatus
    {
        Alive, // 生存
        Sabotage, // サボ
        Reactor, // リアクターサボ
        Exiled, // 追放
        Misfire, // 誤爆
        Suicide, // 自殺
        Dead, // 死亡(キル)
        LackO2, // 酸素不足
        Disconnected // 切断
    }

    public enum WinCondition
    {
        Default,

        CrewmateWin,
        ImpostorWin,
        JesterWin,

        ForceEnd,
        EveryoneLose
    }

    static class AdditionalTempData
    {
        public static WinCondition winCondition = WinCondition.Default;
        public static List<WinCondition> additionalWinConditions = new List<WinCondition>();
        public static List<PlayerRoleInfo> playerRoles = new List<PlayerRoleInfo>();
        public static GameOverReason gameOverReason;

        public static void clear()
        {
            playerRoles.Clear();
            additionalWinConditions.Clear();
            winCondition = WinCondition.Default;
        }
        internal class PlayerRoleInfo
        {
            public string WinOrLose { get; set; }
            public string PlayerName { get; set; }
            public List<RoleInfo> Roles { get; set; }
            public string RoleString { get; set; }
            public int TasksCompleted { get; set; }
            public int TasksTotal { get; set; }
            public FinalStatus Status { get; set; }
            public int PlayerId { get; set; }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    public static class OnGameEndPatch
    {
        public static void Prefix(AmongUsClient __instance, [HarmonyArgument(0)] ref EndGameResult endGameResult)
        {
            AdditionalTempData.gameOverReason = endGameResult.GameOverReason;
            if ((int)endGameResult.GameOverReason >= 10) endGameResult.GameOverReason = GameOverReason.ImpostorByKill;
        }

        public static void Postfix(AmongUsClient __instance, [HarmonyArgument(0)] ref EndGameResult endGameResult)
        {
            var GameOverReason = AdditionalTempData.gameOverReason;
            AdditionalTempData.clear();

            var excludeRoles = new RoleType[] { };
            foreach (var p in GameData.Instance.AllPlayers.GetFastEnumerator())
            {
                var roles = RoleInfo.getRoleInfoForPlayer(p.Object, excludeRoles, includeHidden: true);
                var (tasksCompleted, tasksTotal) = TasksHandler.taskInfo(p);
                var finalStatus = finalStatuses[p.PlayerId] =
                    p.Disconnected == true ? FinalStatus.Disconnected :
                    finalStatuses.ContainsKey(p.PlayerId) ? finalStatuses[p.PlayerId] :
                    p.IsDead == true ? FinalStatus.Dead :
                    GameOverReason == (GameOverReason)CustomGameOverReason.SabotageReactor && !p.Role.IsImpostor ? FinalStatus.Reactor :
                    GameOverReason == (GameOverReason)CustomGameOverReason.SabotageO2 && !p.Role.IsImpostor ? FinalStatus.LackO2 :
                    FinalStatus.Alive;

                if (GameOverReason == GameOverReason.HumansByTask && p.Object.IsCrew()) tasksCompleted = tasksTotal;

                AdditionalTempData.playerRoles.Add(new AdditionalTempData.PlayerRoleInfo()
                {
                    PlayerName = p.PlayerName,
                    PlayerId = p.PlayerId,
                    // NameSuffix = Lovers.getIcon(p.Object),
                    Roles = roles,
                    RoleString = RoleInfo.GetRolesString(p.Object, true, excludeRoles, true),
                    TasksTotal = tasksTotal,
                    TasksCompleted = tasksCompleted,
                    Status = finalStatus,
                });
            }

            List<PlayerControl> notWinners = new List<PlayerControl>();
            if (Jester.jester != null) notWinners.Add(Jester.jester);

            List<WinningPlayerData> winnersToRemove = new List<WinningPlayerData>();
            foreach (WinningPlayerData winner in TempData.winners.GetFastEnumerator())
            {
                if (notWinners.Any(x => x.Data.PlayerName == winner.PlayerName)) winnersToRemove.Add(winner);
            }
            foreach (var winner in winnersToRemove) TempData.winners.Remove(winner);

            bool JesterWin = Jester.jester != null && GameOverReason == (GameOverReason)CustomGameOverReason.JesterExiled;

            bool ForceEnd = KeyboardClass.triggerForceEnd;
            bool EveryoneLose = AdditionalTempData.playerRoles.All(x => x.Status != FinalStatus.Alive);

            if (JesterWin)
            {
                TempData.winners = new Il2CppSystem.Collections.Generic.List<WinningPlayerData>();
                Jester.jester.Data.IsDead = true;
                WinningPlayerData wpd = new WinningPlayerData(Jester.jester.Data);
                TempData.winners.Add(wpd);
                AdditionalTempData.winCondition = WinCondition.JesterWin;
            }

            else if (ForceEnd)
            {
                TempData.winners = new Il2CppSystem.Collections.Generic.List<WinningPlayerData>();
                foreach (PlayerControl p in PlayerControl.AllPlayerControls)
                {
                    if (p)
                    {
                        WinningPlayerData wpd = new(p.Data);
                        TempData.winners.Add(wpd);
                    }
                }
                AdditionalTempData.winCondition = WinCondition.ForceEnd;
            }

            else if (EveryoneLose)
            {
                TempData.winners = new Il2CppSystem.Collections.Generic.List<WinningPlayerData>();
                AdditionalTempData.winCondition = WinCondition.EveryoneLose;
            }

            foreach (WinningPlayerData wpd in TempData.winners)
            {
                wpd.IsDead = wpd.IsDead || AdditionalTempData.playerRoles.Any(x => x.PlayerName == wpd.PlayerName && x.Status != FinalStatus.Alive);
            }

            // Reset Settings
            RPCProcedure.ResetVariables();
        }
    }

    [HarmonyPatch(typeof(EndGameManager), nameof(EndGameManager.SetEverythingUp))]
    public class EndGameManagerSetUpPatch
    {
        public static void Postfix(EndGameManager __instance)
        {
            // Delete and readd PoolablePlayers always showing the name and role of the player
            foreach (PoolablePlayer pb in __instance.transform.GetComponentsInChildren<PoolablePlayer>())
            {
                UnityEngine.Object.Destroy(pb.gameObject);
            }
            int num = Mathf.CeilToInt(7.5f);
            List<WinningPlayerData> list = TempData.winners.ToArray().ToList().OrderBy(delegate (WinningPlayerData b)
            {
                if (!b.IsYou)
                {
                    return 0;
                }
                return -1;
            }).ToList<WinningPlayerData>();
            for (int i = 0; i < list.Count; i++)
            {
                WinningPlayerData winningPlayerData2 = list[i];
                int num2 = (i % 2 == 0) ? -1 : 1;
                int num3 = (i + 1) / 2;
                float num4 = (float)num3 / (float)num;
                float num5 = Mathf.Lerp(1f, 0.75f, num4);
                float num6 = (float)((i == 0) ? -8 : -1);
                PoolablePlayer poolablePlayer = UnityEngine.Object.Instantiate<PoolablePlayer>(__instance.PlayerPrefab, __instance.transform);
                poolablePlayer.transform.localPosition = new Vector3(1f * (float)num2 * (float)num3 * num5, FloatRange.SpreadToEdges(-1.125f, 0f, num3, num), num6 + (float)num3 * 0.01f) * 0.9f;
                float num7 = Mathf.Lerp(1f, 0.65f, num4) * 0.9f;
                Vector3 vector = new Vector3(num7, num7, 1f);
                poolablePlayer.transform.localScale = vector;
                poolablePlayer.UpdateFromPlayerOutfit((GameData.PlayerOutfit)winningPlayerData2, PlayerMaterial.MaskType.ComplexUI, winningPlayerData2.IsDead, true);
                if (winningPlayerData2.IsDead)
                {
                    poolablePlayer.cosmetics.currentBodySprite.BodySprite.sprite = poolablePlayer.cosmetics.currentBodySprite.GhostSprite;
                    poolablePlayer.SetDeadFlipX(i % 2 == 0);
                }
                else
                {
                    poolablePlayer.SetFlipX(i % 2 == 0);
                }

                poolablePlayer.cosmetics.nameText.color = Color.white;
                poolablePlayer.cosmetics.nameText.transform.localScale = new Vector3(1f / vector.x, 1f / vector.y, 1f / vector.z);
                poolablePlayer.cosmetics.nameText.transform.localPosition = new Vector3(poolablePlayer.cosmetics.nameText.transform.localPosition.x, poolablePlayer.cosmetics.nameText.transform.localPosition.y, -15f);
                poolablePlayer.cosmetics.nameText.text = winningPlayerData2.PlayerName;

                foreach (var data in AdditionalTempData.playerRoles)
                {
                    if (data.PlayerName != winningPlayerData2.PlayerName) continue;
                    var roles =
                    poolablePlayer.cosmetics.nameText.text += $"\n{string.Join("\n", data.Roles.Select(x => Helpers.cs(x.color, x.name)))}";
                }
            }

            // Additional code
            GameObject bonusText = UnityEngine.Object.Instantiate(__instance.WinText.gameObject);
            bonusText.transform.position = new Vector3(__instance.WinText.transform.position.x, __instance.WinText.transform.position.y - 0.5f, __instance.WinText.transform.position.z);
            bonusText.transform.localScale = new Vector3(0.7f, 0.7f, 1f);
            TMPro.TMP_Text textRenderer = bonusText.GetComponent<TMPro.TMP_Text>();
            textRenderer.text = "";

            string winText = ModTranslation.getString("Win");
            string loseText = ModTranslation.getString("Lose");

            if (AdditionalTempData.winCondition == WinCondition.JesterWin)
            {
                textRenderer.text = ModTranslation.getString("Jester") + winText;
                textRenderer.color = Jester.color;
                __instance.BackgroundBar.material.SetColor("_Color", Jester.color);
            }
            else if (AdditionalTempData.gameOverReason == GameOverReason.HumansByTask || AdditionalTempData.gameOverReason == GameOverReason.HumansByVote)
            {
                textRenderer.text = ModTranslation.getString("Crewmate") + winText;
                textRenderer.color = Palette.White;
            }
            else if (AdditionalTempData.gameOverReason == GameOverReason.ImpostorByKill || AdditionalTempData.gameOverReason == GameOverReason.ImpostorByVote)
            {
                textRenderer.text = ModTranslation.getString("Impostor") + winText;
                textRenderer.color = Palette.ImpostorRed;
            }
            else if (AdditionalTempData.gameOverReason == (GameOverReason)CustomGameOverReason.SabotageO2)
            {
                textRenderer.text = ModTranslation.getString("Impostor") + winText + $"\n" + ModTranslation.getString("O2Win");
                textRenderer.color = Palette.ImpostorRed;
            }
            else if (AdditionalTempData.gameOverReason == (GameOverReason)CustomGameOverReason.SabotageReactor)
            {
                textRenderer.text = ModTranslation.getString("Impostor") + winText + $"\n" + ModTranslation.getString("ReactorWin");
                textRenderer.color = Palette.ImpostorRed;
            }
            else if (AdditionalTempData.winCondition == WinCondition.EveryoneLose)
            {
                textRenderer.text = ModTranslation.getString("Everyone") + loseText;
                textRenderer.color = Palette.DisabledGrey;
                __instance.BackgroundBar.material.SetColor("_Color", Palette.DisabledGrey);
            }
            else if (AdditionalTempData.winCondition == WinCondition.ForceEnd)
            {
                textRenderer.text = ModTranslation.getString("ForceEnd");
                textRenderer.color = Palette.DisabledGrey;
                __instance.BackgroundBar.material.SetColor("_Color", Palette.DisabledGrey);
            }
            AdditionalTempData.clear();
        }
    }

    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.CheckEndCriteria))]
    class CheckEndCriteriaPatch
    {
        public static bool Prefix(ShipStatus __instance)
        {
            if (!GameData.Instance) return false;
            if (DestroyableSingleton<TutorialManager>.InstanceExists) // InstanceExists | Don't check Custom Criteria when in Tutorial
                return true;
            var statistics = new PlayerStatistics(__instance);
            if (CheckAndEndGameForJesterWin(__instance)) return false;
            if (CheckAndEndGameForSabotageWin(__instance)) return false;
            if (CheckAndEndGameForTaskWin(__instance)) return false;
            if (CheckAndEndGameForForceEnd(__instance)) return false;
            if (CheckAndEndGameForImpostorWin(__instance, statistics)) return false;
            if (CheckAndEndGameForCrewmateWin(__instance, statistics)) return false;
            return false;
        }

        private static bool CheckAndEndGameForJesterWin(ShipStatus __instance)
        {
            if (Jester.TriggerJesterWin)
            {
                __instance.enabled = false;
                ShipStatus.RpcEndGame((GameOverReason)CustomGameOverReason.JesterExiled, false);
                return true;
            }
            return false;
        }

        private static bool CheckAndEndGameForForceEnd(ShipStatus __instance)
        {
            if (KeyboardClass.triggerForceEnd)
            {
                __instance.enabled = false;
                ShipStatus.RpcEndGame((GameOverReason)CustomGameOverReason.ForceEnd, false);
                KeyboardClass.triggerForceEnd = false;
                return true;
            }
            return false;
        }

        private static bool CheckAndEndGameForSabotageWin(ShipStatus __instance)
        {
            if (MapUtilities.Systems == null) return false;
            var systemType = MapUtilities.Systems.ContainsKey(SystemTypes.LifeSupp) ? MapUtilities.Systems[SystemTypes.LifeSupp] : null;
            if (systemType != null)
            {
                LifeSuppSystemType lifeSuppSystemType = systemType.TryCast<LifeSuppSystemType>();
                if (lifeSuppSystemType != null && lifeSuppSystemType.Countdown < 0f)
                {
                    EndGameForO2(__instance);
                    lifeSuppSystemType.Countdown = 10000f;
                    return true;
                }
            }
            var systemType2 = MapUtilities.Systems.ContainsKey(SystemTypes.Reactor) ? MapUtilities.Systems[SystemTypes.Reactor] : null;
            if (systemType2 == null)
            {
                systemType2 = MapUtilities.Systems.ContainsKey(SystemTypes.Laboratory) ? MapUtilities.Systems[SystemTypes.Laboratory] : null;
            }
            if (systemType2 != null)
            {
                ICriticalSabotage criticalSystem = systemType2.TryCast<ICriticalSabotage>();
                if (criticalSystem != null && criticalSystem.Countdown < 0f)
                {
                    EndGameForReactor(__instance);
                    criticalSystem.ClearSabotage();
                    return true;
                }
            }
            return false;
        }

        private static bool CheckAndEndGameForTaskWin(ShipStatus __instance)
        {
            if (GameData.Instance.TotalTasks > 0 && GameData.Instance.TotalTasks <= GameData.Instance.CompletedTasks)
            {
                __instance.enabled = false;
                ShipStatus.RpcEndGame(GameOverReason.HumansByTask, false);
                return true;
            }
            return false;
        }

        private static bool CheckAndEndGameForImpostorWin(ShipStatus __instance, PlayerStatistics statistics)
        {
            if (statistics.TeamImpostorsAlive >= statistics.TotalAlive - statistics.TeamImpostorsAlive)
            {
                __instance.enabled = false;
                GameOverReason endReason;
                switch (TempData.LastDeathReason)
                {
                    case DeathReason.Exile:
                        endReason = GameOverReason.ImpostorByVote;
                        break;
                    case DeathReason.Kill:
                        endReason = GameOverReason.ImpostorByKill;
                        break;
                    default:
                        endReason = GameOverReason.ImpostorByVote;
                        break;
                }
                ShipStatus.RpcEndGame(endReason, false);
                return true;
            }
            return false;
        }

        private static bool CheckAndEndGameForCrewmateWin(ShipStatus __instance, PlayerStatistics statistics)
        {
            if (statistics.TeamImpostorsAlive == 0)
            {
                __instance.enabled = false;
                ShipStatus.RpcEndGame(GameOverReason.HumansByVote, false);
                return true;
            }
            return false;
        }

        private static void EndGameForO2(ShipStatus __instance)
        {
            __instance.enabled = false;
            ShipStatus.RpcEndGame((GameOverReason)CustomGameOverReason.SabotageO2, false);
            return;
        }

        private static void EndGameForReactor(ShipStatus __instance)
        {
            __instance.enabled = false;
            ShipStatus.RpcEndGame((GameOverReason)CustomGameOverReason.SabotageReactor, false);
            return;
        }
    }

    internal class PlayerStatistics
    {
        public int TeamImpostorsAlive { get; set; }
        public int TeamCrew { get; set; }
        public int NeutralAlive { get; set; }
        public int TotalAlive { get; set; }

        public PlayerStatistics(ShipStatus __instance)
        {
            GetPlayerCounts();
        }

        private void GetPlayerCounts()
        {
            int numImpostorsAlive = 0;
            int numTotalAlive = 0;
            int numNeutralAlive = 0;
            int numCrew = 0;

            for (int i = 0; i < GameData.Instance.PlayerCount; i++)
            {
                GameData.PlayerInfo playerInfo = GameData.Instance.AllPlayers[i];
                if (!playerInfo.Disconnected)
                {
                    if (playerInfo.Object.IsCrew()) numCrew++;
                    if (!playerInfo.IsDead)
                    {
                        numTotalAlive++;
                        if (playerInfo.Role.IsImpostor) numImpostorsAlive++;
                        if (playerInfo.Object.IsNeutral()) numNeutralAlive++;
                    }
                }
            }

            TeamCrew = numCrew;
            TeamImpostorsAlive = numImpostorsAlive;
            NeutralAlive = numNeutralAlive;
            TotalAlive = numTotalAlive;
        }
    }
}