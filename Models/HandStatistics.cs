namespace LiveHelper.Models
{
    public class HandStatistics
    {
        public int Completed { get; set; }

        public int Missed { get; set; }

        public int AccuracyCount { get; set; }

        public double AccuracySum { get; set; }

        public double? LastGoodTime { get; set; }

        public double? CurrentNps { get; set; }

        public HandInfo ToInfo()
        {
            var total = Completed + Missed;
            return new HandInfo
            {
                completed = Completed,
                missed = Missed,
                missRate = total == 0 ? 0 : 100.0 * Missed / total,
                averageAccuracy = AccuracyCount == 0 ? null : 100.0 * AccuracySum / (15.0 * AccuracyCount),
                currentNps = CurrentNps
            };
        }
    }
}
