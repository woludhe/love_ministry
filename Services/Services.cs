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
            bool enableSSL = settings["EnableSsl"] == "true" ? true : false;//Wendy EDIT
            await client.ConnectAsync(settings["SmtpHost"], int.Parse(settings["SmtpPort"]!), MailKit.Security.SecureSocketOptions.None);
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
}
