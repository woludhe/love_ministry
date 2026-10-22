// Add this class to ViewModels/ViewModels.cs

namespace ChurchMinistry.ViewModels
{
    public class HomeIndexViewModel
    {
        public List<ChurchMinistry.Models.Sermon> RecentSermons { get; set; } = new();
        public ChurchMinistry.Models.Sermon? LiveSermon { get; set; }
        public List<ChurchMinistry.Models.ChurchEvent> UpcomingEvents { get; set; } = new();
        public int TotalMembers { get; set; }
        public List<ChurchMinistry.Models.PrayerRequest> PrayerWall { get; set; } = new();
        public List<ChurchMinistry.Models.PaymentMethodConfig> ActivePaymentMethods { get; set; } = new();
        public List<UpcomingItem> UpcomingItems { get; set; } = new();
    }

    // A future event or scheduled sermon, shown together on the home page
    public class UpcomingItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public DateTime Date { get; set; }
        public string? Subtitle { get; set; }      // event Location, or sermon Speaker
        public bool IsSermon { get; set; }
        public string? DeliveryMode { get; set; }  // sermon only: live | meeting | video | audio
    }
}
