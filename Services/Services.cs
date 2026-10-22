using ChurchMinistry.Data;
using ChurchMinistry.Models;
using ChurchMinistry.ViewModels;
using MailKit.Net.Smtp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MimeKit;
using QRCoder;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Flutterwave.Net;
using Newtonsoft.Json.Linq;

namespace ChurchMinistry.Services
{
    // ─── Email Service ───
    public interface IEmailService
    {
        Task SendEmailAsync(string to, string subject, string htmlBody, string? name = null);
        Task SendBulkAsync(List<string> recipients, string subject, string htmlBody);
        Task SendReceiptAsync(Giving giving, Member? member);
        Task SendWelcomeAsync(Member member);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        public EmailService(IConfiguration config) { _config = config; }

        public async Task SendEmailAsync(string to, string subject, string htmlBody, string? name = null)
        {
            var settings = _config.GetSection("EmailSettings");
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(settings["FromName"], settings["FromEmail"]));
            message.To.Add(new MailboxAddress(name ?? to, to));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = WrapInTemplate(subject, htmlBody) };

            using var client = new SmtpClient();
            // EnableSsl: port 465 uses TLS from the start; other ports (587) upgrade with STARTTLS.
            // Without SSL (e.g. a local relay on port 25) the connection stays plain.
            var port = int.Parse(settings["SmtpPort"]!);
            var enableSsl = bool.TryParse(settings["EnableSsl"], out var ssl) && ssl;
            var socketOptions = !enableSsl ? MailKit.Security.SecureSocketOptions.None
                : port == 465 ? MailKit.Security.SecureSocketOptions.SslOnConnect
                : MailKit.Security.SecureSocketOptions.StartTls;
            await client.ConnectAsync(settings["SmtpHost"], port, socketOptions);
            await client.AuthenticateAsync(settings["SmtpUser"], settings["SmtpPassword"]);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        public async Task SendBulkAsync(List<string> recipients, string subject, string htmlBody)
        {
            foreach (var r in recipients)
                await SendEmailAsync(r, subject, htmlBody);
        }

        public async Task SendReceiptAsync(Giving giving, Member? member)
        {
            var email = member?.Email ?? giving.AnonymousName ?? "";
            if (string.IsNullOrEmpty(email)) return;
            var html = $@"
                <h2>Giving Receipt</h2>
                <p>Thank you for your generosity!</p>
                <table>
                    <tr><td><b>Amount:</b></td><td>{giving.Currency} {giving.Amount:N2}</td></tr>
                    <tr><td><b>Type:</b></td><td>{giving.GivingType}</td></tr>
                    <tr><td><b>Method:</b></td><td>{giving.PaymentMethod}</td></tr>
                    <tr><td><b>Date:</b></td><td>{giving.GivingDate:dd MMM yyyy}</td></tr>
                    <tr><td><b>Reference:</b></td><td>{giving.TransactionId}</td></tr>
                </table>";
            await SendEmailAsync(email, "Your Giving Receipt", html, member?.FullName);
        }

        public async Task SendWelcomeAsync(Member member)
        {
            var html = $@"
                <h2>Welcome to our Church Family, {member.FirstName}!</h2>
                <p>We are so glad you've joined us. Your membership has been confirmed.</p>
                <p>You can now access your member portal, view sermons, submit prayer requests and connect with our community.</p>
                <a href='#' style='background:#6366f1;color:white;padding:12px 24px;border-radius:6px;text-decoration:none;'>Visit Member Portal</a>";
            await SendEmailAsync(member.Email, "Welcome to Love Fellowship Ministries!", html, member.FullName);
        }

