using ChurchMinistry.Data;
using ChurchMinistry.Models;
using ChurchMinistry.Services;
using ChurchMinistry.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChurchMinistry.Controllers
{
    [Authorize]
    public class GivingController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IPaymentService _payment;
        private readonly IEmailService _email;
        private readonly IConfiguration _config;

        private readonly IActivityService _activity;

        public GivingController(ApplicationDbContext db, UserManager<ApplicationUser> um,
            IPaymentService payment, IEmailService email, IConfiguration config, IActivityService activity)
        {
            _db = db; _userManager = um;
            _payment = payment; _email = email; _config = config; _activity = activity;
        }

        private async Task<Member?> GetMemberAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            return user == null ? null
                : await _db.Members.FirstOrDefaultAsync(m => m.UserId == user.Id);
        }

        // ── Helper: load active payment methods ─────────────────
        private async Task<List<PaymentMethodConfig>> GetActiveMethodsAsync()
            => await _db.PaymentMethods
                .Where(m => m.IsActive)
                .OrderBy(m => m.SortOrder)
                .ToListAsync();

        // ── Give form GET ────────────────────────────────────────
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Give(string? method = null, decimal? amount = null, GivingType? type = null)
        {
            var methods = await GetActiveMethodsAsync();
            var member  = await GetMemberAsync();

            // Pre-select method if passed in URL
            PaymentMethodConfig? selected = null;
            if (!string.IsNullOrEmpty(method))
                selected = methods.FirstOrDefault(m => m.Code == method);
            selected ??= methods.FirstOrDefault();

            var vm = new GivingViewModel
            {
                Amount         = amount ?? 0,
                Currency       = selected?.Currency ?? "UGX",
                MethodCode     = selected?.Code ?? "",
                GivingType     = type ?? GivingType.Tithe,   // giving reminders link back with the same type
            };

            ViewBag.PaymentMethods = methods;
            ViewBag.SelectedMethod = selected;
            ViewBag.MemberName     = member?.FullName ?? "";
            ViewBag.MemberEmail    = (await _userManager.GetUserAsync(User))?.Email ?? "";

            // Serialize methods to JSON for the Give.cshtml script block
            ViewBag.MethodsJson = System.Text.Json.JsonSerializer.Serialize(
                methods.Select(m => new {
                    code             = m.Code,
                    name             = m.Name,
                    currency         = m.Currency,
                    currencySymbol   = m.CurrencySymbol,
                    integrationType  = m.IntegrationType.ToString().ToLower(),
                    amounts          = m.ParsedAmounts,
                    instructionsText = m.InstructionsText,
                    instructionsImage= m.InstructionsImagePath,
                })
            );
            return View(vm);
        }

        // ── Give form POST ───────────────────────────────────────
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Give(GivingViewModel vm)
        {
            var methods = await GetActiveMethodsAsync();
            ViewBag.PaymentMethods = methods;

            // Load the selected payment method config
            var methodConfig = methods.FirstOrDefault(m => m.Code == vm.MethodCode);
            if (methodConfig == null)
            {
                ModelState.AddModelError("", "Invalid payment method selected.");
                return View(vm);
            }

            // Sync currency from the method config
            vm.Currency = methodConfig.Currency;
            ViewBag.SelectedMethod = methodConfig;

            if (!ModelState.IsValid) return View(vm);

            var user   = await _userManager.GetUserAsync(User);
            var member = user == null ? null
                : await _db.Members.FirstOrDefaultAsync(m => m.UserId == user.Id);

            var txRef = $"CHURCH-{DateTime.UtcNow:yyyyMMddHHmmss}-" +
                        $"{Guid.NewGuid().ToString()[..8].ToUpper()}";

            var giving = new Giving
            {
                MemberId          = member?.Id,
                Amount            = vm.Amount,
                Currency          = vm.Currency,
                GivingType        = vm.GivingType,
                PaymentMethod     = ToPaymentMethod(methodConfig),
                MethodCode        = methodConfig.Code,
                IsManualPayment   = methodConfig.IntegrationType == PaymentIntegrationType.Manual,
                PhoneNumber       = vm.PhoneNumber,
                IsRecurring       = vm.IsRecurring,
                RecurringInterval = vm.RecurringInterval,
                Notes             = vm.Notes,
                TransactionId     = txRef,
                PaymentStatus     = PaymentStatus.Pending
            };
            _db.Givings.Add(giving);
            await _db.SaveChangesAsync();

            // "Remind me to give this regularly": set up now, switched on once this gift is paid or confirmed
            if (vm.IsRecurring && vm.RecurringInterval != null && member != null)
            {
                _db.RecurringGifts.Add(new RecurringGift
                {
                    MemberId       = member.Id,
                    SourceGivingId = giving.Id,
                    Amount         = vm.Amount,
                    Currency       = vm.Currency,
                    GivingType     = vm.GivingType,
                    MethodCode     = vm.MethodCode,
                    Interval       = vm.RecurringInterval.Value
                });
                await _db.SaveChangesAsync();
            }

            // ── Route based on integration type ─────────────────
            if (methodConfig.IntegrationType == PaymentIntegrationType.Flutterwave)
            {
                // Flutterwave hosted checkout
                ViewBag.PubKey   = _config["Flutterwave:PublicKey"];
                ViewBag.Email    = user?.Email ?? "guest@church.org";
                ViewBag.Amount   = vm.Amount;
                ViewBag.Currency = vm.Currency;
                ViewBag.Method   = methodConfig;
                return View("CardPayment", giving);
            }
            else
            {
                // Manual — show payment instructions
                ViewBag.Method  = methodConfig;
                ViewBag.TxRef   = txRef;
                ViewBag.Member  = member;
                return View("ManualPayment", giving);
            }
        }

        // ── Manual payment confirmation ──────────────────────────
        // Member clicks "I have paid" — records as pending verification
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmManual(int givingId)
        {
            var giving = await _db.Givings.FindAsync(givingId);
            if (giving == null) return NotFound();
            if (giving.PaymentStatus != PaymentStatus.Pending)   // already confirmed, reconciled or resubmitted
                return RedirectToAction("GivingThankYou", new { id = givingId });

            giving.PaymentStatus = PaymentStatus.PendingVerification;
            giving.Notes         = (giving.Notes ?? "") + " [Member confirmed payment]";
            await _db.SaveChangesAsync();
            await StartRecurringReminderAsync(giving);
            if (giving.MemberId.HasValue)
                await _activity.LogAsync(giving.MemberId.Value, MemberActivityType.Giving,
                    $"{giving.GivingType} {giving.Currency} {giving.Amount:N0} (pending verification)",
                    giving.Id, oncePerDay: true);

            // Notify admin
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            foreach (var admin in admins)
            {
                var adminMember = await _db.Members.FirstOrDefaultAsync(m => m.UserId == admin.Id);
                if (adminMember != null)
                {
                    _db.Notifications.Add(new Notification
                    {
                        MemberId = adminMember.Id,
                        Title    = "💰 Manual Giving — Verify",
                        Message  = $"A {giving.GivingType} of {giving.Currency} " +
                                   $"{giving.Amount:N0} via {giving.PaymentMethod} " +
                                   $"needs manual verification. Ref: {giving.TransactionId}",
                        Link     = "/Admin/Givings?status=reconcile"
                    });
                }
            }
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Thank you! Your giving of {giving.Currency} {giving.Amount:N0} " +
                                  $"has been recorded. Our team will verify and confirm it shortly.";
            return RedirectToAction("GivingThankYou", new { id = givingId });
        }

        // ── Thank you page ───────────────────────────────────────
        [AllowAnonymous]
        public async Task<IActionResult> GivingThankYou(int id)
        {
            var giving = await _db.Givings.FindAsync(id);
            if (giving == null) return RedirectToAction("Give");
            return View(giving);
        }

        // ── Pending page (mobile money) ──────────────────────────
        [AllowAnonymous]
        public async Task<IActionResult> GivingPending(int id)
        {
            var giving = await _db.Givings.FindAsync(id);
            if (giving == null) return NotFound();
            return View(giving);
        }

        // ── Flutterwave callback (GET — browser redirect) ────────
        [HttpGet, AllowAnonymous]
        public async Task<IActionResult> Callback(string status, string tx_ref, string transaction_id)
            => await ProcessCallback(status, tx_ref, transaction_id);

        // ── Flutterwave webhook (POST) ───────────────────────────
        [HttpPost, AllowAnonymous]
        public async Task<IActionResult> Callback(FlutterwaveCallback cb)
        {
            var hash         = Request.Headers["verif-hash"].ToString();
            var expectedHash = _config["Flutterwave:WebhookSecret"];
            if (!string.IsNullOrEmpty(expectedHash) && hash != expectedHash)
                return Unauthorized();
            await ProcessCallback(cb.status, cb.tx_ref, cb.transaction_id);
            return Ok();
        }

        // ── Shared callback processing ───────────────────────────
        private async Task<IActionResult> ProcessCallback(
            string status, string tx_ref, string transaction_id)
        {
            if (string.IsNullOrEmpty(tx_ref))
            { TempData["Error"] = "Invalid payment reference."; return RedirectToAction("Give"); }

            var giving = await _db.Givings.FirstOrDefaultAsync(g => g.TransactionId == tx_ref);
            if (giving == null)
            { TempData["Error"] = $"Reference not found: {tx_ref}"; return RedirectToAction("Give"); }

            if (giving.PaymentStatus == PaymentStatus.Successful)
            { TempData["Info"] = "This payment is already confirmed."; return RedirectToAction("History"); }

            if (status is "successful" or "completed")
            {
                bool verified = false;
                try { verified = await _payment.VerifyTransactionAsync(transaction_id); } catch { }

                if (verified)
                {
                    giving.PaymentStatus  = PaymentStatus.Successful;
                    giving.FlutterwaveRef = transaction_id;
                    await _db.SaveChangesAsync();
                    await StartRecurringReminderAsync(giving);
                    if (giving.MemberId.HasValue)
                        await _activity.LogAsync(giving.MemberId.Value, MemberActivityType.Giving,
                            $"{giving.GivingType} {giving.Currency} {giving.Amount:N0}", giving.Id);

                    var member = giving.MemberId.HasValue
                        ? await _db.Members.FindAsync(giving.MemberId.Value) : null;
                    await _email.SendReceiptAsync(giving, member);

                    if (giving.MemberId.HasValue)
                    {
                        _db.Notifications.Add(new Notification
                        {
                            MemberId = giving.MemberId.Value,
                            Title    = "Giving Confirmed ✅",
                            Message  = $"Your {giving.GivingType} of {giving.Currency} " +
                                       $"{giving.Amount:N0} was received. Thank you!",
                            Link     = "/Giving/History"
                        });
                        await _db.SaveChangesAsync();
                    }

                    TempData["Success"] = $"Thank you! Your {giving.GivingType} of " +
                        $"{giving.Currency} {giving.Amount:N0} has been received.";
                    return RedirectToAction("GivingThankYou", new { id = giving.Id });
                }
            }

            giving.PaymentStatus = PaymentStatus.Failed;
            await _db.SaveChangesAsync();
            TempData["Error"] = $"Payment could not be verified. Contact us quoting: {tx_ref}";
            return RedirectToAction("Give");
        }

        // ── Giving history ───────────────────────────────────────
        public async Task<IActionResult> History(int page = 1)
        {
            var member = await GetMemberAsync();
            if (member == null) return NotFound();

            var now     = DateTime.UtcNow;
            var givings = await _db.Givings
                .Where(g => g.MemberId == member.Id)
                .OrderByDescending(g => g.GivingDate)
                .ToListAsync();

            // Totals cover every gift; the table shows one page
            var pager = new PagerModel(page, "gifts");
            ViewBag.Pager = pager;
            ViewBag.Reminders = await _db.RecurringGifts
                .Where(r => r.MemberId == member.Id && r.IsActive)
                .OrderBy(r => r.NextDueAt).ToListAsync();
            return View(new GivingHistoryViewModel
            {
                Givings        = givings.ToPage(pager),
                TotalThisYear  = givings.Where(g => g.GivingDate.Year == now.Year
                                                 && g.PaymentStatus == PaymentStatus.Successful)
                                        .Sum(g => g.Amount),
                TotalThisMonth = givings.Where(g => g.GivingDate.Month == now.Month
                                                 && g.GivingDate.Year == now.Year
                                                 && g.PaymentStatus == PaymentStatus.Successful)
                                        .Sum(g => g.Amount),
                ByType         = givings.Where(g => g.PaymentStatus == PaymentStatus.Successful)
                                        .GroupBy(g => g.GivingType.ToString())
                                        .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount))
            });
        }

        // ── Stop a giving reminder ───────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StopReminder(int id)
        {
            var member = await GetMemberAsync();
            var reminder = await _db.RecurringGifts.FirstOrDefaultAsync(r => r.Id == id && r.MemberId == member!.Id);
            if (reminder != null && reminder.IsActive)
            {
                reminder.IsActive  = false;
                reminder.StoppedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                TempData["Success"] = "Giving reminder stopped.";
            }
            return RedirectToAction("History");
        }

        private Task StartRecurringReminderAsync(Giving giving)
            => RecurringGiftReminderService.StartForGivingAsync(_db, giving);

        // Payment methods are admin-configured, so match the config's code/name to the closest enum value
        private static PaymentMethod ToPaymentMethod(PaymentMethodConfig m)
        {
            var s = $"{m.Code} {m.Name}".ToLowerInvariant();
            if (s.Contains("airtel")) return PaymentMethod.AirtelMoney;
            if (s.Contains("mpesa") || s.Contains("m-pesa")) return PaymentMethod.MPesa;
            if (s.Contains("mtn") || s.Contains("momo")) return PaymentMethod.MTNMobileMoney;
            if (s.Contains("master")) return PaymentMethod.Mastercard;
            if (s.Contains("visa") || s.Contains("card")) return PaymentMethod.Visa;
            if (s.Contains("cash")) return PaymentMethod.Cash;
            if (s.Contains("bank")) return PaymentMethod.Bank;
            return m.IntegrationType == PaymentIntegrationType.Manual ? PaymentMethod.Cash : PaymentMethod.Visa;
        }

        // ── API: get amounts for a method (used by JS) ───────────
        [HttpGet, AllowAnonymous]
        public async Task<IActionResult> GetMethodAmounts(string code)
        {
            var method = await _db.PaymentMethods
                .FirstOrDefaultAsync(m => m.Code == code && m.IsActive);
            if (method == null) return NotFound();
            return Json(new
            {
                amounts        = method.ParsedAmounts,
                currency       = method.Currency,
                currencySymbol = method.CurrencySymbol,
                integrationType= method.IntegrationType.ToString(),
                hasInstructions= !string.IsNullOrEmpty(method.InstructionsText),
                hasImage       = !string.IsNullOrEmpty(method.InstructionsImagePath),
            });
        }
    }
}
