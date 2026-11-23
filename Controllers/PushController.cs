using System.Security.Claims;
using ChurchMinistry.Data;
using ChurchMinistry.Models;
using ChurchMinistry.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChurchMinistry.Controllers
{
    // Push alerts: wwwroot/js/push.js registers this phone/browser here after the user taps "Turn on".
    // The alerts themselves are sent by PushSenderService (Services.cs) whenever a Notification is saved.
    [Authorize]
    public class PushController : Controller
    {
        public record SubscriptionDto(string? Endpoint, string? P256dh, string? Auth);

        private readonly ApplicationDbContext _db;
        private readonly INotificationService _notifications;

        public PushController(ApplicationDbContext db, INotificationService notifications)
        {
            _db = db; _notifications = notifications;
        }

        private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Subscribe([FromBody] SubscriptionDto dto, bool welcome = false)
        {
            if (string.IsNullOrWhiteSpace(dto.Endpoint) || string.IsNullOrWhiteSpace(dto.P256dh) || string.IsNullOrWhiteSpace(dto.Auth)
                || dto.Endpoint.Length > 500 || dto.P256dh.Length > 200 || dto.Auth.Length > 100
                || !Uri.TryCreate(dto.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                return BadRequest();

            // One row per browser. If someone else used to be signed in on this phone, it now belongs to the current user.
            var device = await _db.PushDevices.FirstOrDefaultAsync(d => d.Endpoint == dto.Endpoint);
            if (device == null)
            {
                device = new PushDevice { Endpoint = dto.Endpoint };
                _db.PushDevices.Add(device);
            }
            device.UserId = UserId;
            device.P256dh = dto.P256dh;
            device.Auth = dto.Auth;
            var ua = Request.Headers.UserAgent.ToString();
            device.UserAgent = ua.Length > 300 ? ua[..300] : ua;
            await _db.SaveChangesAsync();

            // First time on: a confirmation alert, so the user sees it working straight away
            if (welcome)
            {
                var memberId = await _db.Members.Where(m => m.UserId == UserId).Select(m => (int?)m.Id).FirstOrDefaultAsync();
                if (memberId != null)
                    await _notifications.CreateAsync(memberId.Value, "Alerts are on",
                        "You'll get alerts on this device for messages, prayers, meetings and more.", "/Notifications");
            }
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unsubscribe([FromBody] SubscriptionDto dto)
        {
            if (!string.IsNullOrWhiteSpace(dto.Endpoint))
                await _db.PushDevices.Where(d => d.Endpoint == dto.Endpoint && d.UserId == UserId).ExecuteDeleteAsync();
            return Ok();
        }
    }
}