        private string WrapInTemplate(string subject, string body) => $@"
<!DOCTYPE html><html><body style='font-family:sans-serif;max-width:600px;margin:auto;padding:20px'>
<div style='background:#6366f1;padding:20px;border-radius:8px 8px 0 0;text-align:center'>
  <h1 style='color:white;margin:0'>Love Fellowship Ministries</h1>
</div>
<div style='border:1px solid #e5e7eb;padding:30px;border-radius:0 0 8px 8px'>
{body}
<hr style='margin:24px 0'/>
<p style='color:#6b7280;font-size:12px;text-align:center'>Love Fellowship Ministries | Kampala, Uganda<br/>
<a href='#' style='color:#6366f1'>Unsubscribe</a></p>
</div></body></html>";
    }

    // ─── QR Code Service ───
    public interface IQrCodeService
    {
        string GenerateQrCode(string data);
        string GetMemberQrData(int memberId, string email);
    }

    public class QrCodeService : IQrCodeService
    {
        private readonly IWebHostEnvironment _env;
        public QrCodeService(IWebHostEnvironment env) { _env = env; }

        public string GenerateQrCode(string data)
        {
            using var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(data, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrData);
            var bytes = qrCode.GetGraphic(20);
            return Convert.ToBase64String(bytes);
        }

        public string GetMemberQrData(int memberId, string email)
            => $"CHURCH_MEMBER|{memberId}|{email}|{DateTime.UtcNow:yyyyMMdd}";
    }

    // ─── Payment Service (Flutterwave) ───
    public interface IPaymentService
    {
        Task<PaymentInitResult> InitiateMobileMoneyPaymentAsync(GivingViewModel model, string memberId, string txRef);
        Task<bool> VerifyTransactionAsync(string transactionId);
    }

    public class PaymentService : IPaymentService
    {
        private readonly IConfiguration _config;
        private readonly HttpClient _http;

        public PaymentService(IConfiguration config, IHttpClientFactory httpFactory)
        {
            _config = config;
            _http = httpFactory.CreateClient();
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", _config["Flutterwave:SecretKey"]);
            _http.BaseAddress = new Uri(_config["Flutterwave:BaseUrl"]!);
        }

        public async Task<PaymentInitResult> InitiateMobileMoneyPaymentAsync(
            GivingViewModel model, string memberId, string txRef)
        {
            var currency = model.Currency;
            var endpoint = model.PaymentMethod switch
            {
                PaymentMethod.MTNMobileMoney => "charges?type=mobile_money_uganda",
                PaymentMethod.AirtelMoney => "charges?type=mobile_money_uganda",
                PaymentMethod.MPesa => "charges?type=mpesa",
                _ => ""
            };

            if (string.IsNullOrEmpty(endpoint))
                return new PaymentInitResult { Success = false, Message = "Use Flutterwave checkout for card payments" };

            var payload = new
            {
                phone_number = model.PhoneNumber,
                amount = model.Amount,
                currency,
                email = $"{memberId}@church.org",
                tx_ref = txRef,
                network = model.PaymentMethod == PaymentMethod.AirtelMoney ? "AIRTEL" : "MTN",
                fullname = "Church Member",
                client_ip = "192.168.1.1",
                device_fingerprint = txRef,
                meta = new { member_id = memberId, giving_type = model.GivingType.ToString() }
            };

            var res = await _http.PostAsync(endpoint,
                new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
            var json = await res.Content.ReadAsStringAsync();
            dynamic result = JsonConvert.DeserializeObject<dynamic>(json)!;

            return new PaymentInitResult
            {
                Success = result?.status == "success",
                Message = result?.message ?? "",
                FlutterwaveRef = result?.data?.flw_ref ?? txRef
            };
        }

        public async Task<bool> VerifyTransactionAsync(string transactionId)
        {
            var res = await _http.GetAsync($"transactions/{transactionId}/verify");
            var json = await res.Content.ReadAsStringAsync();
            var result = JObject.Parse(json);
            var status = result["status"]?.ToString(); // ✅ works correctly
            return (status == "success" || status == "successful");
        }
    }

    public class PaymentInitResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string? FlutterwaveRef { get; set; }
        public string? PaymentLink { get; set; }
    }

    // ─── SMS Service (Africa's Talking) ───
    public interface ISmsService
    {
        Task SendSmsAsync(string phone, string message);
        Task SendBulkSmsAsync(List<string> phones, string message);
    }

    public class SmsService : ISmsService
    {
        private readonly IConfiguration _config;
        public SmsService(IConfiguration config) { _config = config; }

        public async Task SendSmsAsync(string phone, string message)
        {
            using var client = new HttpClient();
            var payload = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", _config["AfricasTalking:Username"]!),
                new KeyValuePair<string, string>("to", phone),
                new KeyValuePair<string, string>("message", message),
            });
            client.DefaultRequestHeaders.Add("apiKey", _config["AfricasTalking:ApiKey"]);
            await client.PostAsync("https://api.africastalking.com/version1/messaging", payload);
        }

        public async Task SendBulkSmsAsync(List<string> phones, string message)
        {
            await SendSmsAsync(string.Join(",", phones), message);
        }
    }

    // ─── LiveKit (meeting media server) ───
    public interface ILiveKitService
    {
        string Url { get; }
        // LiveKit room name for a meeting. The optional prefix keeps a development copy
        // of the site out of live rooms when both share one LiveKit server.
        string RoomName(string roomCode);
        // Access token for joining a LiveKit room. A hidden participant (a Live page
        // viewer) receives media but isn't listed to anyone else in the room.
        string CreateJoinToken(string roomName, string identity, string displayName,
            string? metadata, bool canPublish, TimeSpan? validFor = null, bool hidden = false);

        // Server-side controls. These are best effort: a failure is logged and
        // never stops the meeting hub, which still tells the browsers itself.
        Task RemoveParticipantAsync(string roomName, string identity);
        Task MuteMicrophoneAsync(string roomName, string identity);
        // Disconnects everyone still in the room
        Task DeleteRoomAsync(string roomName);
    }

    public class LiveKitService : ILiveKitService
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _http;
        private readonly ILogger<LiveKitService> _log;
        public LiveKitService(IConfiguration config, IHttpClientFactory http, ILogger<LiveKitService> log)
        {
            _config = config; _http = http; _log = log;
        }

        public string Url => _config["LiveKit:Url"]!;

        public string RoomName(string roomCode) => (_config["LiveKit:RoomPrefix"] ?? "") + roomCode;

        public string CreateJoinToken(string roomName, string identity, string displayName,
            string? metadata, bool canPublish, TimeSpan? validFor = null, bool hidden = false)
        {
            var now = DateTimeOffset.UtcNow;
            var claims = new Dictionary<string, object?>
            {
                ["iss"]  = _config["LiveKit:ApiKey"],
                ["sub"]  = identity,
                ["name"] = displayName,
                ["nbf"]  = now.ToUnixTimeSeconds(),
                // Only needed to join: LiveKit sends connected clients a renewed token every
                // 5 minutes, so a short life limits what a leaked or old token can do
                ["exp"]  = now.Add(validFor ?? TimeSpan.FromMinutes(5)).ToUnixTimeSeconds(),
                ["metadata"] = metadata,
                ["video"] = new Dictionary<string, object>
                {
                    ["room"]           = roomName,
                    ["roomJoin"]       = true,
                    ["canSubscribe"]   = true,
                    ["canPublish"]     = canPublish,
                    ["canPublishData"] = canPublish,
                    ["hidden"]         = hidden
                }
            };
            return SignJwt(claims);
        }

        // A removed client may reconnect on its own with the latest token the server gave
        // it. So permissions are revoked first: that cuts their audio/video straight away
        // and the server sends the client a refreshed token with no permissions, so a
        // reconnect gets them nothing. The pause lets that token reach them before removal.
        public async Task RemoveParticipantAsync(string roomName, string identity)
        {
            var revoked = await CallAsync(roomName, "UpdateParticipant", new
            {
                room = roomName, identity,
                permission = new { can_subscribe = false, can_publish = false, can_publish_data = false }
            });
            if (revoked == null) return;   // not in the media room (or the server is unreachable)
            await Task.Delay(1000);
            await CallAsync(roomName, "RemoveParticipant", new { room = roomName, identity });
        }

        public async Task MuteMicrophoneAsync(string roomName, string identity)
        {
            var info = await CallAsync(roomName, "GetParticipant", new { room = roomName, identity });
            var mics = (info?["tracks"] as JArray ?? new JArray())
                .Where(t => (string?)t["source"] == "MICROPHONE" && (bool?)t["muted"] != true);
            foreach (var t in mics)
                await CallAsync(roomName, "MutePublishedTrack",
                    new { room = roomName, identity, track_sid = (string?)t["sid"], muted = true });
        }

        // As with removal, everyone's permissions are revoked first: a client that reconnects
        // after the room closes lands in a new, empty room with nothing to send or receive.
        public async Task DeleteRoomAsync(string roomName)
        {
            var list = await CallAsync(roomName, "ListParticipants", new { room = roomName });
            var identities = (list?["participants"] as JArray ?? new JArray())
                .Select(p => (string?)p["identity"]).Where(i => i != null).ToList();
            await Task.WhenAll(identities.Select(identity => CallAsync(roomName, "UpdateParticipant", new
            {
                room = roomName, identity,
                permission = new { can_subscribe = false, can_publish = false, can_publish_data = false }
            })));
            if (identities.Count > 0) await Task.Delay(1000);
            await CallAsync(roomName, "DeleteRoom", new { room = roomName });
        }

        // LiveKit's server API (Twirp over HTTP, JSON bodies), authorised by a short-lived
        // admin token for this one room. "Not found" (room or person already gone) is normal.
        private async Task<JObject?> CallAsync(string roomName, string method, object body)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var token = SignJwt(new Dictionary<string, object?>
                {
                    ["iss"] = _config["LiveKit:ApiKey"],
                    ["nbf"] = now.ToUnixTimeSeconds(),
                    ["exp"] = now.AddMinutes(5).ToUnixTimeSeconds(),
                    ["video"] = new Dictionary<string, object>
                    {
                        ["room"] = roomName, ["roomAdmin"] = true, ["roomCreate"] = true
                    }
                });
                var baseUrl = System.Text.RegularExpressions.Regex.Replace(Url.TrimEnd('/'), "^ws", "http");
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/twirp/livekit.RoomService/{method}")
                {
                    Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json")
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var client = _http.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                using var res = await client.SendAsync(req);
                var text = await res.Content.ReadAsStringAsync();
                if (res.IsSuccessStatusCode) return JObject.Parse(text);
                if (res.StatusCode != System.Net.HttpStatusCode.NotFound)
                    _log.LogWarning("LiveKit {Method} for room {Room} failed: {Status} {Body}",
                        method, roomName, (int)res.StatusCode, text);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "LiveKit {Method} for room {Room} failed", method, roomName);
            }
            return null;
        }

        // LiveKit tokens are HS256 JWTs signed with the API secret
        private string SignJwt(Dictionary<string, object?> claims)
        {
            static string B64(byte[] b) => Microsoft.AspNetCore.WebUtilities.Base64UrlTextEncoder.Encode(b);
            var header  = B64(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
            var payload = B64(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(claims,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore })));
            using var hmac = new System.Security.Cryptography.HMACSHA256(
                Encoding.UTF8.GetBytes(_config["LiveKit:ApiSecret"]!));
            var sig = B64(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{header}.{payload}")));
            return $"{header}.{payload}.{sig}";
        }
    }

    // ─── Attendance Service ───
    public interface IAttendanceService
    {
        Task<bool> CheckInAsync(string qrData, int sessionId);
        Task<AttendanceRecord?> ManualCheckInAsync(int memberId, int sessionId);
        Task CloseSermonAttendanceAsync(IEnumerable<int> sermonIds);
    }

    public class AttendanceService : IAttendanceService
    {
        private readonly ApplicationDbContext _db;
        public AttendanceService(ApplicationDbContext db) { _db = db; }

        public async Task<bool> CheckInAsync(string qrData, int sessionId)
        {
            var parts = qrData.Split('|');
            if (parts.Length < 2 || !int.TryParse(parts[1], out int memberId)) return false;
            var record = await ManualCheckInAsync(memberId, sessionId);
            return record != null;
        }

        public async Task<AttendanceRecord?> ManualCheckInAsync(int memberId, int sessionId)
        {
            if (await _db.AttendanceRecords.AnyAsync(a => a.MemberId == memberId && a.ServiceSessionId == sessionId))
                return null;

            var record = new AttendanceRecord
            {
                MemberId = memberId,
                ServiceSessionId = sessionId,
                CheckInTime = DateTime.UtcNow,
                CheckInMethod = CheckInMethod.QR
            };
            _db.AttendanceRecords.Add(record);
            await _db.SaveChangesAsync();
            return record;
        }

        // The live stream stopped or the meeting ended for all: everyone still
        // watching leaves now, rather than whenever their browser disconnects
        public async Task CloseSermonAttendanceAsync(IEnumerable<int> sermonIds)
        {
            var ids = sermonIds.Distinct().ToList();
            if (ids.Count == 0) return;
            var now = DateTime.UtcNow;
            await _db.SermonAttendances
                .Where(a => ids.Contains(a.SermonId) && a.LeftAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.LeftAt, now));
        }
    }

    // ─── Notification Service ───
    public interface INotificationService
    {
        Task CreateAsync(int memberId, string title, string message, string? link = null);
        Task<List<Notification>> GetUnreadAsync(int memberId);
    }

    public class NotificationService : INotificationService
    {
        private readonly ApplicationDbContext _db;
        public NotificationService(ApplicationDbContext db) { _db = db; }

        public async Task CreateAsync(int memberId, string title, string message, string? link = null)
        {
            _db.Notifications.Add(new Notification { MemberId = memberId, Title = title, Message = message, Link = link });
            await _db.SaveChangesAsync();
        }

        public async Task<List<Notification>> GetUnreadAsync(int memberId)
            => await _db.Notifications.Where(n => n.MemberId == memberId && !n.IsRead)
                .OrderByDescending(n => n.CreatedAt).ToListAsync();
    }

    // ─── Member activity ───
    // Behind-the-scenes usage log (logins, live viewing, prayer, meetings, community, giving…)
    // plus the points that rank the top participants. Admins and Pastors are never ranked.
    public record TopParticipant(int MemberId, int Points);

    public interface IActivityService
    {
        // Never throws: tracking must not break the action being tracked.
        // oncePerDay skips the write if the same member/type/reference was already logged today.
        Task LogAsync(int memberId, MemberActivityType type, string? description = null,
                      int? referenceId = null, bool oncePerDay = false);
        Task LogForUserAsync(string? userId, MemberActivityType type, string? description = null,
                             int? referenceId = null, bool oncePerDay = false);
        Task<List<TopParticipant>> GetTopParticipantsAsync();
        Task<Dictionary<int, int>> GetPointsAsync(IEnumerable<int> memberIds);
    }

    public class ActivityService : IActivityService
    {
        public const int TopCount = 10;
        public const int RankingDays = 30;
        private const string TopCacheKey = "top-participants";

        public static readonly IReadOnlyDictionary<MemberActivityType, int> Weights =
            new Dictionary<MemberActivityType, int>
            {
                [MemberActivityType.Login] = 1,
                [MemberActivityType.WatchLive] = 3,
                [MemberActivityType.WatchSermon] = 2,
                [MemberActivityType.PrayerRequest] = 3,
                [MemberActivityType.PrayedFor] = 1,
                [MemberActivityType.MeetingJoined] = 3,
                [MemberActivityType.ForumPost] = 3,
                [MemberActivityType.ForumReply] = 2,
                [MemberActivityType.CommunityChat] = 1,
                [MemberActivityType.Giving] = 5,
                [MemberActivityType.EventRegistration] = 3,
                [MemberActivityType.ProfileUpdated] = 1,
            };

        public static string Label(MemberActivityType type) => type switch
        {
            MemberActivityType.Login => "Logged in",
            MemberActivityType.WatchLive => "Watched live",
            MemberActivityType.WatchSermon => "Watched a sermon",
            MemberActivityType.PrayerRequest => "Prayer request",
            MemberActivityType.PrayedFor => "Prayed for someone",
            MemberActivityType.MeetingJoined => "Joined a meeting",
            MemberActivityType.ForumPost => "Community post",
            MemberActivityType.ForumReply => "Community reply",
            MemberActivityType.CommunityChat => "Community chat",
            MemberActivityType.Giving => "Gave",
            MemberActivityType.EventRegistration => "Event registration",
            MemberActivityType.ProfileUpdated => "Updated profile",
            _ => type.ToString()
        };

        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ActivityService> _log;

        public ActivityService(ApplicationDbContext db, IMemoryCache cache, ILogger<ActivityService> log)
        {
            _db = db; _cache = cache; _log = log;
        }

        public async Task LogAsync(int memberId, MemberActivityType type, string? description = null,
                                   int? referenceId = null, bool oncePerDay = false)
        {
            try
            {
                if (oncePerDay)
                {
                    var today = DateTime.UtcNow.Date;
                    if (await _db.MemberActivities.AnyAsync(a => a.MemberId == memberId && a.Type == type
                            && a.ReferenceId == referenceId && a.CreatedAt >= today))
                        return;
                }

                if (description?.Length > 300) description = description[..300];
                _db.MemberActivities.Add(new MemberActivity
                {
                    MemberId = memberId,
                    Type = type,
                    Description = description,
                    ReferenceId = referenceId
                });
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not log {Type} activity for member {MemberId}", type, memberId);
            }
        }

        public async Task LogForUserAsync(string? userId, MemberActivityType type, string? description = null,
                                          int? referenceId = null, bool oncePerDay = false)
        {
            if (string.IsNullOrEmpty(userId)) return;
            var memberId = await _db.Members.Where(m => m.UserId == userId)
                .Select(m => (int?)m.Id).FirstOrDefaultAsync();
            if (memberId != null)
                await LogAsync(memberId.Value, type, description, referenceId, oncePerDay);
        }

        // Top 10 by points over the last 30 days, cached briefly since it's shown on many pages
        public async Task<List<TopParticipant>> GetTopParticipantsAsync()
        {
            if (_cache.TryGetValue(TopCacheKey, out List<TopParticipant>? cached) && cached != null)
                return cached;

            var since = DateTime.UtcNow.AddDays(-RankingDays);
            var staffUserIds = from ur in _db.UserRoles
                               join r in _db.Roles on ur.RoleId equals r.Id
                               where r.Name == "Admin" || r.Name == "Pastor"
                               select ur.UserId;

            var counts = await _db.MemberActivities
                .Where(a => a.CreatedAt >= since
                         && (a.Member.UserId == null || !staffUserIds.Contains(a.Member.UserId)))
                .GroupBy(a => new { a.MemberId, a.Type })
                .Select(g => new { g.Key.MemberId, g.Key.Type, Count = g.Count() })
                .ToListAsync();

            var top = ToPoints(counts.Select(c => (c.MemberId, c.Type, c.Count)))
                .Where(kv => kv.Value > 0)
                .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
                .Take(TopCount)
                .Select(kv => new TopParticipant(kv.Key, kv.Value))
                .ToList();

            _cache.Set(TopCacheKey, top, TimeSpan.FromMinutes(5));
            return top;
        }

        // Points over the last 30 days for the given members (members with none are left out)
        public async Task<Dictionary<int, int>> GetPointsAsync(IEnumerable<int> memberIds)
        {
            var ids = memberIds.Distinct().ToList();
            if (ids.Count == 0) return new();
            var since = DateTime.UtcNow.AddDays(-RankingDays);
            var counts = await _db.MemberActivities
                .Where(a => a.CreatedAt >= since && ids.Contains(a.MemberId))
                .GroupBy(a => new { a.MemberId, a.Type })
                .Select(g => new { g.Key.MemberId, g.Key.Type, Count = g.Count() })
                .ToListAsync();
            return ToPoints(counts.Select(c => (c.MemberId, c.Type, c.Count)));
        }

        private static Dictionary<int, int> ToPoints(IEnumerable<(int MemberId, MemberActivityType Type, int Count)> counts) =>
            counts.GroupBy(c => c.MemberId)
                  .ToDictionary(g => g.Key, g => g.Sum(c => c.Count * Weights.GetValueOrDefault(c.Type, 1)));
    }

    // ─── Profile completion ───
    // Ten items, 10% each. Gender and marital status aren't counted: they always have a value.
    public static class ProfileCompletion
    {
        // Field = the profile form input it maps to (highlighted on the edit page when missing)
        private static (string Field, string Label, bool Done)[] Checks(Member m) => new[]
        {
            ("FirstName", "Name & email", !string.IsNullOrWhiteSpace(m.FirstName) && !string.IsNullOrWhiteSpace(m.Email)),
            ("Phone", "Phone number", !string.IsNullOrWhiteSpace(m.Phone)),
            ("ProfilePhotoFile", "Profile photo", !string.IsNullOrWhiteSpace(m.ProfilePhoto)),
            ("DateOfBirth", "Date of birth", m.DateOfBirth != null),
            ("Address", "Address", !string.IsNullOrWhiteSpace(m.Address)),
            ("City", "City", !string.IsNullOrWhiteSpace(m.City)),
            ("Country", "Country", !string.IsNullOrWhiteSpace(m.Country)),
            ("Occupation", "Occupation", !string.IsNullOrWhiteSpace(m.Occupation)),
            ("EmergencyContact", "Emergency contact", !string.IsNullOrWhiteSpace(m.EmergencyContact)),
            ("EmergencyPhone", "Emergency phone", !string.IsNullOrWhiteSpace(m.EmergencyPhone)),
        };

        public static (int Percent, List<string> Missing) For(Member m)
        {
            var checks = Checks(m);
            var missing = checks.Where(c => !c.Done).Select(c => c.Label).ToList();
            return ((checks.Length - missing.Count) * 100 / checks.Length, missing);
        }

        public static List<string> MissingFields(Member m) =>
            Checks(m).Where(c => !c.Done).Select(c => c.Field).ToList();
    }

    // ─── Recurring meeting series ───
    // Each week of a recurring meeting is its own MeetingRoom (own code, participants,
    // chat, attendance), linked by SeriesId. Only the next one exists at any time.
    public interface IMeetingSeriesService
    {
        // Creates the series' next meeting (next day or week) after this one, if it doesn't exist yet
        Task<MeetingRoom?> CreateNextAsync(MeetingRoom current);
        // Moves every series on whose latest meeting has ended or was missed
        Task CatchUpAsync();
        string GenerateRoomCode(MeetingType type);
    }

    public class MeetingSeriesService : IMeetingSeriesService
    {
        // A scheduled meeting this long past its start without being opened counts as missed
        private static readonly TimeSpan MissedAfter = TimeSpan.FromHours(3);

        private readonly ApplicationDbContext _db;
        private readonly INotificationService _notifications;

        public MeetingSeriesService(ApplicationDbContext db, INotificationService notifications)
        {
            _db = db; _notifications = notifications;
        }

        public async Task<MeetingRoom?> CreateNextAsync(MeetingRoom current)
        {
            if (!current.IsRecurring || current.SeriesId == null) return null;
            if (await _db.MeetingRooms.AnyAsync(r => r.SeriesId == current.SeriesId && r.Id > current.Id))
                return null;

            var now = DateTime.UtcNow;
            var after = current.ScheduledAt.HasValue && current.ScheduledAt.Value > now
                ? current.ScheduledAt.Value : now;
            var nextAt = current.NextOccurrenceUtc(after);
            if (nextAt == null) return null;

            var next = new MeetingRoom
            {
                Title              = current.Title,
                Description        = current.Description,
                MeetingType        = current.MeetingType,
                RoomCode           = GenerateRoomCode(current.MeetingType),
                HostUserId         = current.HostUserId,
                HostMemberId       = current.HostMemberId,
                ScheduledAt        = nextAt,
                DurationMinutes    = current.DurationMinutes,
                Status             = RoomStatus.Scheduled,
                IsRecurring        = true,
                RecurringPattern   = current.RecurringPattern,
                SeriesId           = current.SeriesId,
                WaitingRoomEnabled = current.WaitingRoomEnabled,
                ChatEnabled        = current.ChatEnabled,
                RecordingEnabled   = current.RecordingEnabled,
                MaxParticipants    = current.MaxParticipants,
                IsPublic           = current.IsPublic,
                PassCode           = current.PassCode,
                // The series was approved once
                IsApproved         = current.IsApproved,
                ApprovalStatus     = current.ApprovalStatus,
                ApprovedByUserId   = current.ApprovedByUserId,
                ApprovedAt         = current.ApprovedAt
            };

            var invitations = await _db.MeetingInvitations.AsNoTracking()
                .Where(i => i.MeetingRoomId == current.Id).ToListAsync();
            foreach (var inv in invitations)
                next.Invitations.Add(new MeetingInvitation
                {
                    MemberId        = inv.MemberId,
                    Email           = inv.Email,
                    MinistryGroupId = inv.MinistryGroupId
                });

            _db.MeetingRooms.Add(next);
            await _db.SaveChangesAsync();

            if (!next.IsPublic && next.IsApproved)
                await NotifyInviteesAsync(next, invitations);

            return next;
        }

        // In-app notification only, to invited members and the current members of invited groups
        private async Task NotifyInviteesAsync(MeetingRoom room, List<MeetingInvitation> invitations)
        {
            var memberIds = invitations.Where(i => i.MemberId != null).Select(i => i.MemberId!.Value).ToList();
            var groupIds  = invitations.Where(i => i.MinistryGroupId != null).Select(i => i.MinistryGroupId!.Value).ToList();
            if (groupIds.Count > 0)
                memberIds.AddRange(await _db.Members
                    .Where(m => m.MinistryGroups.Any(g => groupIds.Contains(g.Id))
                             && m.Status == MemberStatus.Active)
                    .Select(m => m.Id).ToListAsync());

            var when = room.ScheduledAt?.ToLocalTime().ToString("ddd, dd MMM · h:mm tt");
            foreach (var memberId in memberIds.Distinct().Where(id => id != room.HostMemberId))
                await _notifications.CreateAsync(memberId,
                    $"Meeting Invitation: {room.Title}",
                    $"{room.Title} — next session {when}. Room code: {room.RoomCode}",
                    $"/meeting/room/{room.RoomCode}");
        }

        public async Task CatchUpAsync()
        {
            var cutoff = DateTime.UtcNow - MissedAfter;
            var due = await _db.MeetingRooms
                .Where(r => r.IsRecurring && r.SeriesId != null
                         && (r.Status == RoomStatus.Ended
                          || (r.Status == RoomStatus.Scheduled && r.ScheduledAt < cutoff))
                         && !_db.MeetingRooms.Any(o => o.SeriesId == r.SeriesId && o.Id > r.Id))
                .ToListAsync();

            foreach (var room in due)
            {
                // Nobody opened it: recorded as not held
                if (room.Status == RoomStatus.Scheduled)
                {
                    room.Status = RoomStatus.Cancelled;
                    await _db.SaveChangesAsync();
                }
                await CreateNextAsync(room);
            }
        }

        public string GenerateRoomCode(MeetingType type)
        {
            var prefix = type switch
            {
                MeetingType.Prayer       => "PRAY",
                MeetingType.BibleStudy   => "BSDY",
                MeetingType.Worship      => "WRSP",
                MeetingType.YouthService => "YWTH",
                MeetingType.CellGroup    => "CELL",
                _                        => "MEET"
            };
            // Codes must be unique; retry on the rare clash
            for (var attempt = 0; ; attempt++)
            {
                var code = $"{prefix}-{Random.Shared.Next(1000, 10000)}";
                if (attempt >= 20 || !_db.MeetingRooms.Any(r => r.RoomCode == code)) return code;
            }
        }
    }

    // ─── Meeting access (public vs private) ───
    public interface IMeetingAccessService
    {
        // Public meetings are open to every signed-in member. A private one only to its
        // host, Admins/Pastors, and people invited directly, by email or through a group.
        Task<bool> CanAccessAsync(MeetingRoom room, System.Security.Claims.ClaimsPrincipal user);
    }

    public class MeetingAccessService : IMeetingAccessService
    {
        private readonly ApplicationDbContext _db;
        public MeetingAccessService(ApplicationDbContext db) { _db = db; }

        public async Task<bool> CanAccessAsync(MeetingRoom room, System.Security.Claims.ClaimsPrincipal user)
        {
            if (room.IsPublic) return true;
            if (user.IsInRole("Admin") || user.IsInRole("Pastor")) return true;

            var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userId == null) return false;
            if (room.HostUserId == userId) return true;

            var member = await _db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId);
            if (member != null && room.HostMemberId == member.Id) return true;

            var userEmail = await _db.Users.Where(u => u.Id == userId)
                .Select(u => u.Email).FirstOrDefaultAsync();
            var emails = new[] { member?.Email, userEmail }
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e!.ToLower()).Distinct().ToList();
            var memberId = member?.Id;
            var groupIds = memberId == null ? new List<int>()
                : await _db.MinistryGroupMembers.Where(gm => gm.MemberId == memberId)
                    .Select(gm => gm.MinistryGroupId).ToListAsync();

            // A group invite follows the group's current membership
            return await _db.MeetingInvitations.AnyAsync(i => i.MeetingRoomId == room.Id
                && ((memberId != null && i.MemberId == memberId)
                 || (i.MinistryGroupId != null && groupIds.Contains(i.MinistryGroupId.Value))
                 || (i.Email != null && emails.Contains(i.Email.ToLower()))));
        }
    }

    // ─── Audio store ───
    // Uploaded audio lives in wwwroot/uploads/audio/{type}/{file}: one folder per type
    // (sermons, music, or any type an admin names). Files left in the top folder count as "Other".
    public record AudioStoreFile(string Type, string TypeLabel, string Name, string Url, long Size, DateTime Modified);
    public record AudioTypeInfo(string Slug, string Label);

    public static class AudioStore
    {
        public const long MaxBytes = 200L * 1024 * 1024;
        public static readonly string[] Extensions = { ".mp3", ".m4a", ".aac", ".ogg", ".wav" };
        public static readonly string[] BuiltInTypes = { "sermons", "music" };

        public static string Folder(IWebHostEnvironment env) => Path.Combine(env.WebRootPath, "uploads", "audio");

        public static string Url(string type, string name) =>
            string.IsNullOrEmpty(type) ? $"/uploads/audio/{name}" : $"/uploads/audio/{type}/{name}";

        // "Worship Songs!" → "worship-songs"
        public static string Slug(string? text, int maxLength)
        {
            var slug = System.Text.RegularExpressions.Regex.Replace((text ?? "").ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
            return slug.Length > maxLength ? slug[..maxLength].TrimEnd('-') : slug;
        }

        // "worship-songs" → "Worship Songs"; the top folder is "Other"
        public static string TypeLabel(string slug) => slug.Length == 0 ? "Other"
            : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug.Replace('-', ' '));

        // Sermons and Music always, then any other type folders
        public static List<AudioTypeInfo> ListTypes(IWebHostEnvironment env)
        {
            var root = Folder(env);
            var custom = Directory.Exists(root)
                ? new DirectoryInfo(root).GetDirectories().Select(d => d.Name)
                    .Where(n => !BuiltInTypes.Contains(n) && n == Slug(n, 40)).OrderBy(n => n)
                : Enumerable.Empty<string>();
            return BuiltInTypes.Concat(custom).Select(s => new AudioTypeInfo(s, TypeLabel(s))).ToList();
        }

        // Every audio file, newest first
        public static List<AudioStoreFile> ListFiles(IWebHostEnvironment env)
        {
            var root = Folder(env);
            if (!Directory.Exists(root)) return new();

            var folders = new[] { (Slug: "", Dir: new DirectoryInfo(root)) }
                .Concat(new DirectoryInfo(root).GetDirectories()
                    .Where(d => d.Name == Slug(d.Name, 40))
                    .Select(d => (Slug: d.Name, Dir: d)));

            return folders
                .SelectMany(f => f.Dir.GetFiles()
                    .Where(file => Extensions.Contains(file.Extension.ToLowerInvariant()))
                    .Select(file => new AudioStoreFile(f.Slug, TypeLabel(f.Slug), file.Name,
                        Url(f.Slug, file.Name), file.Length, file.LastWriteTime)))
                .OrderByDescending(f => f.Modified)
                .ToList();
        }

        // Only plain names inside the audio folders — never a path elsewhere
        public static string? FilePath(IWebHostEnvironment env, string? type, string? name)
        {
            if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name)) return null;
            if (!string.IsNullOrEmpty(type) && type != Slug(type, 40)) return null;
            return Path.Combine(Folder(env), type ?? "", name);
        }

        // True when the url is a file in the audio library
        public static bool IsLibraryUrl(IWebHostEnvironment env, string? url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("/uploads/audio/")) return false;
            var parts = url["/uploads/audio/".Length..].Split('/');
            var path = parts.Length switch
            {
                1 => FilePath(env, "", parts[0]),
                2 => FilePath(env, parts[0], parts[1]),
                _ => null
            };
            return path != null && File.Exists(path);
        }
    }

    // ─── Media links ───
    // Works out what kind of player a sermon's video/audio/live-stream link needs
    public static class MediaLinks
    {
        private static readonly string[] AudioExtensions = { ".mp3", ".aac", ".m4a", ".ogg", ".oga", ".opus", ".wav", ".flac" };
        private static readonly System.Text.RegularExpressions.Regex YouTubeIdPattern = new("^[A-Za-z0-9_-]{11}$");

        // youtube | youtube-channel | youtube-unsupported | facebook | hls | audio | file | none
        public static string Kind(string? url)
        {
            var uri = ToUri(url);
            if (uri == null) return "none";
            if (IsYouTubeHost(uri))
                return YouTubeVideoId(url) != null ? "youtube"
                     : YouTubeChannelId(url) != null ? "youtube-channel"
                     : "youtube-unsupported";
            if (IsFacebook(url)) return "facebook";
            if (IsHls(url)) return "hls";
            if (IsAudioFile(url)) return "audio";
            return "file";
        }

        // watch?v=ID, youtu.be/ID, /live/ID, /embed/ID, /shorts/ID, /v/ID (query strings ignored)
        public static string? YouTubeVideoId(string? url)
        {
            var uri = ToUri(url);
            if (uri == null || !IsYouTubeHost(uri)) return null;

            string? id = null;
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (uri.Host.EndsWith("youtu.be")) id = segments.FirstOrDefault();
            else if (segments.Length >= 2 && segments[0] is "live" or "embed" or "shorts" or "v") id = segments[1];
            else id = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query).TryGetValue("v", out var v) ? v.ToString() : null;

            return id != null && YouTubeIdPattern.IsMatch(id) ? id : null;
        }

        // youtube.com/channel/UC…(/live) — plays whatever the channel is streaming now
        public static string? YouTubeChannelId(string? url)
        {
            var uri = ToUri(url);
            if (uri == null || !IsYouTubeHost(uri)) return null;
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length >= 2 && segments[0] == "channel" && segments[1].StartsWith("UC") ? segments[1] : null;
        }

        public static bool IsFacebook(string? url)
        {
            var host = ToUri(url)?.Host ?? "";
            return host == "fb.watch" || host == "facebook.com" || host.EndsWith(".facebook.com");
        }

        public static bool IsHls(string? url) =>
            ToUri(url)?.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) == true;

        public static bool IsAudioFile(string? url)
        {
            var path = ToUri(url)?.AbsolutePath ?? "";
            return AudioExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsYouTubeHost(Uri uri) =>
            uri.Host == "youtu.be" || uri.Host == "youtube.com" || uri.Host.EndsWith(".youtube.com")
            || uri.Host.EndsWith("youtube-nocookie.com");

        // Accepts links pasted without "https://"; relative links (uploaded files) resolve against the site
        private static Uri? ToUri(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            url = url.Trim();
            if (url.StartsWith("/")) return new Uri(new Uri("https://local.site"), url);
            if (!url.Contains("://")) url = "https://" + url;
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
        }
    }

    // ─── Bible Service ───
    // Reads EasyWorship Bible files (*.ewb) from App_Data/bibles. Layout: header
    // "EasyWorship Bible Text", then 66 book records of 0xE0 bytes from 0x58, each with
    // its chapter count at +0x33 and the offset/length (Int64) of its zlib-compressed
    // text at +0xD0/+0xD8. The text is one verse per line: "1:1 In the beginning…".
    public record BibleTranslation(string Code, string Abbrev, string Name);
    public record BibleRef(int Book, int Chapter, int? Verse);
    public record BibleVerseText(int Number, string Label, string Text);   // Label is "16", or "9-10" for combined verses
    public record BibleChapter(int Book, string BookName, int Chapter, int ChapterCount,
                               string Code, string Abbrev, List<BibleVerseText> Verses);
    public record BibleBookInfo(int Number, string Name, int Chapters, string[] Aliases);   // for the book/chapter autocomplete

    public interface IBibleService
    {
        IReadOnlyList<BibleTranslation> GetTranslations();
        string? DefaultCode();
        string BookName(int book);
        IReadOnlyList<BibleBookInfo> GetBooks();
        (BibleRef? Ref, string? Error) ParseReference(string? input);
        Task<(BibleChapter? Chapter, string? Error)> GetChapterAsync(string? code, int book, int chapter);
    }

    public class BibleService : IBibleService
    {
        private const string Magic = "EasyWorship Bible Text";
        private const int FirstRecord = 0x58, RecordSize = 0xE0, BookCount = 66;
        private const string PreferredDefault = "kjv", FallbackDefault = "nkjv";

        private static readonly Dictionary<string, string> KnownNames = new()
        {
            ["kjv"] = "King James Version", ["nkjv"] = "New King James Version",
            ["niv"] = "New International Version", ["niva"] = "New International Version (Anglicised)",
            ["esv"] = "English Standard Version", ["nlt"] = "New Living Translation",
            ["amp"] = "Amplified Bible", ["msg"] = "The Message", ["nasb"] = "New American Standard Bible",
            ["rsv"] = "Revised Standard Version", ["nrsv"] = "New Revised Standard Version",
            ["gnt"] = "Good News Translation", ["darby"] = "Darby Translation",
            ["lbla"] = "La Biblia de las Américas", ["nblh"] = "Nueva Biblia Latinoamericana de Hoy",
            ["nvi"] = "Nueva Versión Internacional", ["nvi-p"] = "Nova Versão Internacional",
            ["rvr"] = "Reina-Valera 1960", ["lsg"] = "Louis Segond",
            ["nbv"] = "De Nieuwe Bijbelvertaling", ["nbg51"] = "NBG-vertaling 1951",
            ["nb88-bm"] = "Norsk Bibel 88 (bokmål)", ["nb88-nn"] = "Norsk Bibel 88 (nynorsk)",
            ["bibel2000"] = "Bibel 2000",
        };

        private static readonly string[] Books =
        {
            "Genesis", "Exodus", "Leviticus", "Numbers", "Deuteronomy", "Joshua", "Judges", "Ruth",
            "1 Samuel", "2 Samuel", "1 Kings", "2 Kings", "1 Chronicles", "2 Chronicles", "Ezra",
            "Nehemiah", "Esther", "Job", "Psalms", "Proverbs", "Ecclesiastes", "Song of Solomon",
            "Isaiah", "Jeremiah", "Lamentations", "Ezekiel", "Daniel", "Hosea", "Joel", "Amos",
            "Obadiah", "Jonah", "Micah", "Nahum", "Habakkuk", "Zephaniah", "Haggai", "Zechariah",
            "Malachi", "Matthew", "Mark", "Luke", "John", "Acts", "Romans", "1 Corinthians",
            "2 Corinthians", "Galatians", "Ephesians", "Philippians", "Colossians", "1 Thessalonians",
            "2 Thessalonians", "1 Timothy", "2 Timothy", "Titus", "Philemon", "Hebrews", "James",
            "1 Peter", "2 Peter", "1 John", "2 John", "3 John", "Jude", "Revelation"
        };

        // Short forms that aren't simply the start of a book's name (compared without spaces/dots)
        private static readonly Dictionary<string, int> Aliases = new()
        {
            ["gn"] = 1, ["ex"] = 2, ["lv"] = 3, ["nm"] = 4, ["dt"] = 5, ["jos"] = 6, ["jdg"] = 7, ["jgs"] = 7,
            ["ru"] = 8, ["ps"] = 19, ["psa"] = 19, ["psalm"] = 19, ["pss"] = 19, ["pr"] = 20, ["prv"] = 20,
            ["qoh"] = 21, ["song"] = 22, ["songofsongs"] = 22, ["sos"] = 22, ["canticles"] = 22,
            ["ezk"] = 26, ["jl"] = 29, ["ob"] = 31, ["jnh"] = 32, ["mi"] = 33, ["na"] = 34, ["hab"] = 35,
            ["zep"] = 36, ["hg"] = 37, ["zec"] = 38, ["mt"] = 40, ["mk"] = 41, ["mrk"] = 41, ["lk"] = 42,
            ["jn"] = 43, ["jhn"] = 43, ["ac"] = 44, ["rm"] = 45, ["php"] = 50, ["phil"] = 50,
            ["phm"] = 57, ["philem"] = 57, ["jas"] = 59, ["jm"] = 59, ["jud"] = 65, ["jd"] = 65,
            ["rv"] = 66, ["revelations"] = 66, ["apocalypse"] = 66,
        };

        static BibleService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        private readonly string _folder;
        private readonly IMemoryCache _cache;

        public BibleService(IWebHostEnvironment env, IMemoryCache cache)
        {
            _folder = Path.Combine(env.ContentRootPath, "App_Data", "bibles");
            _cache = cache;
        }

        public IReadOnlyList<BibleTranslation> GetTranslations()
        {
            if (!Directory.Exists(_folder)) return Array.Empty<BibleTranslation>();
            // Only the EasyWorship 2009 format is read; newer (SQLite) .ewb files are skipped
            return Directory.GetFiles(_folder, "*.ewb")
                .Where(IsReadableFormat)
                .Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant())
                .OrderBy(c => c)
                .Select(c => new BibleTranslation(c, c.ToUpperInvariant(),
                    KnownNames.TryGetValue(c, out var name) ? name : c.ToUpperInvariant()))
                .ToList();
        }

        public string? DefaultCode()
        {
            var codes = GetTranslations().Select(t => t.Code).ToList();
            return codes.Contains(PreferredDefault) ? PreferredDefault
                 : codes.Contains(FallbackDefault) ? FallbackDefault
                 : codes.FirstOrDefault();
        }

        public string BookName(int book) => book >= 1 && book <= BookCount ? Books[book - 1] : "";

        // Chapters per book (Protestant canon, as in the Bible files)
        private static readonly int[] ChapterCounts =
        {
            50, 40, 27, 36, 34, 24, 21, 4, 31, 24, 22, 25, 29, 36, 10, 13, 10, 42, 150, 31, 12, 8,
            66, 52, 5, 48, 12, 14, 3, 9, 1, 4, 7, 3, 3, 3, 2, 14, 4, 28, 16, 24, 21, 28, 16, 16,
            13, 6, 6, 4, 4, 5, 3, 6, 4, 3, 1, 13, 5, 5, 3, 5, 1, 1, 1, 22
        };

        public IReadOnlyList<BibleBookInfo> GetBooks() => Enumerable.Range(1, BookCount)
            .Select(i => new BibleBookInfo(i, Books[i - 1], ChapterCounts[i - 1],
                Aliases.Where(a => a.Value == i).Select(a => a.Key).ToArray()))
            .ToList();

        public (BibleRef? Ref, string? Error) ParseReference(string? input)
        {
            var m = System.Text.RegularExpressions.Regex.Match(input ?? "",
                @"^\s*(.+?)\.?\s*(\d+)\s*(?:[:.]\s*(\d+))?\s*$");
            if (!m.Success)
                return (null, "Type a book and chapter, e.g. John 1 or Psalm 23.");

            var key = Compact(m.Groups[1].Value);
            if (key.Length == 0)
                return (null, "Type a book and chapter, e.g. John 1 or Psalm 23.");

            int book;
            if (Aliases.TryGetValue(key, out var aliased)) book = aliased;
            else
            {
                var exact = Array.FindIndex(Books, b => Compact(b) == key);
                if (exact >= 0) book = exact + 1;
                else
                {
                    var matches = Enumerable.Range(1, BookCount).Where(i => Compact(Books[i - 1]).StartsWith(key)).ToList();
                    if (matches.Count == 1) book = matches[0];
                    else if (matches.Count > 1)
                        return (null, $"\"{m.Groups[1].Value.Trim()}\" could be {string.Join(", ", matches.Take(5).Select(i => Books[i - 1]))}. Type more of the name.");
                    else
                        return (null, $"Couldn't find a book called \"{m.Groups[1].Value.Trim()}\".");
                }
            }

            var chapter = int.Parse(m.Groups[2].Value);
            int? verse = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : null;
            return (new BibleRef(book, chapter, verse), null);
        }

        public async Task<(BibleChapter? Chapter, string? Error)> GetChapterAsync(string? code, int book, int chapter)
        {
            var translation = GetTranslations().FirstOrDefault(t => t.Code == code?.ToLowerInvariant());
            if (translation == null) return (null, "That translation isn't available.");
            if (book < 1 || book > BookCount) return (null, "Unknown book.");

            BookText? text;
            try { text = await LoadBookAsync(translation.Code, book); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                return (null, $"Couldn't read {Books[book - 1]} from {translation.Abbrev}: the file looks damaged. Try another translation.");
            }
            if (text == null || text.Chapters.Count == 0)
                return (null, $"{Books[book - 1]} isn't in {translation.Abbrev}.");

            var count = Math.Max(text.ExpectedChapters, text.Chapters.Keys.Max());
            if (chapter < 1 || chapter > count)
                return (null, $"{Books[book - 1]} has {count} chapter{(count == 1 ? "" : "s")}.");
            if (!text.Chapters.TryGetValue(chapter, out var verses))
                return (null, $"{translation.Abbrev}'s copy of {Books[book - 1]} is damaged from chapter {text.Chapters.Keys.Max() + 1} on. Try another translation.");

            return (new BibleChapter(book, Books[book - 1], chapter, count,
                translation.Code, translation.Abbrev, verses), null);
        }

        private record BookText(int ExpectedChapters, Dictionary<int, List<BibleVerseText>> Chapters);

        // One decompressed book, by chapter; cached for a while since a service reads on through it
        private async Task<BookText?> LoadBookAsync(string code, int book)
        {
            var cacheKey = $"bible:{code}:{book}";
            if (_cache.TryGetValue(cacheKey, out BookText? cached)) return cached;

            var bytes = await File.ReadAllBytesAsync(Path.Combine(_folder, code + ".ewb"));
            if (bytes.Length < FirstRecord + BookCount * RecordSize
                || Encoding.ASCII.GetString(bytes, 0, Magic.Length) != Magic)
                throw new InvalidDataException("Not an EasyWorship Bible file");

            var rec = FirstRecord + (book - 1) * RecordSize;
            var expectedChapters = bytes[rec + 0x33];
            var offset = BitConverter.ToInt64(bytes, rec + 0xD0);
            var length = BitConverter.ToInt64(bytes, rec + 0xD8);
            if (length <= 0) return null;
            if (offset < 0 || offset >= bytes.Length) throw new InvalidDataException("Book is out of range");

            // Each book's data follows a 10-byte "QK\x03\x04…" marker. The stored offset normally
            // points just past it, but a few damaged files are off by a few bytes, so look nearby.
            var start = (int)offset;
            for (var p = Math.Max(0, start - 32); p <= Math.Min(bytes.Length - 12, start + 32); p++)
                if (bytes[p] == 0x51 && bytes[p + 1] == 0x4B && bytes[p + 2] == 3 && bytes[p + 3] == 4) { start = p + 10; break; }
            if (bytes[start] != 0x78) throw new InvalidDataException("No compressed data at the book's offset");

            // Raw deflate after the 2-byte zlib header; keep whatever decompresses before any damage
            var output = new MemoryStream();
            using (var deflate = new System.IO.Compression.DeflateStream(
                       new MemoryStream(bytes, start + 2, bytes.Length - start - 2), System.IO.Compression.CompressionMode.Decompress))
            {
                var buffer = new byte[81920];
                try { int n; while ((n = await deflate.ReadAsync(buffer)) > 0) output.Write(buffer, 0, n); }
                catch (InvalidDataException) when (output.Length > 0) { }
            }
            var raw = output.ToArray();

            string text;
            try { text = new UTF8Encoding(false, true).GetString(raw); }
            catch (DecoderFallbackException) { text = Encoding.GetEncoding(1252).GetString(raw); }

            // One verse per line: "3:16 For God…" or a combined "6:9-10 This is…". Verses must
            // come in order, which also keeps out any scrambled text in a damaged file.
            var chapters = new Dictionary<int, List<BibleVerseText>>();
            int lastChapter = 0, lastVerse = 0;
            foreach (var line in text.Split('\n'))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line.Trim(), @"^(\d+):(\d+)(?:-(\d+))?\s+(\S.*)$");
                if (!m.Success) continue;
                int ch = int.Parse(m.Groups[1].Value), v = int.Parse(m.Groups[2].Value);
                if (ch > expectedChapters || ch < lastChapter || (ch == lastChapter && v <= lastVerse)) continue;

                var label = m.Groups[3].Success ? $"{v}-{m.Groups[3].Value}" : v.ToString();
                if (!chapters.TryGetValue(ch, out var list)) chapters[ch] = list = new();
                list.Add(new BibleVerseText(v, label, m.Groups[4].Value.Trim()));
                lastChapter = ch;
                lastVerse = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : v;
            }

            var result = new BookText(expectedChapters, chapters);
            _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(30)
            });
            return result;
        }

        private static bool IsReadableFormat(string path)
        {
            try
            {
                using var file = File.OpenRead(path);
                var head = new byte[Magic.Length];
                return file.Read(head, 0, head.Length) == head.Length && Encoding.ASCII.GetString(head) == Magic;
            }
            catch (IOException) { return false; }
        }

        private static string Compact(string s) =>
            new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }
}
