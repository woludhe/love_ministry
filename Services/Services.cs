using ChurchMinistry.Data;
using ChurchMinistry.Models;
using ChurchMinistry.ViewModels;
using MailKit.Net.Smtp;
using Microsoft.EntityFrameworkCore;
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
}
