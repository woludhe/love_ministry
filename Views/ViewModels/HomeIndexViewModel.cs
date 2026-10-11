// Add this class to ViewModels/ViewModels.cs

namespace ChurchMinistry.ViewModels
{
    public class HomeIndexViewModel
    {
        public List<ChurchMinistry.Models.Sermon> RecentSermons { get; set; } = new();
        public ChurchMinistry.Models.Sermon? LiveSermon { get; set; }
        public List<ChurchMinistry.Models.ChurchEvent> UpcomingEvents { get; set; } = new();
        public ChurchMinistry.Models.ChurchEvent? NextEvent { get; set; }
        public int TotalMembers { get; set; }
        public List<ChurchMinistry.Models.PrayerRequest> PrayerWall { get; set; } = new();
        public List<ChurchMinistry.Models.PaymentMethodConfig> ActivePaymentMethods { get; set; } = new();
    }
}
