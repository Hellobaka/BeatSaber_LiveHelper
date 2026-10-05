namespace LiveHelper.Models
{
    public class OverlaySnapshot
    {
        public string state { get; set; } = "hidden";

        public SongInfo? song { get; set; }

        public HandInfo left { get; set; } = new HandInfo();

        public HandInfo right { get; set; } = new HandInfo();
    }
}
