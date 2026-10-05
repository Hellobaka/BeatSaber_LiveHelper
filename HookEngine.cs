using HarmonyLib;
using LiveHelper.Models;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LiveHelper
{
    public sealed class HookEngine(Plugin plugin) : IDisposable
    {
        private Harmony Harmony { get; set; } = new("LiveHelper.GameBridge");

        public GameplayCoreSceneSetupData? GameplayCoreSceneSetupData { get; set; }

        public StandardLevelGameplayManager? StandardLevelGameplayManager { get; set; }

        public ScoreController? ScoreController { get; set; }

        public PauseController? PauseController { get; set; }

        public AudioTimeSyncController? AudioTimeSyncController { get; set; }

        public ResultsStatsUi? ResultsStatsUi { get; set; }

        private bool HideWhenResultsClose { get; set; }

        public void Start()
        {
            Harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private void LevelStarted()
        {
            try
            {
                HideWhenResultsClose = false;
                if (GameplayCoreSceneSetupData == null
                    || GameplayCoreSceneSetupData.beatmapLevel == null
                    || GameplayCoreSceneSetupData.transformedBeatmapData == null)
                {
                    plugin.Logger.Warn("Gameplay setup unavailable; overlay remains hidden");
                    return;
                }
                var level = GameplayCoreSceneSetupData.beatmapLevel;
                var key = GameplayCoreSceneSetupData.beatmapKey;
                var data = GameplayCoreSceneSetupData.transformedBeatmapData;
                var count = data.allBeatmapDataItems.Count(item => item is NoteData note && IsOrdinaryNote(note));
                var title = level.songName;
                if (GameplayCoreSceneSetupData.beatmapLevelData is FileSystemBeatmapLevelData local)
                {
                    var folder = Path.GetDirectoryName(local.songAudioClipPath);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        title = Path.GetFileName(folder);
                    }
                }

                var duration = ResolveDuration(level.songDuration,
                    GameplayCoreSceneSetupData.songAudioClip != null ? GameplayCoreSceneSetupData.songAudioClip.length : 0,
                    AudioTimeSyncController != null ? AudioTimeSyncController.songLength : 0);
                var song = new SongInfo
                {
                    title = title,
                    artist = level.songAuthorName,
                    difficulty = key.difficulty.ToString(),
                    noteCount = count,
                    durationSeconds = duration,
                    averageNps = AverageNps(count, duration)
                };
                var playerModel = Resources.FindObjectsOfTypeAll<PlayerDataModel>().FirstOrDefault();
                var stats = playerModel?.playerData?.TryGetPlayerLevelStatsData(in key);
                if (stats != null)
                {
                    song.previousPlays = stats.playCount;
                    if (stats.playCount > 0)
                    {
                        song.bestCombo = stats.maxCombo;
                    }

                    var maxScore = ScoreModel.ComputeMaxMultipliedScoreForBeatmap(data);
                    if (stats.validScore && stats.highScore > 0)
                    {
                        song.bestScore = stats.highScore;
                        if (maxScore > 0)
                        {
                            song.bestCompletionRate = Math.Max(0, Math.Min(100, 100.0 * stats.highScore / maxScore));
                        }
                    }
                }
                plugin.SessionState.Start(song);
            }
            catch (Exception ex)
            {
                plugin.Logger.Error("Cannot read current song: " + ex);
                plugin.SessionState.End();
            }
        }

        private static bool IsOrdinaryNote(NoteData note)
        {
            return note.scoringType == NoteData.ScoringType.Normal
                && (note.colorType == ColorType.ColorA || note.colorType == ColorType.ColorB);
        }

        private void ScoringFinished(ScoringElement element)
        {
            try
            {
                var note = element.noteData;
                if (note == null || !IsOrdinaryNote(note))
                {
                    return;
                }

                var hand = note.colorType == ColorType.ColorA ? Hand.Left : Hand.Right;
                var time = AudioTimeSyncController != null ? AudioTimeSyncController.songTime : element.time;
                if (element is GoodCutScoringElement good)
                {
                    plugin.SessionState.Record(hand, NoteOutcome.Good, good.cutScoreBuffer.centerDistanceCutScore, time);
                }
                else if (element is BadCutScoringElement)
                {
                    plugin.SessionState.Record(hand, NoteOutcome.BadCut, null, time);
                }
                else if (element is MissScoringElement)
                {
                    plugin.SessionState.Record(hand, NoteOutcome.Miss, null, time);
                }
            }
            catch (Exception ex)
            {
                plugin.Logger.Error("Cannot process note scoring: " + ex);
            }
        }

        private void LevelEnded()
        {
            plugin.SessionState.Finish();
        }

        private void SelectionActivated()
        {
            plugin.SessionState.SelectionShown(HideWhenResultsClose);
            HideWhenResultsClose = false;
        }

        private void ResultsContinuePressed()
        {
            HideWhenResultsClose = true;
        }

        private void ResultsActivated(ResultsViewController view)
        {
            ResultsStatsUi?.Dispose();
            ResultsStatsUi = null;
            var snapshot = plugin.SessionState.Snapshot_Sync;
            if (snapshot.state != "results" || snapshot.song == null)
            {
                return;
            }

            try
            {
                ResultsStatsUi = new ResultsStatsUi(view, plugin.SessionState);
            }
            catch (Exception ex)
            {
                plugin.Logger.Error("Cannot create results stats UI: " + ex);
            }
        }

        private void ResultsDeactivated()
        {
            ResultsStatsUi?.Dispose();
            ResultsStatsUi = null;
            if (HideWhenResultsClose)
            {
                SelectionActivated();
            }
        }

        public void Dispose()
        {
            ResultsStatsUi?.Dispose();
            ResultsStatsUi = null;
            Harmony.UnpatchSelf();
            DetachManager();
            if (ScoreController != null)
            {
                DetachScore(ScoreController);
            }

            if (PauseController != null)
            {
                DetachPause(PauseController);
            }
        }
        public static double? ResolveDuration(double levelDuration, double clipDuration, double controllerDuration)
        {
            if (Valid(levelDuration))
            {
                return levelDuration;
            }

            if (Valid(clipDuration))
            {
                return clipDuration;
            }

            if (Valid(controllerDuration))
            {
                return controllerDuration;
            }

            return null;
        }

        public static double? AverageNps(int noteCount, double? duration)
        {
            if (noteCount < 0 || !duration.HasValue || !Valid(duration.Value))
            {
                return null;
            }

            var average = noteCount / duration.Value;
            return double.IsNaN(average) || double.IsInfinity(average) ? null : (double?)average;
        }

        private static bool Valid(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        #region HarmonyPatch
        private void CaptureSetup(GameplayCoreSceneSetupData setup)
        {
            GameplayCoreSceneSetupData = setup;
        }

        private void AttachManager(StandardLevelGameplayManager manager)
        {
            if (StandardLevelGameplayManager != null)
            {
                DetachManager();
            }

            StandardLevelGameplayManager = manager;
            AudioTimeSyncController = AccessTools.Field(typeof(StandardLevelGameplayManager), "AudioTimeSyncControllerTimeSyncController")?.GetValue(manager) as AudioTimeSyncController;
            manager.levelDidStartEvent += LevelStarted;
            manager.levelFinishedEvent += LevelEnded;
            manager.levelFailedEvent += LevelEnded;
        }

        private void DetachManager()
        {
            if (StandardLevelGameplayManager == null)
            {
                return;
            }

            StandardLevelGameplayManager.levelDidStartEvent -= LevelStarted;
            StandardLevelGameplayManager.levelFinishedEvent -= LevelEnded;
            StandardLevelGameplayManager.levelFailedEvent -= LevelEnded;
            StandardLevelGameplayManager = null;
            AudioTimeSyncController = null;
            GameplayCoreSceneSetupData = null;
        }

        private void AttachScore(ScoreController score)
        {
            if (ScoreController != null)
            {
                ScoreController.scoringForNoteFinishedEvent -= ScoringFinished;
            }

            ScoreController = score;
            score.scoringForNoteFinishedEvent += ScoringFinished;
        }

        private void DetachScore(ScoreController score)
        {
            if (ScoreController != score)
            {
                return;
            }

            score.scoringForNoteFinishedEvent -= ScoringFinished;
            ScoreController = null;
        }

        private void AttachPause(PauseController pause)
        {
            if (PauseController != null)
            {
                DetachPause(PauseController);
            }

            PauseController = pause;
            pause.didPauseEvent += plugin.SessionState.Pause;
            pause.didResumeEvent += plugin.SessionState.Resume;
        }

        private void DetachPause(PauseController pause)
        {
            if (PauseController != pause)
            {
                return;
            }

            pause.didPauseEvent -= plugin.SessionState.Pause;
            pause.didResumeEvent -= plugin.SessionState.Resume;
            PauseController = null;
        }

        [HarmonyPatch(typeof(GameplayCoreSceneSetupData), "LoadTransformedBeatmapDataAsync")]
        private static class SetupPatch
        {
            private static void Prefix(GameplayCoreSceneSetupData __instance)
            {
                Plugin.Instance?.HookEngine?.CaptureSetup(__instance);
            }
        }

        [HarmonyPatch(typeof(StandardLevelGameplayManager), "Start")]
        private static class ManagerStartPatch
        {
            private static void Postfix(StandardLevelGameplayManager __instance)
            {
                Plugin.Instance?.HookEngine?.AttachManager(__instance);
            }
        }

        [HarmonyPatch(typeof(StandardLevelGameplayManager), "OnDestroy")]
        private static class ManagerDestroyPatch
        {
            private static void Prefix(StandardLevelGameplayManager __instance)
            {
                var bridge = Plugin.Instance?.HookEngine;
                if (bridge?.StandardLevelGameplayManager == __instance)
                {
                    bridge.DetachManager();
                }
            }
        }

        [HarmonyPatch(typeof(ScoreController), "Start")]
        private static class ScoreStartPatch
        {
            private static void Postfix(ScoreController __instance)
            {
                Plugin.Instance?.HookEngine?.AttachScore(__instance);
            }
        }

        [HarmonyPatch(typeof(ScoreController), "OnDestroy")]
        private static class ScoreDestroyPatch
        {
            private static void Prefix(ScoreController __instance)
            {
                Plugin.Instance?.HookEngine?.DetachScore(__instance);
            }
        }

        [HarmonyPatch(typeof(PauseController), "Start")]
        private static class PauseStartPatch
        {
            private static void Postfix(PauseController __instance)
            {
                Plugin.Instance?.HookEngine?.AttachPause(__instance);
            }
        }

        [HarmonyPatch(typeof(PauseController), "OnDestroy")]
        private static class PauseDestroyPatch
        {
            private static void Prefix(PauseController __instance)
            {
                Plugin.Instance?.HookEngine?.DetachPause(__instance);
            }
        }

        [HarmonyPatch(typeof(LevelSelectionNavigationController), "DidActivate")]
        private static class SelectionActivatedPatch
        {
            private static void Postfix()
            {
                Plugin.Instance?.HookEngine?.SelectionActivated();
            }
        }

        [HarmonyPatch(typeof(SoloFreePlayFlowCoordinator), "HandleResultsViewControllerContinueButtonPressed")]
        private static class ResultsContinuePatch
        {
            private static void Prefix()
            {
                Plugin.Instance?.HookEngine?.ResultsContinuePressed();
            }
        }

        [HarmonyPatch(typeof(ResultsViewController), "DidDeactivate")]
        private static class ResultsDeactivatedPatch
        {
            private static void Postfix()
            {
                Plugin.Instance?.HookEngine?.ResultsDeactivated();
            }
        }

        [HarmonyPatch(typeof(ResultsViewController), "DidActivate")]
        private static class ResultsActivatedPatch
        {
            private static void Postfix(ResultsViewController __instance)
            {
                Plugin.Instance?.HookEngine?.ResultsActivated(__instance);
            }
        }
        #endregion
    }
}
