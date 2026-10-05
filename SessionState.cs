using LiveHelper.Models;
using System;

namespace LiveHelper
{
    public class SessionState
    {
        public event Action<OverlaySnapshot>? OnSnapshotChanged;

        private readonly object syncLock = new();

        private HandStatistics LeftHandStatistics { get; set; } = new();

        private HandStatistics RightHandStatistics { get; set; } = new();

        private SongInfo? CurrentSong { get; set; }

        private SessionStatus CurrentState { get; set; } = SessionStatus.Hidden;

        private OverlaySnapshot CurrentSnapshot { get; set; } = new();

        public OverlaySnapshot Snapshot_Sync
        {
            get
            {
                lock (syncLock)
                {
                    return CurrentSnapshot;
                }
            }
        }

        public void Start(SongInfo song)
        {
            if (song == null)
            {
                throw new ArgumentNullException(nameof(song));
            }

            OverlaySnapshot snapshot;
            lock (syncLock)
            {
                LeftHandStatistics = new HandStatistics();
                RightHandStatistics = new HandStatistics();
                CurrentSong = song;
                CurrentState = SessionStatus.Playing;
                snapshot = Publish();
            }
            OnSnapshotChanged?.Invoke(snapshot);
        }

        public void Pause()
        {
            ChangeState(SessionStatus.Playing, SessionStatus.Paused);
        }

        public void Resume()
        {
            ChangeState(SessionStatus.Paused, SessionStatus.Playing);
        }

        public void Finish()
        {
            OverlaySnapshot snapshot;
            lock (syncLock)
            {
                if (CurrentState != SessionStatus.Playing && CurrentState != SessionStatus.Paused)
                {
                    return;
                }

                CurrentState = SessionStatus.Results;
                snapshot = Publish();
            }
            OnSnapshotChanged?.Invoke(snapshot);
        }

        public void SelectionShown(bool leavingResults)
        {
            lock (syncLock)
            {
                if (CurrentState == SessionStatus.Results && !leavingResults)
                {
                    return;
                }
            }

            End();
        }

        public void End()
        {
            OverlaySnapshot snapshot;
            lock (syncLock)
            {
                CurrentState = SessionStatus.Hidden;
                CurrentSong = null;
                LeftHandStatistics = new HandStatistics();
                RightHandStatistics = new HandStatistics();
                snapshot = Publish();
            }
            OnSnapshotChanged?.Invoke(snapshot);
        }

        public void Record(Hand hand, NoteOutcome outcome, int? centerScore, double songTime)
        {
            OverlaySnapshot snapshot;
            lock (syncLock)
            {
                if (CurrentState != SessionStatus.Playing)
                {
                    return;
                }

                var target = hand == Hand.Left ? LeftHandStatistics : RightHandStatistics;
                if (outcome == NoteOutcome.Good)
                {
                    target.Completed++;
                    if (centerScore.HasValue && centerScore.Value >= 0 && centerScore.Value <= 15)
                    {
                        target.AccuracySum += centerScore.Value;
                        target.AccuracyCount++;
                    }
                    if (double.IsNaN(songTime) || double.IsInfinity(songTime))
                    {
                        target.CurrentNps = null;
                        target.LastGoodTime = null;
                    }
                    else
                    {
                        if (target.LastGoodTime.HasValue && songTime > target.LastGoodTime.Value)
                        {
                            var interval = songTime - target.LastGoodTime.Value;
                            var nps = 1.0 / interval;
                            target.CurrentNps = double.IsInfinity(interval) || double.IsNaN(nps) || double.IsInfinity(nps)
                                ? null : (double?)nps;
                        }

                        target.LastGoodTime = songTime;
                    }
                }
                else
                {
                    target.Missed++;
                }

                snapshot = Publish();
            }
            OnSnapshotChanged?.Invoke(snapshot);
        }

        private void ChangeState(SessionStatus from, SessionStatus to)
        {
            OverlaySnapshot snapshot;
            lock (syncLock)
            {
                if (CurrentState != from)
                {
                    return;
                }

                CurrentState = to;
                snapshot = Publish();
            }
            OnSnapshotChanged?.Invoke(snapshot);
        }

        private OverlaySnapshot Publish()
        {
            CurrentSnapshot = new OverlaySnapshot
            {
                state = CurrentState switch
                {
                    SessionStatus.Hidden => "hidden",
                    SessionStatus.Playing => "playing",
                    SessionStatus.Paused => "paused",
                    SessionStatus.Results => "results",
                    _ => throw new InvalidOperationException("Unknown session state.")
                },
                song = CurrentSong,
                left = LeftHandStatistics.ToInfo(),
                right = RightHandStatistics.ToInfo()
            };
            return CurrentSnapshot;
        }
    }
}
