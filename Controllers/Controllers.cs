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
    // ────────────────────────────────────────────────
    // HOME CONTROLLER
    // ────────────────────────────────────────────────
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _db;
        public HomeController(ApplicationDbContext db) { _db = db; }

        public async Task<IActionResult> Index()
        {
            //var vm = new
            //{
            //    RecentSermons = await _db.Sermons.Where(s => s.IsPublished)
            //        .OrderByDescending(s => s.SermonDate).Take(3).ToListAsync(),
            //    LiveSermon = await _db.Sermons.FirstOrDefaultAsync(s => s.IsLive),
            //    UpcomingEvents = await _db.ChurchEvents.Where(e => e.StartDate >= DateTime.UtcNow && e.IsPublic)
            //        .OrderBy(e => e.StartDate).Take(4).ToListAsync(),
            //    TotalMembers = await _db.Members.CountAsync(m => m.Status == MemberStatus.Active),
            //    PrayerWall = await _db.PrayerRequests.Where(p => p.IsPublic && !p.IsAnonymous)
            //        .OrderByDescending(p => p.CreatedAt).Take(5).ToListAsync(),
            //    NextEvent = await _db.ChurchEvents
            //        .Where(e => e.StartDate >= DateTime.Now && e.IsPublic)
            //        .OrderBy(e => e.StartDate)
            //        .FirstOrDefaultAsync()
            //};
            //return View(vm);
            var now = DateTime.UtcNow;
            var vm = new ChurchMinistry.ViewModels.HomeIndexViewModel
            {
                RecentSermons = await _db.Sermons
                    .Where(s => s.IsPublished)
                    .OrderByDescending(s => s.SermonDate)
                    .Take(3).ToListAsync(),

                LiveSermon = await _db.Sermons
                    .FirstOrDefaultAsync(s => s.IsLive),

                UpcomingEvents = await _db.ChurchEvents
                    .Where(e => e.StartDate >= now && e.IsPublic)
                    .OrderBy(e => e.StartDate)
                    .Take(4).ToListAsync(),

                NextEvent = await _db.ChurchEvents
                    .Where(e => e.StartDate >= now && e.IsPublic)
                    .OrderBy(e => e.StartDate)
                    .FirstOrDefaultAsync(),

                TotalMembers = await _db.Members
                    .CountAsync(m => m.Status == MemberStatus.Active),

                PrayerWall = await _db.PrayerRequests
                    .Where(p => p.IsPublic && !p.IsAnonymous)
                    .OrderByDescending(p => p.CreatedAt)
                    .Take(5).ToListAsync()
            };

            return View(vm);
        }

        public IActionResult About() => View();
        public IActionResult Contact() => View();

        [HttpPost]
        public async Task<IActionResult> Subscribe(NewsletterSubscribeViewModel vm, [FromServices] IEmailService email)
        {
            if (!ModelState.IsValid) return RedirectToAction("Index");
            if (!await _db.NewsletterSubscribers.AnyAsync(s => s.Email == vm.Email))
            {
                _db.NewsletterSubscribers.Add(new NewsletterSubscriber
                {
                    Email = vm.Email,
                    Name = vm.Name,
                    UnsubscribeToken = Guid.NewGuid().ToString()
                });
                await _db.SaveChangesAsync();
            }
            TempData["Success"] = "You have subscribed to our newsletter!";
            return RedirectToAction("Index");
        }
    }

    // ────────────────────────────────────────────────
    // ACCOUNT CONTROLLER
    // ────────────────────────────────────────────────
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ApplicationDbContext _db;
        private readonly IEmailService _email;
        private readonly IQrCodeService _qr;

        public AccountController(UserManager<ApplicationUser> um, SignInManager<ApplicationUser> sm,
            ApplicationDbContext db, IEmailService email, IQrCodeService qr)
        {
            _userManager = um; _signInManager = sm; _db = db; _email = email; _qr = qr;
        }

        [HttpGet] public IActionResult Login(string? returnUrl = null) { ViewBag.ReturnUrl = returnUrl; return View(); }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel vm, string? returnUrl = null)
        {
            if (!ModelState.IsValid) return View(vm);

            var result = await _signInManager.PasswordSignInAsync(vm.Email, vm.Password, vm.RememberMe, false);
            if (!result.Succeeded)
            {
                ModelState.AddModelError("", "Invalid email or password.");
                return View(vm);
            }

            // If a specific returnUrl was requested (e.g. they tried to access a
            // protected page before logging in), honour it regardless of role.
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            // Otherwise route by role
            var user = await _userManager.FindByEmailAsync(vm.Email);
            if (user != null)
            {
                if (await _userManager.IsInRoleAsync(user, "Admin") ||
                    await _userManager.IsInRoleAsync(user, "Pastor"))
                    return RedirectToAction("Index", "Admin");
            }

            return RedirectToAction("Dashboard", "Members");
        }

        [HttpGet] public IActionResult Register() => View();

        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel vm)
        {
            if (!ModelState.IsValid) return View(vm);
            var user = new ApplicationUser { UserName = vm.Email, Email = vm.Email, FirstName = vm.FirstName, LastName = vm.LastName };
            var result = await _userManager.CreateAsync(user, vm.Password);
            if (!result.Succeeded)
            {
                foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
                return View(vm);
            }

            await _userManager.AddToRoleAsync(user, "Member");

            var qrData = _qr.GetMemberQrData(0, vm.Email);
            var member = new Member
            {
                FirstName = vm.FirstName,
                LastName = vm.LastName,
                Email = vm.Email,
                Phone = vm.Phone,
                UserId = user.Id,
                QrCode = _qr.GenerateQrCode(qrData)
            };
            _db.Members.Add(member);
            await _db.SaveChangesAsync();

            await _signInManager.SignInAsync(user, false);
            await _email.SendWelcomeAsync(member);

            // Welcome notification for the new member
            _db.Notifications.Add(new Notification
            {
                MemberId = member.Id,
                Title = "Welcome to " + (HttpContext.RequestServices
                    .GetRequiredService<IConfiguration>()["Church:Name"] ?? "our church"),
                Message = "Your membership has been confirmed. Explore sermons, prayer requests, events and more!",
                Link = "/Members/Dashboard"
            });
            await _db.SaveChangesAsync();

            return RedirectToAction("Dashboard", "Members");
        }

        [HttpPost, Authorize]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        [HttpGet] public IActionResult ForgotPassword() => View();

        [HttpPost]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user != null)
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var link = Url.Action("ResetPassword", "Account", new { token, email }, Request.Scheme);
                await _email.SendEmailAsync(email, "Password Reset", $"<p>Click <a href='{link}'>here</a> to reset your password.</p>");
            }
            TempData["Info"] = "If that email exists, a reset link has been sent.";
            return RedirectToAction("Login");
        }
    }

    // ────────────────────────────────────────────────
    // MEMBERS CONTROLLER
    // ────────────────────────────────────────────────
    [Authorize]
    public class MembersController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;

        public MembersController(ApplicationDbContext db, UserManager<ApplicationUser> um, IWebHostEnvironment env)
        {
            _db = db; _userManager = um; _env = env;
        }

        private async Task<Member?> GetCurrentMemberAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            return user == null ? null : await _db.Members.FirstOrDefaultAsync(m => m.UserId == user.Id);
        }

        public async Task<IActionResult> Dashboard()
        {
            var member = await GetCurrentMemberAsync();
            if (member == null) return RedirectToAction("Login", "Account");

            var now = DateTime.UtcNow;
            var vm = new MemberDashboardViewModel
            {
                Member = member,
                AttendanceThisMonth = await _db.AttendanceRecords
                    .CountAsync(a => a.MemberId == member.Id && a.CheckInTime.Month == now.Month),
                TotalGivingThisYear = await _db.Givings
                    .Where(g => g.MemberId == member.Id && g.GivingDate.Year == now.Year && g.PaymentStatus == PaymentStatus.Successful)
                    .SumAsync(g => g.Amount),
                PrayerRequestsCount = await _db.PrayerRequests.CountAsync(p => p.MemberId == member.Id),
                RecentSermons = await _db.Sermons.Where(s => s.IsPublished)
                    .OrderByDescending(s => s.SermonDate).Take(3).ToListAsync(),
                UpcomingEvents = await _db.ChurchEvents.Where(e => e.StartDate >= now && e.IsPublic)
                    .OrderBy(e => e.StartDate).Take(3).ToListAsync(),
                Notifications = await _db.Notifications
                    .Where(n => n.MemberId == member.Id && !n.IsRead)
                    .OrderByDescending(n => n.CreatedAt).Take(10).ToListAsync()
            };
            return View(vm);
        }

        [HttpGet("profile")]
        public async Task<IActionResult> Profile()
        {
            var member = await GetCurrentMemberAsync();
            if (member == null) return NotFound();
            return View(new MemberProfileViewModel
            {
                Id = member.Id,
                FirstName = member.FirstName,
                LastName = member.LastName,
                Email = member.Email,
                Phone = member.Phone,
                DateOfBirth = member.DateOfBirth,
                Gender = member.Gender,
                MaritalStatus = member.MaritalStatus,
                Address = member.Address,
                City = member.City,
                Country = member.Country,
                Occupation = member.Occupation,
                MinistryGroupId = member.MinistryGroupId,
                EmergencyContact = member.EmergencyContact,
                ExistingPhoto = member.ProfilePhoto
            });
        }

        //View any member's profile by ID (read-only for others, editable for self/admin)
        [HttpGet("profile/{id}")]
        public async Task<IActionResult> Profile(int? id)
        {
            Member? member;

            if (id.HasValue)
            {
                // Viewing another member's profile
                member = await _db.Members
                    .Include(m => m.MinistryGroup)
                    .FirstOrDefaultAsync(m => m.Id == id.Value);
                if (member == null) return NotFound();

                var currentMember = await GetCurrentMemberAsync();
                bool isOwnProfile = currentMember?.Id == member.Id;
                bool isAdmin = User.IsInRole("Admin") || User.IsInRole("Pastor");

                // Admins and the member themselves can edit; others see read-only view
                if (isOwnProfile || isAdmin)
                    return View("EditProfile", MapToViewModel(member));

                return View("ViewProfile", member);
            }
            else
            {
                // No id supplied → load own profile for editing
                member = await GetCurrentMemberAsync();
                if (member == null) return NotFound();
                return View("EditProfile", MapToViewModel(member));
            }
        }

        [HttpPost("profile/{id}")]
        public async Task<IActionResult> Profile(int? id, MemberProfileViewModel vm)
        {
            if (!ModelState.IsValid) return View("EditProfile", vm);

            // Admins can edit any member; regular members can only edit themselves
            Member? member;
            if (User.IsInRole("Admin") || User.IsInRole("Pastor"))
                member = await _db.Members.FindAsync(id);
            else
                member = await GetCurrentMemberAsync();

            if (member == null) return NotFound();

            // Prevent a regular member from editing someone else's profile via form tampering
            if (!User.IsInRole("Admin") && !User.IsInRole("Pastor"))
            {
                var current = await GetCurrentMemberAsync();
                if (current?.Id != member.Id) return Forbid();
            }

            if (vm.ProfilePhotoFile != null)
            {
                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(vm.ProfilePhotoFile.FileName)}";
                var path = Path.Combine(_env.WebRootPath, "uploads", fileName);
                using var stream = new FileStream(path, FileMode.Create);
                await vm.ProfilePhotoFile.CopyToAsync(stream);
                member.ProfilePhoto = $"/uploads/{fileName}";
            }

            member.FirstName = vm.FirstName; member.LastName = vm.LastName;
            member.Phone = vm.Phone; member.DateOfBirth = vm.DateOfBirth;
            member.Gender = vm.Gender; member.MaritalStatus = vm.MaritalStatus;
            member.Address = vm.Address; member.City = vm.City; member.Country = vm.Country;
            member.Occupation = vm.Occupation; member.MinistryGroupId = vm.MinistryGroupId;
            member.EmergencyContact = vm.EmergencyContact; member.EmergencyPhone = vm.EmergencyPhone;

            await _db.SaveChangesAsync();
            TempData["Success"] = "Profile updated successfully!";

            if (User.IsInRole("Admin") || User.IsInRole("Pastor"))
            {
                return RedirectToAction("Members", "Admin");
            }
            else
            {
                return RedirectToAction("Profile", new { id = member.Id });
            }
        }

        private static MemberProfileViewModel MapToViewModel(Member m) => new()
        {
            Id = m.Id,
            FirstName = m.FirstName,
            LastName = m.LastName,
            Email = m.Email,
            Phone = m.Phone,
            DateOfBirth = m.DateOfBirth,
            Gender = m.Gender,
            MaritalStatus = m.MaritalStatus,
            Address = m.Address,
            City = m.City,
            Country = m.Country,
            Occupation = m.Occupation,
            MinistryGroupId = m.MinistryGroupId,
            EmergencyContact = m.EmergencyContact,
            EmergencyPhone = m.EmergencyPhone,
            ExistingPhoto = m.ProfilePhoto
        };

        public async Task<IActionResult> Directory(string? search, string? group)
        {
            var q = _db.Members.Include(m => m.MinistryGroup)
                .Where(m => m.Status == MemberStatus.Active).AsQueryable();
            if (!string.IsNullOrEmpty(search))
                q = q.Where(m => m.FirstName.Contains(search) || m.LastName.Contains(search));
            if (!string.IsNullOrEmpty(group) && int.TryParse(group, out int gid))
                q = q.Where(m => m.MinistryGroupId == gid);
            ViewBag.Groups = await _db.MinistryGroups.Where(g => g.IsActive).ToListAsync();
            return View(await q.OrderBy(m => m.FirstName).ToListAsync());
        }

        public async Task<IActionResult> MyQrCode()
        {
            var member = await GetCurrentMemberAsync();
            if (member == null) return NotFound();
            return View(member);
        }

        public async Task<IActionResult> Attendance()
        {
            var member = await GetCurrentMemberAsync();
            if (member == null) return NotFound();
            var records = await _db.AttendanceRecords
                .Include(a => a.ServiceSession)
                .Where(a => a.MemberId == member.Id)
                .OrderByDescending(a => a.CheckInTime)
                .ToListAsync();
            return View(records);
        }
    }

    // ────────────────────────────────────────────────
    // GIVING CONTROLLER
    // ────────────────────────────────────────────────
    //[Authorize]
    //public class GivingController : Controller
    //{
    //    private readonly ApplicationDbContext _db;
    //    private readonly UserManager<ApplicationUser> _userManager;
    //    private readonly IPaymentService _payment;
    //    private readonly IEmailService _email;

    //    public GivingController(ApplicationDbContext db, UserManager<ApplicationUser> um,
    //        IPaymentService payment, IEmailService email)
    //    {
    //        _db = db; _userManager = um; _payment = payment; _email = email;
    //    }

    //    [HttpGet] public IActionResult Give() => View(new GivingViewModel());

    //    [HttpPost]
    //    public async Task<IActionResult> Give(GivingViewModel vm)
    //    {
    //        if (!ModelState.IsValid) return View(vm);
    //        var user = await _userManager.GetUserAsync(User);
    //        var member = user == null ? null : await _db.Members.FirstOrDefaultAsync(m => m.UserId == user.Id);

    //        var txRef = $"CHURCH-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8].ToUpper()}";

    //        var giving = new Giving
    //        {
    //            MemberId = member?.Id,
    //            Amount = vm.Amount,
    //            Currency = vm.Currency,
    //            GivingType = vm.GivingType,
    //            PaymentMethod = vm.PaymentMethod,
    //            PhoneNumber = vm.PhoneNumber,
    //            IsRecurring = vm.IsRecurring,
    //            RecurringInterval = vm.RecurringInterval,
    //            Notes = vm.Notes,
    //            TransactionId = txRef,
    //            PaymentStatus = PaymentStatus.Pending
    //        };
    //        _db.Givings.Add(giving);
    //        await _db.SaveChangesAsync();

    //        // Card payments → redirect to Flutterwave hosted page
    //        if (vm.PaymentMethod is PaymentMethod.Visa or PaymentMethod.Mastercard)
    //        {
    //            var pubKey = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Flutterwave:PublicKey"];
    //            ViewBag.TxRef = txRef;
    //            ViewBag.PubKey = pubKey;
    //            ViewBag.Amount = vm.Amount;
    //            ViewBag.Currency = vm.Currency;
    //            ViewBag.Email = user?.Email ?? "guest@apostleenockmakoko.org";
    //            return View("CardPayment", giving);
    //        }

    //        // Mobile money push
    //        var result = await _payment.InitiateMobileMoneyPaymentAsync(vm, user?.Id ?? "guest", txRef);
    //        if (result.Success)
    //        {
    //            giving.FlutterwaveRef = result.FlutterwaveRef;
    //            await _db.SaveChangesAsync();
    //            TempData["Info"] = "Please approve the payment prompt on your phone.";
    //            return RedirectToAction("GivingPending", new { id = giving.Id });
    //        }

    //        TempData["Error"] = result.Message;
    //        return View(vm);
    //    }

    //    public async Task<IActionResult> GivingPending(int id)
    //    {
    //        var giving = await _db.Givings.FindAsync(id);
    //        return View(giving);
    //    }

    //    [AllowAnonymous]
    //    [HttpPost]
    //    //public async Task<IActionResult> Callback(FlutterwaveCallback cb)
    //    public async Task<IActionResult> Callback([FromBody] FlutterwaveCallback cb, [FromServices] IConfiguration config)
    //    {
    //        // Verify it's genuinely from Flutterwave
    //        var hash = Request.Headers["verif-hash"].ToString();
    //        var expectedHash = config["Flutterwave:WebhookSecret"];
    //        if (hash != expectedHash) return Unauthorized();
    //        if (cb.status == "successful")
    //        {
    //            var verified = await _payment.VerifyTransactionAsync(cb.transaction_id);
    //            var giving = await _db.Givings.FirstOrDefaultAsync(g => g.TransactionId == cb.tx_ref);
    //            if (giving != null)//Wendy Return on LIVE --- giving != null && verified
    //            {
    //                giving.PaymentStatus = PaymentStatus.Successful;
    //                giving.FlutterwaveRef = cb.transaction_id;
    //                await _db.SaveChangesAsync();
    //                var member = giving.MemberId.HasValue
    //                    ? await _db.Members.FindAsync(giving.MemberId.Value) : null;
    //                await _email.SendReceiptAsync(giving, member);

    //                // Notify the member their giving went through
    //                if (giving.MemberId.HasValue)
    //                {
    //                    _db.Notifications.Add(new Notification
    //                    {
    //                        MemberId = giving.MemberId.Value,
    //                        Title = "Giving Confirmed",
    //                        Message = $"Your {giving.GivingType} of {giving.Currency} {giving.Amount:N0} was received successfully. Thank you!",
    //                        Link = "/Giving/History"
    //                    });
    //                    await _db.SaveChangesAsync();
    //                }
    //            }
    //        }
    //        return Ok();
    //    }

    //    public async Task<IActionResult> History()
    //    {
    //        var user = await _userManager.GetUserAsync(User);
    //        var member = user == null ? null : await _db.Members.FirstOrDefaultAsync(m => m.UserId == user.Id);
    //        if (member == null) return NotFound();

    //        var now = DateTime.UtcNow;
    //        var givings = await _db.Givings.Where(g => g.MemberId == member.Id)
    //            .OrderByDescending(g => g.GivingDate).ToListAsync();

    //        return View(new GivingHistoryViewModel
    //        {
    //            Givings = givings,
    //            TotalThisYear = givings.Where(g => g.GivingDate.Year == now.Year && g.PaymentStatus == PaymentStatus.Successful).Sum(g => g.Amount),
    //            TotalThisMonth = givings.Where(g => g.GivingDate.Month == now.Month && g.GivingDate.Year == now.Year && g.PaymentStatus == PaymentStatus.Successful).Sum(g => g.Amount),
    //            ByType = givings.Where(g => g.PaymentStatus == PaymentStatus.Successful)
    //                .GroupBy(g => g.GivingType.ToString())
    //                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount))
    //        });
    //    }
    //}

    // ────────────────────────────────────────────────
    // MEDIA CONTROLLER
    // ────────────────────────────────────────────────
    //public class MediaController : Controller
    //{
    //    private readonly ApplicationDbContext _db;
    //    public MediaController(ApplicationDbContext db) { _db = db; }

    //    public async Task<IActionResult> Index(string? search, string? speaker, string? series, int page = 1)
    //    {
    //        const int pageSize = 12;
    //        var q = _db.Sermons.Where(s => s.IsPublished).AsQueryable();
    //        if (!string.IsNullOrEmpty(search)) q = q.Where(s => s.Title.Contains(search) || s.Description!.Contains(search));
    //        if (!string.IsNullOrEmpty(speaker)) q = q.Where(s => s.Speaker == speaker);
    //        if (!string.IsNullOrEmpty(series)) q = q.Where(s => s.SeriesName == series);

    //        var total = await q.CountAsync();
    //        var sermons = await q.OrderByDescending(s => s.SermonDate)
    //            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

    //        return View(new SermonIndexViewModel
    //        {
    //            Sermons = sermons,
    //            LiveSermon = await _db.Sermons.FirstOrDefaultAsync(s => s.IsLive),
    //            SearchTerm = search,
    //            Speaker = speaker,
    //            Series = series,
    //            Page = page,
    //            TotalPages = (int)Math.Ceiling((double)total / pageSize)
    //        });
    //    }

    //    //public async Task<IActionResult> Watch(int id)
    //    //{
    //    //    var sermon = await _db.Sermons.FindAsync(id);
    //    //    if (sermon == null) return NotFound();
    //    //    sermon.ViewCount++;
    //    //    await _db.SaveChangesAsync();
    //    //    return View(sermon);
    //    //}

    //    public async Task<IActionResult> Watch(int id)
    //    {
    //        var sermon = await _db.Sermons.FindAsync(id);
    //        if (sermon == null) return NotFound();

    //        sermon.ViewCount++;
    //        await _db.SaveChangesAsync();

    //        // ── Recommendation logic ──────────────────────────────
    //        // Priority 1: same series (excluding current)
    //        var recommended = await _db.Sermons
    //            .Where(s => s.IsPublished
    //                     && s.Id != id
    //                     && s.SeriesName != null
    //                     && s.SeriesName == sermon.SeriesName)
    //            .OrderByDescending(s => s.SermonDate)
    //            .Take(3)
    //            .ToListAsync();

    //        // Priority 2: same speaker - fill remaining slots
    //        if (recommended.Count < 3)
    //        {
    //            var existingIds = recommended.Select(s => s.Id).ToList();
    //            existingIds.Add(id);

    //            var bySpeaker = await _db.Sermons
    //                .Where(s => s.IsPublished
    //                         && !existingIds.Contains(s.Id)
    //                         && s.Speaker == sermon.Speaker)
    //                .OrderByDescending(s => s.SermonDate)
    //                .Take(3 - recommended.Count)
    //                .ToListAsync();

    //            recommended.AddRange(bySpeaker);
    //        }

    //        // Priority 3: latest published sermons - fill any remaining slots
    //        if (recommended.Count < 3)
    //        {
    //            var existingIds = recommended.Select(s => s.Id).ToList();
    //            existingIds.Add(id);

    //            var latest = await _db.Sermons
    //                .Where(s => s.IsPublished && !existingIds.Contains(s.Id))
    //                .OrderByDescending(s => s.SermonDate)
    //                .Take(3 - recommended.Count)
    //                .ToListAsync();

    //            recommended.AddRange(latest);
    //        }

    //        ViewBag.Recommended = recommended;
    //        return View(sermon);
    //    }

    //    public async Task<IActionResult> Live()
    //    {
    //        var live = await _db.Sermons.FirstOrDefaultAsync(s => s.IsLive);
    //        return View(live);
    //    }
    //}

    // ────────────────────────────────────────────────
    // COMMUNITY CONTROLLER
    // ────────────────────────────────────────────────
    [Authorize]
    public class CommunityController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public CommunityController(ApplicationDbContext db, UserManager<ApplicationUser> um)
        {
            _db = db; _userManager = um;
        }

        private async Task<Member?> GetMemberAsync() =>
            (await _userManager.GetUserAsync(User)) is { } u
                ? await _db.Members.FirstOrDefaultAsync(m => m.UserId == u.Id)
                : null;

        public async Task<IActionResult> Index(ForumCategory? category, int page = 1)
        {
            const int ps = 20;
            var q = _db.ForumPosts.Include(p => p.Member).Include(p => p.Replies).AsQueryable();
            if (category.HasValue) q = q.Where(p => p.Category == category.Value);
            var total = await q.CountAsync();
            ViewBag.Posts = await q.OrderByDescending(p => p.IsPinned).ThenByDescending(p => p.CreatedAt)
                .Skip((page - 1) * ps).Take(ps).ToListAsync();
            ViewBag.Total = total; ViewBag.Page = page; ViewBag.TotalPages = (int)Math.Ceiling((double)total / ps);
            return View();
        }

        public async Task<IActionResult> Post(int id)
        {
            var post = await _db.ForumPosts.Include(p => p.Member)
                .Include(p => p.Replies).ThenInclude(r => r.Member)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (post == null) return NotFound();
            post.ViewCount++;
            await _db.SaveChangesAsync();
            return View(post);
        }

        [HttpGet] public IActionResult NewPost() => View(new ForumPostViewModel());

        [HttpPost]
        public async Task<IActionResult> NewPost(ForumPostViewModel vm)
        {
            if (!ModelState.IsValid) return View(vm);
            var member = await GetMemberAsync();
            if (member == null) return Unauthorized();

            _db.ForumPosts.Add(new ForumPost
            {
                Title = vm.Title,
                Content = vm.Content,
                MemberId = member.Id,
                Category = vm.Category,
                IsPastorQuestion = vm.IsPastorQuestion
            });
            await _db.SaveChangesAsync();
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> Reply(int postId, string content)
        {
            var member = await GetMemberAsync();
            if (member == null) return Unauthorized();
            var post = await _db.ForumPosts.FindAsync(postId);
            if (post == null) return NotFound();

            _db.ForumReplies.Add(new ForumReply
            {
                ForumPostId = postId,
                MemberId = member.Id,
                Content = content,
                IsPastorReply = User.IsInRole("Pastor")
            });
            await _db.SaveChangesAsync();
            return RedirectToAction("Post", new { id = postId });
        }

        public IActionResult Chat() => View();
    }

    // ────────────────────────────────────────────────
    // NEWSLETTER CONTROLLER
    // ────────────────────────────────────────────────
    public class NewsletterController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IEmailService _email;

        public NewsletterController(ApplicationDbContext db, IEmailService email)
        {
            _db = db; _email = email;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.Newsletters = await _db.Newsletters
                .Where(n => n.Status == NewsletterStatus.Sent)
                .OrderByDescending(n => n.SentAt).Take(10).ToListAsync();
            return View();
        }

        [HttpGet] public IActionResult Unsubscribe(string token) => View(model: token);

        [HttpPost]
        public async Task<IActionResult> Unsubscribe(string token, string confirm)
        {
            var sub = await _db.NewsletterSubscribers.FirstOrDefaultAsync(s => s.UnsubscribeToken == token);
            if (sub != null) { sub.IsActive = false; await _db.SaveChangesAsync(); }
            TempData["Info"] = "You have been unsubscribed.";
            return RedirectToAction("Index", "Home");
        }
    }

    //// ────────────────────────────────────────────────
    //// EVENTS CONTROLLER
    //// ────────────────────────────────────────────────
    //public class EventsController : Controller
    //{
    //    private readonly ApplicationDbContext _db;
    //    private readonly UserManager<ApplicationUser> _userManager;

    //    public EventsController(ApplicationDbContext db, UserManager<ApplicationUser> um)
    //    {
    //        _db = db; _userManager = um;
    //    }

    //    public async Task<IActionResult> Index()
    //    {
    //        var events = await _db.ChurchEvents.Where(e => e.IsPublic)
    //            .OrderBy(e => e.StartDate).ToListAsync();
    //        return View(events);
    //    }

    //    public async Task<IActionResult> Details(int id)
    //    {
    //        var ev = await _db.ChurchEvents.Include(e => e.Registrations).FirstOrDefaultAsync(e => e.Id == id);
    //        if (ev == null) return NotFound();
    //        return View(ev);
    //    }

    //    [HttpPost, Authorize]
    //    public async Task<IActionResult> Register(EventRegisterViewModel vm)
    //    {
    //        if (!ModelState.IsValid) return RedirectToAction("Details", new { id = vm.EventId });
    //        var user = await _userManager.GetUserAsync(User);
    //        var member = user == null ? null : await _db.Members.FirstOrDefaultAsync(m => m.UserId == user.Id);

    //        _db.EventRegistrations.Add(new EventRegistration
    //        {
    //            EventId = vm.EventId,
    //            MemberId = member?.Id,
    //            Name = vm.Name,
    //            Email = vm.Email,
    //            Phone = vm.Phone
    //        });
    //        await _db.SaveChangesAsync();
    //        TempData["Success"] = "You have been registered for this event!";
    //        return RedirectToAction("Details", new { id = vm.EventId });
    //    }
    //}

    // ────────────────────────────────────────────────
    // ADMIN CONTROLLER
    // ────────────────────────────────────────────────
    [Authorize(Roles = "Admin,Pastor")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailService _email;
        private readonly ISmsService _sms;
        private readonly IAttendanceService _attendance;

        public AdminController(ApplicationDbContext db, UserManager<ApplicationUser> um,
            IEmailService email, ISmsService sms, IAttendanceService attendance)
        {
            _db = db; _userManager = um; _email = email; _sms = sms; _attendance = attendance;
        }

        public async Task<IActionResult> Index()
        {
            var now = DateTime.UtcNow;
            var lastService = await _db.ServiceSessions.OrderByDescending(s => s.ServiceDate).FirstOrDefaultAsync();
            var prevService = await _db.ServiceSessions.OrderByDescending(s => s.ServiceDate).Skip(1).FirstOrDefaultAsync();

            int lastCount = lastService == null ? 0 : await _db.AttendanceRecords.CountAsync(a => a.ServiceSessionId == lastService.Id);
            int prevCount = prevService == null ? 0 : await _db.AttendanceRecords.CountAsync(a => a.ServiceSessionId == prevService.Id);

            var vm = new AdminDashboardViewModel
            {
                TotalMembers = await _db.Members.CountAsync(),
                ActiveMembers = await _db.Members.CountAsync(m => m.Status == MemberStatus.Active),
                NewMembersThisMonth = await _db.Members.CountAsync(m => m.JoinDate.Month == now.Month && m.JoinDate.Year == now.Year),
                TotalGivingThisMonth = await _db.Givings
                    .Where(g => g.GivingDate.Month == now.Month && g.GivingDate.Year == now.Year && g.PaymentStatus == PaymentStatus.Successful)
                    .SumAsync(g => (decimal?)g.Amount) ?? 0,
                TotalGivingThisYear = await _db.Givings
                    .Where(g => g.GivingDate.Year == now.Year && g.PaymentStatus == PaymentStatus.Successful)
                    .SumAsync(g => (decimal?)g.Amount) ?? 0,
                AttendanceLastService = lastCount,
                AttendanceGrowthPercent = prevCount == 0 ? 0 : Math.Round(((double)(lastCount - prevCount) / prevCount) * 100, 1),
                PrayerRequestsPending = await _db.PrayerRequests.CountAsync(p => !p.IsAnswered),
                UpcomingEvents = await _db.ChurchEvents.CountAsync(e => e.StartDate >= now),
                RecentMembers = await _db.Members.OrderByDescending(m => m.JoinDate).Take(5).ToListAsync(),
                RecentGivings = await _db.Givings.Include(g => g.Member)
                    .OrderByDescending(g => g.GivingDate).Take(5).ToListAsync(),
                RecentServices = await _db.ServiceSessions.OrderByDescending(s => s.ServiceDate).Take(5).ToListAsync(),
                GivingByType = (await _db.Givings
                    .Where(g => g.PaymentStatus == PaymentStatus.Successful && g.GivingDate.Year == now.Year)
                    .Select(g => new { g.GivingType, g.Amount })
                    .ToListAsync())
                    .GroupBy(g => g.GivingType.ToString())
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount)),
                MembersByGroup = await _db.MinistryGroups.Where(x => x.IsActive).Include(g => g.Members)
                    .ToDictionaryAsync(g => g.Name, g => g.Members.Count)
            };
            return View(vm);
        }

        public async Task<IActionResult> EventRegistrations(int id)
        {
            var ev = await _db.ChurchEvents
                .Include(e => e.Registrations)
                .ThenInclude(r => r.Member)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (ev == null) return NotFound();

            ViewBag.Event = ev;
            ViewBag.Registrations = ev.Registrations
                .OrderByDescending(r => r.RegisteredAt)
                .ToList();

            return View(ev);
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


        // ── Members management ──
        public async Task<IActionResult> Members(string? search, string? status, int page = 1)
        {
            const int ps = 25;
            var q = _db.Members.Include(m => m.MinistryGroup).AsQueryable();
            if (!string.IsNullOrEmpty(search))
                q = q.Where(m => m.FirstName.Contains(search) || m.LastName.Contains(search) || m.Email.Contains(search));
            if (Enum.TryParse<MemberStatus>(status, out var ms))
                q = q.Where(m => m.Status == ms);
            ViewBag.Total = await q.CountAsync();
            ViewBag.Members = await q.OrderBy(m => m.FirstName).Skip((page - 1) * ps).Take(ps).ToListAsync();
            ViewBag.Page = page; ViewBag.TotalPages = (int)Math.Ceiling((double)ViewBag.Total / ps);
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> UpdateMemberStatus(int id, MemberStatus status)
        {
            var member = await _db.Members.FindAsync(id);
            if (member != null) { member.Status = status; await _db.SaveChangesAsync(); }
            return Json(new { success = true });
        }

        // ── Attendance management ──
        public async Task<IActionResult> Attendance()
        {
            ViewBag.Sessions = await _db.ServiceSessions.OrderByDescending(s => s.ServiceDate).Take(10)
                .Include(x => x.AttendanceRecords)
                .ToListAsync();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CreateSession(ServiceSession session)
        {
            _db.ServiceSessions.Add(session);
            await _db.SaveChangesAsync();
            return RedirectToAction("Attendance");
        }

        [HttpPost]
        public async Task<IActionResult> CheckInMember([FromBody] AttendanceCheckInViewModel vm)
        {
            var success = await _attendance.CheckInAsync(vm.QrCode, vm.ServiceSessionId);
            return Json(new { success, message = success ? "Checked in successfully!" : "Already checked in or invalid QR." });
        }

        public async Task<IActionResult> AttendanceReport(int sessionId)
        {
            var session = await _db.ServiceSessions.Include(s => s.AttendanceRecords)
                .ThenInclude(a => a.Member).FirstOrDefaultAsync(s => s.Id == sessionId);
            return View(session);
        }

        // ── Giving management ──
        public async Task<IActionResult> Givings(string? type, string? method, int page = 1)
        {
            const int ps = 25;
            var q = _db.Givings.Include(g => g.Member).AsQueryable();
            if (Enum.TryParse<GivingType>(type, out var gt)) q = q.Where(g => g.GivingType == gt);
            if (Enum.TryParse<PaymentMethod>(method, out var pm)) q = q.Where(g => g.PaymentMethod == pm);
            ViewBag.Total = await q.CountAsync();
            ViewBag.Givings = await q.OrderByDescending(g => g.GivingDate).Skip((page - 1) * ps).Take(ps).ToListAsync();
            return View();
        }

        // ── Sermons management ──
        //public async Task<IActionResult> Sermons() =>
        //    View(await _db.Sermons.OrderByDescending(s => s.SermonDate).ToListAsync());

        public async Task<IActionResult> Sermons(string? search, string? speaker,
            string? series, bool? published, int page = 1)
        {
            const int pageSize = 15;

            var q = _db.Sermons.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(s => s.Title.Contains(search)
                              || (s.Speaker != null && s.Speaker.Contains(search))
                              || (s.ScriptureReference != null && s.ScriptureReference.Contains(search))
                              || (s.Tags != null && s.Tags.Contains(search)));

            if (!string.IsNullOrWhiteSpace(speaker))
                q = q.Where(s => s.Speaker != null && s.Speaker.Contains(speaker));

            if (!string.IsNullOrWhiteSpace(series))
                q = q.Where(s => s.SeriesName != null && s.SeriesName.Contains(series));

            if (published.HasValue)
                q = q.Where(s => s.IsPublished == published.Value);

            var total = await q.CountAsync();
            var sermons = await q.OrderByDescending(s => s.SermonDate)
                                 .Skip((page - 1) * pageSize)
                                 .Take(pageSize)
                                 .ToListAsync();

            // Distinct speaker and series lists for filter dropdowns
            ViewBag.Speakers = await _db.Sermons
                .Where(s => s.Speaker != null)
                .Select(s => s.Speaker).Distinct().OrderBy(s => s).ToListAsync();
            ViewBag.SeriesList = await _db.Sermons
                .Where(s => s.SeriesName != null)
                .Select(s => s.SeriesName).Distinct().OrderBy(s => s).ToListAsync();

            ViewBag.Search = search;
            ViewBag.Speaker = speaker;
            ViewBag.Series = series;
            ViewBag.Published = published;
            ViewBag.Page = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)total / pageSize);
            ViewBag.Total = total;

            return View(sermons);
        }

        //[HttpGet] public IActionResult AddSermon() => View(new Sermon());

        //[HttpPost]
        //public async Task<IActionResult> AddSermon(Sermon sermon, IFormFile? thumbnail, [FromServices] IWebHostEnvironment env)
        //{
        //    if (thumbnail != null)
        //    {
        //        var fn = $"{Guid.NewGuid()}{Path.GetExtension(thumbnail.FileName)}";
        //        var path = Path.Combine(env.WebRootPath, "uploads", fn);
        //        using var s = new FileStream(path, FileMode.Create);
        //        await thumbnail.CopyToAsync(s);
        //        sermon.ThumbnailUrl = $"/uploads/{fn}";
        //    }
        //    _db.Sermons.Add(sermon);
        //    await _db.SaveChangesAsync();
        //    TempData["Success"] = "Sermon added!";
        //    return RedirectToAction("Sermons");
        //}

        //[HttpGet]
        //public async Task<IActionResult> EditSermon(int id)
        //{
        //    var sermon = await _db.Sermons.FindAsync(id);
        //    if (sermon == null) return NotFound();
        //    return View(sermon);
        //}

        //[HttpPost]
        //public async Task<IActionResult> EditSermon(Sermon updated, IFormFile? thumbnail, [FromServices] IWebHostEnvironment env)
        //{
        //    if (!ModelState.IsValid) return View(updated);

        //    var sermon = await _db.Sermons.FindAsync(updated.Id);
        //    if (sermon == null) return NotFound();

        //    // Replace thumbnail only if a new one was uploaded
        //    if (thumbnail != null)
        //    {
        //        var fn = $"{Guid.NewGuid()}{Path.GetExtension(thumbnail.FileName)}";
        //        var path = Path.Combine(env.WebRootPath, "uploads", fn);
        //        using var s = new FileStream(path, FileMode.Create);
        //        await thumbnail.CopyToAsync(s);
        //        sermon.ThumbnailUrl = $"/uploads/{fn}";
        //    }

        //    sermon.Title = updated.Title;
        //    sermon.Speaker = updated.Speaker;
        //    sermon.SeriesName = updated.SeriesName;
        //    sermon.SermonDate = updated.SermonDate;
        //    sermon.ScriptureReference = updated.ScriptureReference;
        //    sermon.Description = updated.Description;
        //    sermon.VideoUrl = updated.VideoUrl;
        //    sermon.AudioUrl = updated.AudioUrl;
        //    sermon.LiveStreamUrl = updated.LiveStreamUrl;
        //    sermon.Tags = updated.Tags;
        //    sermon.IsPublished = updated.IsPublished;

        //    await _db.SaveChangesAsync();
        //    TempData["Success"] = "Sermon updated successfully!";
        //    return RedirectToAction("Sermons");
        //}

        // ── Add Sermon GET ──────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> AddSermon()
        {
            await PopulateMeetingRoomsAsync();
            return View(new Sermon());
        }

        // ── Add Sermon POST ─────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> AddSermon(
            Sermon sermon,
            IFormFile? thumbnail,
            [FromServices] IWebHostEnvironment env)
        {
            if (thumbnail != null)
            {
                var fn = $"{Guid.NewGuid()}{Path.GetExtension(thumbnail.FileName)}";
                var path = Path.Combine(env.WebRootPath, "uploads", fn);
                using var s = new FileStream(path, FileMode.Create);
                await thumbnail.CopyToAsync(s);
                sermon.ThumbnailUrl = $"/uploads/{fn}";
            }

            // If linked to an online meeting, clear LiveStreamUrl — the meeting hub handles the stream
            if (sermon.IsOnlineMeeting && !string.IsNullOrEmpty(sermon.MeetingRoomCode))
                sermon.LiveStreamUrl = null;

            _db.Sermons.Add(sermon);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Sermon added!";
            return RedirectToAction("Sermons");
        }

        // ── Edit Sermon GET ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> EditSermon(int id)
        {
            var sermon = await _db.Sermons.FindAsync(id);
            if (sermon == null) return NotFound();
            await PopulateMeetingRoomsAsync();
            return View(sermon);
        }

        // ── Edit Sermon POST ────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> EditSermon(
            Sermon updated,
            IFormFile? thumbnail,
            [FromServices] IWebHostEnvironment env)
        {
            var sermon = await _db.Sermons.FindAsync(updated.Id);
            if (sermon == null) return NotFound();

            if (thumbnail != null)
            {
                var fn = $"{Guid.NewGuid()}{Path.GetExtension(thumbnail.FileName)}";
                var path = Path.Combine(env.WebRootPath, "uploads", fn);
                using var s = new FileStream(path, FileMode.Create);
                await thumbnail.CopyToAsync(s);
                sermon.ThumbnailUrl = $"/uploads/{fn}";
            }

            sermon.Title = updated.Title;
            sermon.Speaker = updated.Speaker;
            sermon.SeriesName = updated.SeriesName;
            sermon.SermonDate = updated.SermonDate;
            sermon.ScriptureReference = updated.ScriptureReference;
            sermon.Description = updated.Description;
            sermon.VideoUrl = updated.VideoUrl;
            sermon.AudioUrl = updated.AudioUrl;
            sermon.Tags = updated.Tags;
            sermon.IsPublished = updated.IsPublished;
            sermon.IsOnlineMeeting = updated.IsOnlineMeeting;
            sermon.MeetingRoomCode = updated.IsOnlineMeeting ? updated.MeetingRoomCode : null;
            sermon.LiveStreamUrl = updated.IsOnlineMeeting ? null : updated.LiveStreamUrl;

            await _db.SaveChangesAsync();
            TempData["Success"] = "Sermon updated!";
            return RedirectToAction("Sermons");
        }

        // ── Helper: load meeting rooms for dropdown ─────────────────
        private async Task PopulateMeetingRoomsAsync()
        {
            ViewBag.MeetingRooms = await _db.MeetingRooms
                .Where(r => r.Status == RoomStatus.Live ||
                            r.Status == RoomStatus.Scheduled)
                .OrderBy(r => r.Status == RoomStatus.Live ? 0 : 1)
                .ThenBy(r => r.ScheduledAt)
                .ToListAsync();
        }

        [HttpPost]
        public async Task<IActionResult> ToggleLive(int id)
        {
            var sermon = await _db.Sermons.FindAsync(id);
            if (sermon == null) return NotFound();

            bool isCurrentlyLive = sermon.IsLive;

            // Always clear any live sermon first (only one can be live at a time)
            await _db.Sermons
                .Where(s => s.IsLive)
                .ForEachAsync(s => s.IsLive = false);

            // If it was NOT live before → go live. If it WAS live → leave it off (stop).
            if (!isCurrentlyLive)
                sermon.IsLive = true;

            await _db.SaveChangesAsync();
            return Json(new { isLive = sermon.IsLive });
        }

        // ── Newsletter ──
        public async Task<IActionResult> Newsletters() =>
            View(await _db.Newsletters.OrderByDescending(n => n.CreatedAt).ToListAsync());

        [HttpGet] public IActionResult ComposeNewsletter() => View(new NewsletterComposeViewModel());

        [HttpPost]
        public async Task<IActionResult> ComposeNewsletter(NewsletterComposeViewModel vm)
        {
            if (!ModelState.IsValid) return View(vm);
            var newsletter = new Newsletter { Subject = vm.Subject, Body = vm.Body, PreviewText = vm.PreviewText };
            if (vm.SendNow)
            {
                var subs = await _db.NewsletterSubscribers.Where(s => s.IsActive).Select(s => s.Email).ToListAsync();
                await _email.SendBulkAsync(subs, vm.Subject, vm.Body);
                newsletter.Status = NewsletterStatus.Sent;
                newsletter.SentAt = DateTime.UtcNow;
                newsletter.RecipientsCount = subs.Count;
            }
            _db.Newsletters.Add(newsletter);
            await _db.SaveChangesAsync();
            TempData["Success"] = vm.SendNow ? $"Newsletter sent to {newsletter.RecipientsCount} subscribers!" : "Newsletter saved as draft.";
            return RedirectToAction("Newsletters");
        }

        // ── Events management ──
        public async Task<IActionResult> Events() =>
            View(await _db.ChurchEvents.Include(e => e.Registrations).OrderByDescending(e => e.StartDate).ToListAsync());

        //[HttpGet] public IActionResult AddEvent() => View(new ChurchEvent());

        //[HttpPost]
        //public async Task<IActionResult> AddEvent(ChurchEvent ev)
        //{
        //    _db.ChurchEvents.Add(ev);
        //    await _db.SaveChangesAsync();
        //    return RedirectToAction("Events");
        //}

        // ── Reports ──
        public async Task<IActionResult> Reports()
        {
            var now = DateTime.UtcNow;
            ViewBag.MonthlyGiving = await _db.Givings
                .Where(g => g.PaymentStatus == PaymentStatus.Successful && g.GivingDate.Year == now.Year)
                .GroupBy(g => g.GivingDate.Month)
                .Select(g => new { Month = g.Key, Total = g.Sum(x => x.Amount) })
                .OrderBy(g => g.Month).ToListAsync();
            return View();
        }
    }
}
