namespace LiveHelper.Models
{
    public class HandInfo
    {
        public int completed { get; set; }

        public int missed { get; set; }

        public double missRate { get; set; }

        public double? averageAccuracy { get; set; }

        public double? currentNps { get; set; }
    }
}
