using ChurchMinistry.Data;
using ChurchMinistry.Hubs;
using ChurchMinistry.Models;
using ChurchMinistry.Services;
using ChurchMinistry.ViewComponents;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──
var conn = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(opt =>
    opt.UseMySql(conn, ServerVersion.AutoDetect(conn)));

// ── Identity ──
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(opt =>
{
    opt.Password.RequiredLength = 8;
    opt.Password.RequireNonAlphanumeric = false;
    opt.Password.RequireUppercase = false;
    opt.SignIn.RequireConfirmedEmail = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(opt =>
{
    opt.LoginPath = "/Account/Login";
    opt.LogoutPath = "/Account/Logout";
    opt.AccessDeniedPath = "/Account/AccessDenied";
    opt.ExpireTimeSpan = TimeSpan.FromDays(30);
    opt.SlidingExpiration = true;
});

// ── MVC + SignalR + Razor runtime compilation ──
builder.Services.AddControllersWithViews().AddRazorRuntimeCompilation();
builder.Services.AddSignalR(o =>
{
    // Matches the 60 s serverTimeoutInMilliseconds set in Views/Meeting/Room.cshtml,
    // so a phone on a weak connection isn't dropped from the meeting too quickly
    o.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
});

// ── Custom Services ──
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IQrCodeService, QrCodeService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<ISmsService, SmsService>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ILiveKitService, LiveKitService>();
builder.Services.AddHttpClient();

// ── Session ──
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(opt => { opt.IdleTimeout = TimeSpan.FromMinutes(60); opt.Cookie.HttpOnly = true; });

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentMemberViewComponent>();
var app = builder.Build();

// ── Middleware ──
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.MapHub<MeetingHub>("/meetingHub");
app.MapHub<ChurchMinistry.Hubs.LiveSessionHub>("/liveSessionHub");
app.UseAuthorization();

// ── Routes ──
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute("meeting-room", "meeting/room/{code}", new { controller = "Meeting", action = "Room" });
app.MapControllerRoute(
    "liveSettings",
    "Admin/LiveSettings/{sermonId}",
    new { controller = "LiveSettings", action = "Index" });
app.MapControllerRoute(
    "eventRegistrations",
    "Admin/EventRegistrations/{id}",
    new { controller = "Admin", action = "EventRegistrations" });
app.MapControllerRoute(
    "sermonAttendance",
    "Admin/SermonAttendance/{id}",
    new { controller = "Admin", action = "SermonAttendance" });
app.MapControllerRoute(
    "eventPaymentCallback",
    "Events/PaymentCallback",
    new { controller = "Events", action = "PaymentCallback" });

app.MapControllerRoute(
    "eventPaymentReturn",
    "Events/PaymentReturn",
    new { controller = "Events", action = "PaymentReturn" });
app.MapHub<ChatHub>("/chatHub");
//app.MapHub<MeetingHub>("/meetingHub");
//app.MapHub<ChurchMinistry.Hubs.LiveSessionHub>("/liveSessionHub");

// ── Auto-migrate on startup ──
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();

    // Meeting connections don't survive a restart — close out anyone still marked present
    await db.MeetingParticipants
        .Where(p => p.LeftAt == null)
        .ExecuteUpdateAsync(s => s.SetProperty(p => p.LeftAt, DateTime.UtcNow));

    await DbInitializer.SeedAsync(scope.ServiceProvider);

    // Seed default admin user
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    string[] roles = ["Admin", "Pastor", "Member"];
    foreach (var role in roles)
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));

    if (await userManager.FindByEmailAsync("admin@church.org") == null)
    {
        var admin = new ApplicationUser
        {
            UserName = "admin@church.org",
            Email = "admin@church.org",
            FirstName = "Church",
            LastName = "Admin",
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(admin, "Admin@123456");
        if (result.Succeeded)
            await userManager.AddToRoleAsync(admin, "Admin");
    }
}

app.Run();
