namespace LiveHelper.Models
{
    public class SongInfo
    {
        public string title { get; set; } = "";

        public string artist { get; set; } = "";

        public string difficulty { get; set; } = "";

        public int noteCount { get; set; }

        public double? durationSeconds { get; set; }

        public double? averageNps { get; set; }

        public int previousPlays { get; set; }

        public double? bestCompletionRate { get; set; }

        public int? bestCombo { get; set; }

        public int? bestScore { get; set; }
    }

}
