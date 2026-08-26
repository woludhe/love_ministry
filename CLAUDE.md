# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

ASP.NET Core 8 MVC web app for "Apostle Enock Makoko" church ministry (Love Fellowship Ministry). MySQL via Pomelo EF Core, ASP.NET Core Identity for auth, SignalR for real-time chat/meetings/live sessions, Flutterwave for payments, MailKit for email, Africa's Talking for SMS, QRCoder for member QR codes.

This directory is **not a git repository** — there is no version control in place here. Be extra careful with destructive file operations since there's no `git` safety net to recover from mistakes.

## Common commands

```bash
dotnet restore
dotnet build
dotnet run                              # starts the app; auto-applies EF migrations + seeds roles/admin on startup
dotnet ef migrations add <Name>         # add a migration after changing entities in Models/
dotnet ef database update               # apply migrations manually
dotnet publish -c Release -o ./publish  # production build
```

There is no test project in this repo (no `*Test*` project, no `dotnet test` target) and no linter config. Verify changes by building and running the app.

Default seeded login: `admin@church.org` / `Admin@123456` (role `Admin`).

## Architecture

**Entities are consolidated, not one-class-per-file.** Most domain models live in `Models/Models.cs` (~390 lines, one file for Member, MinistryGroup, ServiceSession, AttendanceRecord, Giving, Sermon, ForumPost/Reply, ChatMessage, PrayerRequest, Newsletter, ChurchEvent/EventRegistration/EventPayment, Notification, and their enums). Related model groups that were added later have their own files: `Models/MeetingModels.cs`, `Models/LiveSessionModels.cs`, `Models/PaymentMethodModel.cs`. Follow this pattern (append to the relevant existing file) rather than creating a new file per entity.

Same pattern for controllers: `Controllers/Controllers.cs` holds `HomeController`, `AccountController`, `MembersController`, `CommunityController`, `NewsletterController`, `AdminController`. Newer/feature-specific controllers each have their own file (`EventsController.cs`, `GivingController.cs`, `MediaController.cs`, `PrayerController.cs`, `MeetingController.cs`, `MeetingApprovalsController.cs`, `LiveSettingsController.cs`, `AdminPaymentController.cs`, `PaymentMethodsController.cs` — note this one defines an API controller, `PaymentMethodsApiController : ControllerBase`, `NotificationsController.cs`).

Same for services: `Services/Services.cs` defines all service interfaces + implementations together (`IEmailService`/`EmailService`, `IQrCodeService`/`QrCodeService`, `IPaymentService`/`PaymentService`, `ISmsService`/`SmsService`, `IAttendanceService`/`AttendanceService`, `INotificationService`/`NotificationService`). All are registered as `Scoped` in `Program.cs`.

**ViewModels live under `Views/ViewModels/`, not a top-level `ViewModels/` folder** (`Views/ViewModels/ViewModels.cs`, `HomeIndexViewModel.cs`, `MeetingViewModels.cs`) despite the `ChurchMinistry.ViewModels` namespace and what the README's project-structure diagram implies. Check there first when looking for a view model.

**Data layer**: `Data/ApplicationDbContext.cs` (`IdentityDbContext<ApplicationUser>`) — all entity relationships, enum-to-string conversions, unique indexes, and cascade/delete behaviors are configured centrally in `OnModelCreating`, not via separate `IEntityTypeConfiguration` classes. When adding an entity or relationship, add its `DbSet` and any config here. `Data/DbInitializer.cs` seeds roles and (optionally) a default admin; `Program.cs` also runs `db.Database.Migrate()` and its own inline role/admin seeding on every startup — both places touch seeding, so check both if changing bootstrap behavior.

**Real-time**: three SignalR hubs mapped in `Program.cs` — `/chatHub` (`Hubs/ChatHub.cs`, community chat), `/meetingHub` (`Hubs/MeetingHub.cs`, video/meeting rooms), `/liveSessionHub` (`Hubs/LiveSessionHub.cs`, live service overlay: bible verses, announcements, giving panel).

**Routing**: beyond the default `{controller=Home}/{action=Index}/{id?}` route, `Program.cs` defines explicit routes for `meeting/room/{code}` → `Meeting/Room`, `Admin/LiveSettings/{sermonId}`, `Admin/EventRegistrations/{id}`, and Flutterwave payment callback/return routes under `Events/`. Add new non-default routes here rather than relying on attribute routing, to stay consistent with the rest of the app.

**Current member access pattern**: `ViewComponents/CurrentMemberViewComponent` (in `Views/ViewComponents/CurrentMemberViewComponent.cs`, namespace `ChurchMinistry.ViewComponents`) exposes the logged-in user's `Member` record + roles either as a Razor view component (`@await Component.InvokeAsync("CurrentMember")`) or directly via injected `GetModelAsync()` (used in `_Layout.cshtml`). Use this rather than re-querying `Members` by `UserId` in every controller/view.

**Payments**: Flutterwave covers MTN Mobile Money, Airtel Money, M-Pesa, and Visa/Mastercard through one integration (`IPaymentService`). `Giving` and event ticket payments (`EventRegistration`/`EventPayment`) are separate entities/flows that both go through Flutterwave — the webhook/callback URL is `/Giving/Callback` for giving, and `Events/PaymentCallback` / `Events/PaymentReturn` for event tickets.

**Config note**: `appsettings.json` in this repo currently contains real-looking SMTP, Flutterwave, and Jwt secrets committed in plain text (not placeholders). Treat this file as sensitive and don't paste its contents elsewhere; if adding new config sections follow the existing `EmailSettings`/`Flutterwave`/`AfricasTalking`/`Church`/`Jwt` section style.

## Roles

Three roles seeded on startup: `Admin` (full access + admin dashboard), `Pastor` (admin dashboard access, replies as Pastor in forum), `Member` (member portal, giving, prayer, community). Role checks are done via `ClaimsPrincipal.IsInRole(...)` / `[Authorize(Roles = "...")]`, not a custom permissions system.
