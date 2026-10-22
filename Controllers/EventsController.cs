using ChurchMinistry.Data;
using ChurchMinistry.Models;
using ChurchMinistry.Services;
using ChurchMinistry.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ChurchMinistry.Controllers
{
    public class EventsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IPaymentService _payment;
        private readonly IEmailService _email;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly IActivityService _activity;

        public EventsController(ApplicationDbContext db, UserManager<ApplicationUser> um,
            IPaymentService payment, IEmailService email,
            IConfiguration config, IWebHostEnvironment env, IActivityService activity)
        {
            _db = db; _userManager = um; _payment = payment;
            _email = email; _config = config; _env = env; _activity = activity;
        }

        private async Task<Member?> GetMemberAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            return user == null ? null
                : await _db.Members.FirstOrDefaultAsync(m => m.UserId == user.Id);
        }

        // ── Public Events List ──────────────────────────────────
        public async Task<IActionResult> Index(int page = 1)
        {
            var pager = new PagerModel(page, "events");
            var events = await _db.ChurchEvents
                .Include(e => e.Registrations)
                .Where(e => e.IsPublic)
                .OrderBy(e => e.StartDate)
                .ToPageAsync(pager);
            ViewBag.Pager = pager;

            // Build per-event registration status for current member
            Member? member = null;
            if (User.Identity?.IsAuthenticated == true)
                member = await GetMemberAsync();

            var myRegMap = new Dictionary<int, EventRegistration?>();
            if (member != null)
            {
                var myRegs = await _db.EventRegistrations
                    .Where(r => r.MemberId == member.Id)
                    .ToListAsync();
                foreach (var ev in events)
                    myRegMap[ev.Id] = myRegs.FirstOrDefault(r => r.EventId == ev.Id);
            }

            ViewBag.MyRegMap = myRegMap;
            ViewBag.Member   = member;
            return View(events);
        }

        // ── Event Details ───────────────────────────────────────
        public async Task<IActionResult> Details(int id)
        {
            var ev = await _db.ChurchEvents
                .Include(e => e.Registrations)
                    .ThenInclude(r => r.Payments)
                .FirstOrDefaultAsync(e => e.Id == id);
            if (ev == null) return NotFound();

            Member? member = null;
            EventRegistration? myReg = null;
            if (User.Identity?.IsAuthenticated == true)
            {
                member = await GetMemberAsync();
                if (member != null)
                    myReg = await _db.EventRegistrations
                        .Include(r => r.Payments)
                        .FirstOrDefaultAsync(r => r.EventId == id && r.MemberId == member.Id);
            }

            int confirmedCount = ev.Registrations.Count(r =>
                r.PaymentStatus == PaymentStatus.Free ||
                r.PaymentStatus == PaymentStatus.Successful ||
                (r.AmountPaid > 0));

            ViewBag.MyRegistration  = myReg;
            ViewBag.ConfirmedCount  = confirmedCount;
            ViewBag.SpotsLeft       = ev.MaxCapacity.HasValue
                ? ev.MaxCapacity.Value - confirmedCount : (int?)null;
            ViewBag.Member          = member;
            return View(ev);
        }

        // ── Register (free event) ───────────────────────────────
        [HttpPost, Authorize]
        public async Task<IActionResult> Register(EventRegisterViewModel vm,
            bool isBehalf = false, string? behalfName = null,
            string? behalfEmail = null, string? behalfPhone = null)
        {
            if (!ModelState.IsValid)
                return RedirectToAction("Details", new { id = vm.EventId });

            var ev     = await _db.ChurchEvents.FindAsync(vm.EventId);
            if (ev == null) return NotFound();
            if (ev.TicketPrice.HasValue && ev.TicketPrice > 0)
                return RedirectToAction("Pay", new { id = vm.EventId });

            var member = await GetMemberAsync();
            if (await _db.EventRegistrations.AnyAsync(r =>
                r.EventId == vm.EventId && r.MemberId == member!.Id &&
                r.PaymentStatus != PaymentStatus.Failed))
            {
                TempData["Info"] = "You are already registered.";
                return RedirectToAction("Details", new { id = vm.EventId });
            }

            _db.EventRegistrations.Add(new EventRegistration
            {
                EventId       = vm.EventId,
                MemberId      = member?.Id,
                Name          = vm.Name,
                Email         = vm.Email,
                Phone         = vm.Phone,
                IsConfirmed   = true,
                PaymentStatus = PaymentStatus.Free,
                IsBehalf      = isBehalf,
                BehalfName    = isBehalf ? behalfName : null,
                BehalfEmail   = isBehalf ? behalfEmail : null,
                BehalfPhone   = isBehalf ? behalfPhone : null
            });
            await _db.SaveChangesAsync();
            if (member != null)
                await _activity.LogAsync(member.Id, MemberActivityType.EventRegistration, ev.Title, ev.Id);

            // Email to registrant and also to behalf person if applicable
            await SendConfirmationEmailAsync(ev, vm.Name, vm.Email);
            if (isBehalf && !string.IsNullOrEmpty(behalfEmail))
                await SendConfirmationEmailAsync(ev, behalfName ?? vm.Name, behalfEmail,
                    $"Registered by {vm.Name}");

            TempData["Success"] = $"Registered for {ev.Title}!";
            return RedirectToAction("Details", new { id = vm.EventId });
        }

        // ── Payment page (paid events) ──────────────────────────
        [HttpGet, Authorize]
        public async Task<IActionResult> Pay(int id, bool isBehalf = false,
            string? behalfName = null, string? behalfEmail = null, string? behalfPhone = null)
        {
            var ev = await _db.ChurchEvents.FindAsync(id);
            if (ev == null) return NotFound();

            var member = await GetMemberAsync();
            var user   = await _userManager.GetUserAsync(User);

            // Get or create registration
            var reg = await _db.EventRegistrations
                .Include(r => r.Payments)
                .FirstOrDefaultAsync(r => r.EventId == id && r.MemberId == member!.Id
                    && r.PaymentStatus != PaymentStatus.Successful);

            if (reg == null)
            {
                reg = new EventRegistration
                {
                    EventId       = id,
                    MemberId      = member?.Id,
                    Name          = isBehalf ? (behalfName ?? member?.FullName ?? "") : (member?.FullName ?? ""),
                    Email         = isBehalf ? (behalfEmail ?? user?.Email ?? "") : (user?.Email ?? ""),
                    Phone         = isBehalf ? behalfPhone : member?.Phone,
                    IsConfirmed   = false,
                    PaymentStatus = PaymentStatus.Pending,
                    TotalAmount   = ev.TicketPrice ?? 0,
                    IsBehalf      = isBehalf,
                    BehalfName    = isBehalf ? behalfName : null,
                    BehalfEmail   = isBehalf ? behalfEmail : null,
                    BehalfPhone   = isBehalf ? behalfPhone : null
                };
                _db.EventRegistrations.Add(reg);
                await _db.SaveChangesAsync();
            }

            ViewBag.Event         = ev;
            ViewBag.Registration  = reg;
            ViewBag.PubKey        = _config["Flutterwave:PublicKey"];
            ViewBag.Email         = user?.Email ?? "";
            ViewBag.MemberName    = member?.FullName ?? "";
            ViewBag.Phone         = member?.Phone ?? "";
            ViewBag.BalanceDue    = reg.TotalAmount - reg.AmountPaid;
            ViewBag.IsBehalf      = isBehalf;
            return View();
        }

        // ── Initiate a payment instalment ───────────────────────
        [HttpPost, Authorize]
        public async Task<IActionResult> MakePayment(int registrationId, decimal amount,
            string paymentMethod, string? phoneNumber)
        {
            var reg = await _db.EventRegistrations
                .Include(r => r.Event)
                .Include(r => r.Payments)
                .FirstOrDefaultAsync(r => r.Id == registrationId);
            if (reg == null) return NotFound();

            var ev  = reg.Event!;
            decimal balance = reg.TotalAmount - reg.AmountPaid;

            // Validate amount
            if (amount <= 0 || amount > balance)
                return Json(new { success = false, message = "Invalid amount." });

            if (ev.AllowPartialPayment && ev.MinPartialAmount.HasValue
                && amount < ev.MinPartialAmount.Value && amount < balance)
                return Json(new { success = false,
                    message = $"Minimum payment is UGX {ev.MinPartialAmount.Value:N0}." });

            var txRef = $"EVT-{ev.Id}-REG-{reg.Id}-{DateTime.UtcNow:yyyyMMddHHmmss}";

            // Record payment attempt
            var payment = new EventPayment
            {
                EventRegistrationId = reg.Id,
                Amount              = amount,
                TransactionId       = txRef,
                Status              = PaymentStatus.Pending,
                PaymentMethod       = paymentMethod
            };
            _db.EventPayments.Add(payment);
            await _db.SaveChangesAsync();

            return Json(new
            {
                success  = true,
                txRef,
                amount,
                currency = "UGX",
                eventTitle = ev.Title
            });
        }

        // ── Payment callback (webhook + redirect) ───────────────
        [HttpGet("Events/PaymentReturn")]
        [HttpPost("Events/PaymentCallback")]
        public async Task<IActionResult> PaymentReturn(
            string status, string tx_ref, string transaction_id)
        {
            // Find the EventPayment instalment by tx_ref
            var payment = await _db.EventPayments
                .Include(p => p.Registration)
                    .ThenInclude(r => r.Event)
                .FirstOrDefaultAsync(p => p.TransactionId == tx_ref);

            if (payment == null)
            {
                // Try legacy tx_ref on registration itself
                var reg2 = await _db.EventRegistrations
                    .Include(r => r.Event)
                    .FirstOrDefaultAsync(r => r.TransactionId == tx_ref);
                if (reg2 == null) return NotFound();

                // Wrap in EventPayment for unified handling
                payment = new EventPayment
                {
                    EventRegistrationId = reg2.Id,
                    Registration        = reg2,
                    Amount              = reg2.TotalAmount
                };
            }

            var registration = payment.Registration;
            var ev           = registration.Event!;

            if (status is "successful" or "completed")
            {
                var verified = await _payment.VerifyTransactionAsync(transaction_id);
                if (verified)
                {
                    payment.Status        = PaymentStatus.Successful;
                    payment.FlutterwaveRef= transaction_id;
                    payment.PaidAt        = DateTime.UtcNow;

                    registration.AmountPaid    += payment.Amount;
                    registration.FlutterwaveRef = transaction_id;

                    bool fullyPaid = registration.AmountPaid >= registration.TotalAmount;
                    registration.PaymentStatus = fullyPaid
                        ? PaymentStatus.Successful : PaymentStatus.Pending;
                    registration.IsConfirmed   = fullyPaid;
                    if (fullyPaid) registration.PaidAt = DateTime.UtcNow;

                    await _db.SaveChangesAsync();
                    if (registration.MemberId.HasValue)
                        await _activity.LogAsync(registration.MemberId.Value, MemberActivityType.EventRegistration,
                            $"{ev.Title} (paid UGX {payment.Amount:N0})", ev.Id, oncePerDay: true);

                    // Email
                    var emailBody = fullyPaid
                        ? $@"<h2>Payment Complete! 🎉</h2>
                             <p>Hi {registration.Name}, your payment of UGX {registration.AmountPaid:N0}
                             for <strong>{ev.Title}</strong> is fully paid. You're confirmed!</p>"
                        : $@"<h2>Partial Payment Received</h2>
                             <p>Hi {registration.Name}, we received UGX {payment.Amount:N0}.
                             Your remaining balance is UGX {(registration.TotalAmount - registration.AmountPaid):N0}.</p>";

                    if (!string.IsNullOrEmpty(registration.Email))
                        await _email.SendEmailAsync(registration.Email,
                            $"{(fullyPaid ? "Payment Confirmed" : "Partial Payment Received")} - {ev.Title}",
                            emailBody, registration.Name);

                    // Also email behalf person
                    if (registration.IsBehalf && !string.IsNullOrEmpty(registration.BehalfEmail))
                        await _email.SendEmailAsync(registration.BehalfEmail,
                            $"Event Registration - {ev.Title}",
                            $"<p>Hi {registration.BehalfName}, you have been registered for <strong>{ev.Title}</strong> by {registration.Name}.</p>",
                            registration.BehalfName);

                    TempData["Success"] = fullyPaid
                        ? "Payment complete! You are fully registered."
                        : $"Payment of UGX {payment.Amount:N0} received. Balance: UGX {(registration.TotalAmount - registration.AmountPaid):N0}";

                    return RedirectToAction("Details", new { id = ev.Id });
                }
            }

            payment.Status             = PaymentStatus.Failed;
            registration.PaymentStatus = PaymentStatus.Failed;
            await _db.SaveChangesAsync();

            TempData["Error"] = "Payment could not be verified. Please try again.";
            return RedirectToAction("Pay", new { id = ev.Id });
        }

        // ── Gallery upload (AJAX) ───────────────────────────────
        [HttpPost, Authorize(Roles = "Admin,Pastor")]
        public async Task<IActionResult> UploadGalleryImage(int id, IFormFile image)
        {
            var ev = await _db.ChurchEvents.FindAsync(id);
            if (ev == null) return NotFound();

            var ext      = Path.GetExtension(image.FileName).ToLower();
            var allowed  = new[]{".jpg",".jpeg",".png",".webp",".gif"};
            if (!allowed.Contains(ext))
                return Json(new { success = false, message = "Invalid file type." });

            var folder   = Path.Combine(_env.WebRootPath, "uploads", "events", id.ToString());
            Directory.CreateDirectory(folder);
            var fileName = $"{Guid.NewGuid()}{ext}";
            var path     = Path.Combine(folder, fileName);

            using var stream = new FileStream(path, FileMode.Create);
            await image.CopyToAsync(stream);

            var url     = $"/uploads/events/{id}/{fileName}";
            var gallery = ev.Gallery;
            gallery.Add(url);
            ev.GalleryJson = JsonSerializer.Serialize(gallery);
            await _db.SaveChangesAsync();

            return Json(new { success = true, url });
        }

        [HttpPost, Authorize(Roles = "Admin,Pastor")]
        public async Task<IActionResult> DeleteGalleryImage(int id, string imageUrl)
        {
            var ev = await _db.ChurchEvents.FindAsync(id);
            if (ev == null) return NotFound();

            var gallery = ev.Gallery;
            gallery.Remove(imageUrl);
            ev.GalleryJson = JsonSerializer.Serialize(gallery);
            await _db.SaveChangesAsync();

            // Delete physical file
            var physical = Path.Combine(_env.WebRootPath, imageUrl.TrimStart('/'));
            if (System.IO.File.Exists(physical))
                System.IO.File.Delete(physical);

            return Json(new { success = true });
        }

        // ── Add Event GET ───────────────────────────────────────────
        [HttpGet]
        public IActionResult AddEvent() => View(new ChurchEvent
        {
            StartDate = DateTime.Now.AddDays(7).Date.AddHours(9),
            EndDate = DateTime.Now.AddDays(7).Date.AddHours(11),
            IsPublic = true,
            RequiresRegistration = false
        });

        // ── Add Event POST ──────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> AddEvent(
            ChurchEvent ev,
            IFormFile? bannerImage,
            [FromServices] IWebHostEnvironment env)
        {
            // Manual validation
            if (string.IsNullOrWhiteSpace(ev.Title))
                ModelState.AddModelError("Title", "Title is required.");

            if (string.IsNullOrWhiteSpace(ev.RichDescription) ||
                System.Text.RegularExpressions.Regex.Replace(ev.RichDescription, "<[^>]+>", "").Trim().Length == 0)
                ModelState.AddModelError("RichDescription", "Description is required.");

            if (ev.StartDate == default)
                ModelState.AddModelError("StartDate", "Start date is required.");

            if (ev.EndDate == default)
                ModelState.AddModelError("EndDate", "End date is required.");

            if (ev.EndDate <= ev.StartDate)
                ModelState.AddModelError("EndDate", "End date must be after start date.");

            if (!ModelState.IsValid) return View(ev);

            // Banner upload
            if (bannerImage != null)
                ev.ImageUrl = await SaveImageAsync(bannerImage, "events/banners", env);

            // Strip HTML to plain text for description snippet
            ev.Description = System.Text.RegularExpressions.Regex
                .Replace(ev.RichDescription ?? "", "<[^>]+>", "")
                .Trim().Substring(0, Math.Min(500,
                    System.Text.RegularExpressions.Regex
                        .Replace(ev.RichDescription ?? "", "<[^>]+>", "").Trim().Length));

            _db.ChurchEvents.Add(ev);
            await _db.SaveChangesAsync();

            // Handle gallery uploads (staged files from form)
            await ProcessGalleryUploads(ev.Id, Request, env);

            TempData["Success"] = $"Event \"{ev.Title}\" created!";
            return RedirectToAction("Events");
        }

        // ── Edit Event GET ──────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> EditEvent(int id)
        {
            var ev = await _db.ChurchEvents.FindAsync(id);
            if (ev == null) return NotFound();
            return View(ev);
        }

        // ── Edit Event POST ─────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> EditEvent(
            ChurchEvent updated,
            IFormFile? bannerImage,
            [FromServices] IWebHostEnvironment env)
        {
            // Validation
            if (string.IsNullOrWhiteSpace(updated.Title))
                ModelState.AddModelError("Title", "Title is required.");

            if (string.IsNullOrWhiteSpace(updated.RichDescription) ||
                System.Text.RegularExpressions.Regex.Replace(updated.RichDescription, "<[^>]+>", "").Trim().Length == 0)
                ModelState.AddModelError("RichDescription", "Description is required.");

            if (updated.StartDate == default)
                ModelState.AddModelError("StartDate", "Start date is required.");

            if (updated.EndDate == default)
                ModelState.AddModelError("EndDate", "End date is required.");

            if (updated.EndDate <= updated.StartDate)
                ModelState.AddModelError("EndDate", "End date must be after start date.");

            if (updated.TicketPrice.HasValue && updated.TicketPrice < 0)
                ModelState.AddModelError("TicketPrice", "Ticket price cannot be negative.");

            if (updated.AllowPartialPayment && updated.MinPartialAmount.HasValue
                && updated.TicketPrice.HasValue
                && updated.MinPartialAmount >= updated.TicketPrice)
                ModelState.AddModelError("MinPartialAmount",
                    "Minimum partial amount must be less than the full ticket price.");

            if (!ModelState.IsValid) return View(updated);

            var ev = await _db.ChurchEvents.FindAsync(updated.Id);
            if (ev == null) return NotFound();

            // Apply all fields
            ev.Title = updated.Title;
            ev.RichDescription = updated.RichDescription;
            ev.Description = System.Text.RegularExpressions.Regex
                .Replace(updated.RichDescription ?? "", "<[^>]+>", "")
                .Trim().Substring(0, Math.Min(500,
                    System.Text.RegularExpressions.Regex
                        .Replace(updated.RichDescription ?? "", "<[^>]+>", "").Trim().Length));
            ev.StartDate = updated.StartDate;
            ev.EndDate = updated.EndDate;
            ev.IsAllDay = updated.IsAllDay;
            ev.Location = updated.Location;
            ev.IsPublic = updated.IsPublic;
            ev.RequiresRegistration = updated.RequiresRegistration;
            ev.MaxCapacity = updated.MaxCapacity;
            ev.TicketPrice = updated.TicketPrice;
            ev.AllowPartialPayment = updated.AllowPartialPayment;
            ev.MinPartialAmount = updated.AllowPartialPayment ? updated.MinPartialAmount : null;
            ev.GalleryJson = updated.GalleryJson;

            if (bannerImage != null)
                ev.ImageUrl = await SaveImageAsync(bannerImage, "events/banners", env);
            else if (!string.IsNullOrEmpty(updated.ImageUrl))
                ev.ImageUrl = updated.ImageUrl;

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Event \"{ev.Title}\" updated.";
            return RedirectToAction("Events");
        }

        // ── Gallery upload endpoint (AJAX from form) ────────────────
        [HttpPost]
        public async Task<IActionResult> UploadEventGalleryImage(
            int id, IFormFile image,
            [FromServices] IWebHostEnvironment env)
        {
            var ev = await _db.ChurchEvents.FindAsync(id);
            if (ev == null) return NotFound();

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
            var ext = Path.GetExtension(image.FileName).ToLower();
            if (!allowed.Contains(ext))
                return Json(new { success = false, message = "Invalid file type." });

            if (image.Length > 5 * 1024 * 1024)
                return Json(new { success = false, message = "Max file size is 5MB." });

            var url = await SaveImageAsync(image, $"events/{id}", env);
            var gallery = ev.Gallery;
            gallery.Add(url);
            ev.GalleryJson = System.Text.Json.JsonSerializer.Serialize(gallery);
            await _db.SaveChangesAsync();

            return Json(new { success = true, url });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteEventGalleryImage(int id, string imageUrl,
            [FromServices] IWebHostEnvironment env)
        {
            var ev = await _db.ChurchEvents.FindAsync(id);
            if (ev == null) return NotFound();

            var gallery = ev.Gallery;
            gallery.Remove(imageUrl);
            ev.GalleryJson = System.Text.Json.JsonSerializer.Serialize(gallery);
            await _db.SaveChangesAsync();

            var physical = Path.Combine(env.WebRootPath, imageUrl.TrimStart('/'));
            if (System.IO.File.Exists(physical))
                System.IO.File.Delete(physical);

            return Json(new { success = true });
        }

        // ── Shared helpers ──────────────────────────────────────────
        private static async Task<string> SaveImageAsync(
            IFormFile file, string subfolder, IWebHostEnvironment env)
        {
            var folder = Path.Combine(env.WebRootPath, "uploads", subfolder);
            Directory.CreateDirectory(folder);
            var ext = Path.GetExtension(file.FileName).ToLower();
            var fileName = $"{Guid.NewGuid()}{ext}";
            var path = Path.Combine(folder, fileName);
            using var stream = new FileStream(path, FileMode.Create);
            await file.CopyToAsync(stream);
            return $"/uploads/{subfolder}/{fileName}";
        }

        private static async Task ProcessGalleryUploads(
            int eventId, HttpRequest request, IWebHostEnvironment env)
        {
            // Processes any files submitted with name "galleryFiles" in add mode
            if (!request.Form.Files.Any()) return;
            var galleryFiles = request.Form.Files
                .Where(f => f.Name == "galleryFiles").ToList();
            // This is handled client-side for add mode - gallery JSON is synced via hidden input
            await Task.CompletedTask;
        }

        // ── Helper ─────────────────────────────────────────────
        private async Task SendConfirmationEmailAsync(ChurchEvent ev,
            string name, string email, string? extra = null)
        {
            var body = $@"
              <h2>Registration Confirmed 🎉</h2>
              <p>Hi {name}, your registration for <strong>{ev.Title}</strong> is confirmed.</p>
              {(extra != null ? $"<p><em>{extra}</em></p>" : "")}
              <table>
                <tr><td><b>Date:</b></td><td>{ev.StartDate:dddd, dd MMMM yyyy}</td></tr>
                <tr><td><b>Time:</b></td><td>{ev.StartDate:h:mm tt}</td></tr>
                {(ev.Location != null ? $"<tr><td><b>Location:</b></td><td>{ev.Location}</td></tr>" : "")}
              </table>";
            await _email.SendEmailAsync(email,
                $"Registration Confirmed - {ev.Title}", body, name);
        }
    }
}
