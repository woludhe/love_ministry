using ChurchMinistry.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ChurchMinistry.Data
{
    public static class DbInitializer
    {
        /// <summary>
        /// Call this from Program.cs after app.Build():
        ///
        ///   using (var scope = app.Services.CreateScope())
        ///   {
        ///       await DbInitializer.SeedAsync(scope.ServiceProvider);
        ///   }
        /// </summary>
        public static async Task SeedAsync(IServiceProvider services)
        {
            var db          = services.GetRequiredService<ApplicationDbContext>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

            // Apply any pending migrations automatically
            await db.Database.MigrateAsync();

            // ── 1. Roles ─────────────────────────────────────────────
            string[] roles = { "Admin", "Pastor", "Member", "Guest" };
            foreach (var role in roles)
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));

            // ── 2. Default Admin user ─────────────────────────────────
            //const string adminEmail    = "admin@church.org";
            //const string adminPassword = "Admin@123456";

            //var adminUser = await userManager.FindByEmailAsync(adminEmail);
            //if (adminUser == null)
            //{
            //    adminUser = new ApplicationUser
            //    {
            //        UserName       = adminEmail,
            //        Email          = adminEmail,
            //        EmailConfirmed = true,
            //        FirstName      = "System",
            //        LastName       = "Administrator",
            //    };
            //    var result = await userManager.CreateAsync(adminUser, adminPassword);
            //    if (!result.Succeeded)
            //        throw new Exception("Failed to create admin user: " +
            //            string.Join(", ", result.Errors.Select(e => e.Description)));
            //}

            //if (!await userManager.IsInRoleAsync(adminUser, "Admin"))
            //    await userManager.AddToRoleAsync(adminUser, "Admin");

            //// Create corresponding Member record for admin
            //if (!await db.Members.AnyAsync(m => m.UserId == adminUser.Id))
            //{
            //    db.Members.Add(new Member
            //    {
            //        UserId        = adminUser.Id,
            //        FirstName     = "System",
            //        LastName      = "Administrator",
            //        Email         = adminEmail,
            //        Phone         = "+256700000000",
            //        JoinDate      = DateTime.UtcNow,
            //        Status        = MemberStatus.Active,
            //        MembershipType          = MembershipType.Regular,
            //    });
            //    await db.SaveChangesAsync();
            //}

            //// ── 3. Default Pastor user ────────────────────────────────
            //const string pastorEmail    = "makokoenock@gmail.com";
            //const string pastorPassword = "Apostle@123456";

            //var pastorUser = await userManager.FindByEmailAsync(pastorEmail);
            //if (pastorUser == null)
            //{
            //    pastorUser = new ApplicationUser
            //    {
            //        UserName       = pastorEmail,
            //        Email          = pastorEmail,
            //        EmailConfirmed = true,
            //        FirstName      = "Apostle Enock",
            //        LastName       = "Makoko",
            //    };
            //    await userManager.CreateAsync(pastorUser, pastorPassword);
            //}

            //if (!await userManager.IsInRoleAsync(pastorUser, "Pastor"))
            //    await userManager.AddToRoleAsync(pastorUser, "Pastor");

            //if (!await db.Members.AnyAsync(m => m.UserId == pastorUser.Id))
            //{
            //    db.Members.Add(new Member
            //    {
            //        UserId        = pastorUser.Id,
            //        FullName      = "Apostle Enock Makoko",
            //        Email         = pastorEmail,
            //        PhoneNumber   = "+256700000001",
            //        MemberNumber  = "MBR-0002",
            //        JoinDate      = DateTime.UtcNow,
            //        Status        = MemberStatus.Active,
            //        Role          = "Pastor",
            //    });
            //    await db.SaveChangesAsync();
            //}

            //// ── 4. Church Events (sample) ─────────────────────────────
            //if (!await db.ChurchEvents.AnyAsync())
            //{
            //    db.ChurchEvents.AddRange(
            //        new ChurchEvent
            //        {
            //            Title           = "Sunday Morning Service",
            //            Description     = "Join us every Sunday for powerful worship and the Word of God.",
            //            StartDate       = NextDayOfWeek(DayOfWeek.Sunday, 8, 0),
            //            EndDate         = NextDayOfWeek(DayOfWeek.Sunday, 11, 0),
            //            Location        = "Love Fellowship Ministries, Main Sanctuary",
            //            IsPublic        = true,
            //            MaxCapacity    = 500,
            //            CreatedAt       = DateTime.UtcNow,
            //        },
            //        new ChurchEvent
            //        {
            //            Title           = "Wednesday Bible Study",
            //            Description     = "Midweek Bible study and prayer meeting. All welcome.",
            //            StartDate       = NextDayOfWeek(DayOfWeek.Wednesday, 18, 30),
            //            EndDate         = NextDayOfWeek(DayOfWeek.Wednesday, 20, 0),
            //            Location        = "Love Fellowship Ministries, Fellowship Hall",
            //            IsPublic        = true,
            //            MaxCapacity = 100,
            //            CreatedAt       = DateTime.UtcNow,
            //        },
            //        new ChurchEvent
            //        {
            //            Title           = "Youth Friday Night",
            //            Description     = "Youth gathering every Friday — games, worship, Word.",
            //            StartDate       = NextDayOfWeek(DayOfWeek.Friday, 17, 0),
            //            EndDate         = NextDayOfWeek(DayOfWeek.Friday, 20, 0),
            //            Location        = "Love Fellowship Ministries, Youth Centre",
            //            IsPublic        = true,
            //            MaxCapacity     = 150,
            //            CreatedAt       = DateTime.UtcNow,
            //        }
            //    );
            //    await db.SaveChangesAsync();
            //}

            //// ── 5. Sample Sermon ──────────────────────────────────────
            //if (!await db.Sermons.AnyAsync())
            //{
            //    db.Sermons.Add(new Sermon
            //    {
            //        Title             = "Walking in Faith",
            //        Speaker           = "Apostle Enock Makoko",
            //        Description       = "A message on trusting God through every season of life.",
            //        SermonDate        = DateTime.UtcNow.Date,
            //        ScriptureReference= "Hebrews 11:1",
            //        //Category          = SermonCategory.Faith,
            //        IsPublished       = true,
            //        IsLive            = false,
            //        IsOnlineMeeting   = false,
            //        CreatedAt         = DateTime.UtcNow,
            //    });
            //    await db.SaveChangesAsync();
            //}

            // ── 6. Payment Methods ────────────────────────────────────
            if (!await db.PaymentMethods.AnyAsync())
            {
                db.PaymentMethods.AddRange(
                    new PaymentMethodConfig
                    {
                        Name                 = "MTN Mobile Money",
                        Code                 = "mtn",
                        BadgeLabel           = "MTN",
                        BadgeColor           = "#d97706",
                        Currency             = "UGX",
                        CurrencySymbol       = "UGX",
                        IntegrationType      = PaymentIntegrationType.Manual,
                        InstructionsText     = "Dial *165# → Send Money → Enter Church Number (123456) → Enter amount → Confirm with PIN.",
                        DefaultAmounts       = "5000,10000,20000,50000,100000",
                        IsActive             = true,
                        SortOrder            = 1,
                    },
                    new PaymentMethodConfig
                    {
                        Name                 = "Airtel Money",
                        Code                 = "airtel",
                        BadgeLabel           = "AIR",
                        BadgeColor           = "#dc2626",
                        Currency             = "UGX",
                        CurrencySymbol       = "UGX",
                        IntegrationType      = PaymentIntegrationType.Manual,
                        InstructionsText     = "Dial *185# → Make Payments → Enter Church Number (654321) → Enter amount → Confirm.",
                        DefaultAmounts       = "5000,10000,20000,50000,100000",
                        IsActive             = true,
                        SortOrder            = 2,
                    },
                    new PaymentMethodConfig
                    {
                        Name                 = "M-Pesa",
                        Code                 = "mpesa",
                        BadgeLabel           = "M-P",
                        BadgeColor           = "#059669",
                        Currency             = "KES",
                        CurrencySymbol       = "KSh",
                        IntegrationType      = PaymentIntegrationType.Manual,
                        InstructionsText     = "Go to M-Pesa → Lipa na M-Pesa → Pay Bill → Business No: 123456 → Account No: GIVE → Enter amount → Enter PIN.",
                        DefaultAmounts       = "500,1000,2000,5000,10000",
                        IsActive             = true,
                        SortOrder            = 3,
                    },
                    new PaymentMethodConfig
                    {
                        Name                 = "VISA / Mastercard",
                        Code                 = "visa",
                        BadgeLabel           = "VISA",
                        BadgeColor           = "#1d4ed8",
                        Currency             = "USD",
                        CurrencySymbol       = "$",
                        IntegrationType      = PaymentIntegrationType.Flutterwave,
                        InstructionsText     = null,
                        DefaultAmounts       = "10,20,50,100,200",
                        IsActive             = true,
                        SortOrder            = 4,
                    },
                    new PaymentMethodConfig
                    {
                        Name                 = "Cash",
                        Code                 = "cash",
                        BadgeLabel           = "CASH",
                        BadgeColor           = "#6366f1",
                        Currency             = "UGX",
                        CurrencySymbol       = "UGX",
                        IntegrationType      = PaymentIntegrationType.Manual,
                        InstructionsText     = "Place your cash offering in the offering bag during service, or visit the church office (Monday–Friday, 9 AM–5 PM).",
                        DefaultAmounts       = "5000,10000,20000,50000",
                        IsActive             = true,
                        SortOrder            = 5,
                    }
                );
                await db.SaveChangesAsync();
            }

            // ── 7. Default Church Settings ────────────────────────────
            // (only if you have a ChurchSettings table)
            // if (!await db.ChurchSettings.AnyAsync())
            // {
            //     db.ChurchSettings.Add(new ChurchSetting
            //     {
            //         ChurchName    = "Love Fellowship Ministries",
            //         PastorName    = "Apostle Enock Makoko",
            //         PhoneNumber   = "+256 784 159 620",
            //         Email         = "info@apostleenockmakoko.com",
            //         Address       = "Entebbe, Uganda",
            //         ServiceTimes  = "Sunday 8:00 AM & 10:30 AM",
            //         Currency      = "UGX",
            //         FacebookUrl   = "",
            //         YouTubeUrl    = "",
            //         WhatsAppNumber= "",
            //     });
            //     await db.SaveChangesAsync();
            // }

            // ── 8. Meeting test accounts (Development only, opt-in) ───
            // Used by tests/meeting-e2e. Enable with env var Seed__MeetingTestUsers=true
            var env    = services.GetRequiredService<IWebHostEnvironment>();
            var config = services.GetRequiredService<IConfiguration>();
            if (env.IsDevelopment() && config.GetValue<bool>("Seed:MeetingTestUsers"))
            {
                // 19 accounts: enough for a 20-person run (host + 19)
                for (int i = 1; i <= 19; i++)
                {
                    var email = $"meettest{i}@church.test";
                    var user  = await userManager.FindByEmailAsync(email);
                    if (user == null)
                    {
                        user = new ApplicationUser
                        {
                            UserName = email, Email = email, EmailConfirmed = true,
                            FirstName = "Tester", LastName = i.ToString()
                        };
                        var result = await userManager.CreateAsync(user, "MeetTest@123");
                        if (!result.Succeeded) continue;
                        await userManager.AddToRoleAsync(user, "Member");
                    }
                    if (!await db.Members.AnyAsync(m => m.UserId == user.Id))
                    {
                        db.Members.Add(new Member
                        {
                            FirstName = "Tester", LastName = i.ToString(),
                            Email = email, UserId = user.Id
                        });
                        await db.SaveChangesAsync();
                    }
                }
            }
        }

        // ── Helper: next occurrence of a weekday at a given time ──────
        private static DateTime NextDayOfWeek(DayOfWeek day, int hour, int minute)
        {
            var today = DateTime.UtcNow.Date;
            int daysUntil = ((int)day - (int)today.DayOfWeek + 7) % 7;
            if (daysUntil == 0) daysUntil = 7; // always future
            return today.AddDays(daysUntil).AddHours(hour).AddMinutes(minute);
        }
    }
}
