namespace ChurchMinistry.Models
{
    // The fixed set of system emails that can be edited from Admin > Mail Templates.
    // Each entry's Key matches a MailTemplate.Key row seeded into the database; the catalog
    // supplies the original wording (used to seed and to "Reset to Default"), the placeholder
    // legend shown next to the edit form, and sample values used only for "Send Test Email".
    public record MailTemplateDefinition(
        string Key,
        string DisplayName,
        string Description,
        string DefaultSubject,
        string DefaultBody,
        List<(string Token, string Description)> Placeholders,
        Dictionary<string, string> SampleValues);

    public static class MailTemplateCatalog
    {
        // The shared header/footer every email is wrapped in. %BODY% is filled in by the
        // system with the chosen content template's rendered HTML — it isn't admin-editable
        // the way the other tokens are, so "Send Test Email" isn't offered for this one.
        public const string MasterLayoutKey = "MasterLayout";

        public static readonly IReadOnlyList<MailTemplateDefinition> All = new[]
        {
            new MailTemplateDefinition(
                "TwoFactorCode", "Two-Factor Sign-in Code",
                "Sent when someone signs in and needs to enter a one-time code.",
                "Your sign-in code",
                "<p>Use this code to sign in to your account:</p>" +
                "<p style=\"font-size:28px;font-weight:700;letter-spacing:6px\">%CODE%</p>" +
                "<p>It expires in a few minutes. If you didn't ask for it, someone may know your password; " +
                "please change it straight away.</p>",
                new() { ("%CODE%", "The one-time sign-in code") },
                new() { ["CODE"] = "123456" }),

            new MailTemplateDefinition(
                "RecoveryEmailVerification", "Recovery Email Verification",
                "Sent to verify a new recovery email address.",
                "Verify your recovery email",
                "<p>Use this code to verify this address as the recovery email for your account:</p>" +
                "<p style=\"font-size:28px;font-weight:700;letter-spacing:6px\">%CODE%</p>" +
                "<p>It expires in a few minutes. If you didn't ask for it, someone may know your password; " +
                "please change it straight away.</p>",
                new() { ("%CODE%", "The one-time verification code") },
                new() { ["CODE"] = "654321" }),

            new MailTemplateDefinition(
                "Welcome", "Welcome Email",
                "Sent right after someone registers as a new member.",
                "Welcome to %CHURCHNAME%!",
                "<h2>Welcome to our Church Family, %FIRSTNAME%!</h2>" +
                "<p>We are so glad you've joined us. Your membership has been confirmed.</p>" +
                "<p>You can now access your member portal, view sermons, submit prayer requests and connect with our community.</p>" +
                "<a href='%PORTALLINK%' style='background:#6366f1;color:white;padding:12px 24px;border-radius:6px;text-decoration:none;'>Visit Member Portal</a>",
                new()
                {
                    ("%FIRSTNAME%", "The new member's first name"),
                    ("%PORTALLINK%", "Link to the member dashboard")
                },
                new() { ["FIRSTNAME"] = "Jane", ["PORTALLINK"] = "https://example.org/Members/Dashboard" }),

            new MailTemplateDefinition(
                "GivingReceipt", "Giving Receipt",
                "Sent after a gift (tithe, offering, etc.) is received.",
                "Your Giving Receipt",
                "<h2>Giving Receipt</h2><p>Thank you for your generosity!</p><table>" +
                "<tr><td><b>Amount:</b></td><td>%CURRENCY% %AMOUNT%</td></tr>" +
                "<tr><td><b>Type:</b></td><td>%GIVINGTYPE%</td></tr>" +
                "<tr><td><b>Method:</b></td><td>%PAYMENTMETHOD%</td></tr>" +
                "<tr><td><b>Date:</b></td><td>%GIVINGDATE%</td></tr>" +
                "<tr><td><b>Reference:</b></td><td>%TRANSACTIONID%</td></tr></table>",
                new()
                {
                    ("%CURRENCY%", "Currency code, e.g. UGX"),
                    ("%AMOUNT%", "Amount given"),
                    ("%GIVINGTYPE%", "Tithe, Offering, etc."),
                    ("%PAYMENTMETHOD%", "How it was paid"),
                    ("%GIVINGDATE%", "Date of the gift"),
                    ("%TRANSACTIONID%", "Payment reference / transaction ID")
                },
                new()
                {
                    ["CURRENCY"] = "UGX", ["AMOUNT"] = "50,000.00", ["GIVINGTYPE"] = "Tithe",
                    ["PAYMENTMETHOD"] = "MTNMobileMoney", ["GIVINGDATE"] = "07 Nov 2026", ["TRANSACTIONID"] = "TXN-TEST-001"
                }),

            new MailTemplateDefinition(
                "GivingReminder", "Recurring Giving Reminder",
                "Sent when a member's \"remind me to give\" request is due.",
                "Your giving reminder",
                "<h2>Giving reminder</h2><p>Hi %FIRSTNAME%, your %GIFTDESCRIPTION% is due.</p>" +
                "<p><a href=\"%GIVELINK%\" style=\"display:inline-block;padding:.7rem 1.4rem;background:#6366f1;color:#fff;border-radius:8px;text-decoration:none\">Give now</a></p>" +
                "<p style=\"color:#6b7280;font-size:.85rem\">You asked us to remind you. You can <a href=\"%STOPLINK%\">stop these reminders</a> any time.</p>",
                new()
                {
                    ("%FIRSTNAME%", "The member's first name"),
                    ("%GIFTDESCRIPTION%", "e.g. \"Monthly Tithe of UGX 50,000\""),
                    ("%GIVELINK%", "Link to the pre-filled Give page"),
                    ("%STOPLINK%", "Link to stop future reminders")
                },
                new()
                {
                    ["FIRSTNAME"] = "Jane", ["GIFTDESCRIPTION"] = "Monthly Tithe of UGX 50,000",
                    ["GIVELINK"] = "https://example.org/Giving/Give", ["STOPLINK"] = "https://example.org/Giving/Reminders"
                }),

            new MailTemplateDefinition(
                "PasswordReset", "Password Reset",
                "Sent when someone requests a password reset link.",
                "Reset your password",
                "<p>We received a request to reset your password.</p>" +
                "<p><a href=\"%LINK%\">Choose a new password</a></p>" +
                "<p>If you didn't ask for this, you can ignore this email; your password won't change.</p>",
                new() { ("%LINK%", "The password reset link") },
                new() { ["LINK"] = "https://example.org/Account/ResetPassword?code=sample" }),

            new MailTemplateDefinition(
                "EventRegistrationConfirmation", "Event Registration Confirmation",
                "Sent when someone registers for a free event.",
                "Registration Confirmed - %EVENTTITLE%",
                "<h2>Registration Confirmed 🎉</h2>" +
                "<p>Hi %NAME%, your registration for <strong>%EVENTTITLE%</strong> is confirmed.</p>" +
                "%EXTRANOTE%" +
                "<table><tr><td><b>Date:</b></td><td>%EVENTDATE%</td></tr>" +
                "<tr><td><b>Time:</b></td><td>%EVENTTIME%</td></tr>%EVENTLOCATIONROW%</table>",
                new()
                {
                    ("%NAME%", "The registrant's name"),
                    ("%EVENTTITLE%", "The event's title"),
                    ("%EVENTDATE%", "The event's date"),
                    ("%EVENTTIME%", "The event's time"),
                    ("%EVENTLOCATIONROW%", "Location row — filled in automatically, blank if the event has no location"),
                    ("%EXTRANOTE%", "Extra note — filled in automatically, e.g. when registering on someone else's behalf")
                },
                new()
                {
                    ["NAME"] = "Jane Doe", ["EVENTTITLE"] = "Sunday Revival Service",
                    ["EVENTDATE"] = "Sunday, 15 November 2026", ["EVENTTIME"] = "10:00 AM",
                    ["EVENTLOCATIONROW"] = "<tr><td><b>Location:</b></td><td>Main Sanctuary</td></tr>",
                    ["EXTRANOTE"] = "<p><em>Registered by John Doe</em></p>"
                }),

            new MailTemplateDefinition(
                "EventPaymentFull", "Event Payment Confirmed (Full)",
                "Sent when a paid event's registration is fully paid off.",
                "Payment Confirmed - %EVENTTITLE%",
                "<h2>Payment Complete! 🎉</h2>" +
                "<p>Hi %NAME%, your payment of UGX %AMOUNTPAID% for <strong>%EVENTTITLE%</strong> is fully paid. You're confirmed!</p>",
                new()
                {
                    ("%NAME%", "The registrant's name"),
                    ("%EVENTTITLE%", "The event's title"),
                    ("%AMOUNTPAID%", "Total amount paid so far")
                },
                new() { ["NAME"] = "Jane Doe", ["EVENTTITLE"] = "Sunday Revival Service", ["AMOUNTPAID"] = "150,000" }),

            new MailTemplateDefinition(
                "EventPaymentPartial", "Event Payment Received (Partial)",
                "Sent when a partial payment is received for a paid event.",
                "Partial Payment Received - %EVENTTITLE%",
                "<h2>Partial Payment Received</h2>" +
                "<p>Hi %NAME%, we received UGX %AMOUNTRECEIVED%. Your remaining balance is UGX %BALANCE%.</p>",
                new()
                {
                    ("%NAME%", "The registrant's name"),
                    ("%EVENTTITLE%", "The event's title"),
                    ("%AMOUNTRECEIVED%", "The amount just received"),
                    ("%BALANCE%", "Remaining balance due")
                },
                new()
                {
                    ["NAME"] = "Jane Doe", ["EVENTTITLE"] = "Sunday Revival Service",
                    ["AMOUNTRECEIVED"] = "50,000", ["BALANCE"] = "100,000"
                }),

            new MailTemplateDefinition(
                "EventBehalfRegistration", "Event Registered-On-Your-Behalf Notice",
                "Sent to the person being registered, when someone else registers them for a paid event.",
                "Event Registration - %EVENTTITLE%",
                "<p>Hi %BEHALFNAME%, you have been registered for <strong>%EVENTTITLE%</strong> by %REGISTRANTNAME%.</p>",
                new()
                {
                    ("%BEHALFNAME%", "The invitee's name (the person registered for)"),
                    ("%EVENTTITLE%", "The event's title"),
                    ("%REGISTRANTNAME%", "The name of the person who registered them")
                },
                new() { ["BEHALFNAME"] = "Jane Doe", ["EVENTTITLE"] = "Sunday Revival Service", ["REGISTRANTNAME"] = "John Doe" }),

            new MailTemplateDefinition(
                "MeetingInvitation", "Meeting Invitation",
                "Sent to invite someone (member or raw email address) to an online meeting.",
                "You're invited: %MEETINGTITLE%",
                "<div style=\"font-family:sans-serif;max-width:520px;margin:0 auto\">" +
                "<div style=\"background:#312e81;padding:1.5rem;border-radius:12px 12px 0 0;text-align:center\">" +
                "<h1 style=\"color:white;font-size:1.4rem;margin:0\">✝ You're Invited</h1></div>" +
                "<div style=\"background:#f9fafb;padding:1.5rem;border-radius:0 0 12px 12px;border:1px solid #e5e7eb\">" +
                "<p>Hi <strong>%NAME%</strong>,</p><p>You have been invited to join an online meeting:</p>" +
                "<div style=\"background:white;border:1px solid #e5e7eb;border-radius:8px;padding:1rem;margin:1rem 0\">" +
                "<h2 style=\"margin:0 0 .5rem;color:#1e1b4b\">%MEETINGTITLE%</h2>" +
                "<p style=\"margin:0;color:#6b7280\">📅 %SCHEDULEDAT%</p>" +
                "<p style=\"margin:.3rem 0 0;color:#6b7280\">🔑 Room code: <strong>%ROOMCODE%</strong></p></div>" +
                "%DESCRIPTIONROW%" +
                "<div style=\"text-align:center;margin:1.5rem 0\">" +
                "<a href=\"%JOINLINK%\" style=\"background:#6366f1;color:white;padding:.75rem 2rem;border-radius:8px;text-decoration:none;font-weight:700;display:inline-block\">Join Meeting</a></div>" +
                "<p style=\"font-size:.8rem;color:#9ca3af;text-align:center\">Or use room code <strong>%ROOMCODE%</strong> at the meeting page.</p>" +
                "</div></div>",
                new()
                {
                    ("%NAME%", "The invitee's name (or email address if invited by email only)"),
                    ("%MEETINGTITLE%", "The meeting's title"),
                    ("%SCHEDULEDAT%", "When the meeting is scheduled, or \"Soon\" if not scheduled"),
                    ("%ROOMCODE%", "The meeting room code"),
                    ("%DESCRIPTIONROW%", "Description row — filled in automatically, blank if the meeting has no description"),
                    ("%JOINLINK%", "Link to join the meeting")
                },
                new()
                {
                    ["NAME"] = "Jane Doe", ["MEETINGTITLE"] = "Leadership Call",
                    ["SCHEDULEDAT"] = "Sunday, 15 November 2026 at 6:00 PM", ["ROOMCODE"] = "ABC123",
                    ["DESCRIPTIONROW"] = "<p style=\"color:#4b5563\">Monthly leadership planning call.</p>",
                    ["JOINLINK"] = "https://example.org/meeting/room/ABC123"
                }),

            new MailTemplateDefinition(
                "OneOnOneScheduled", "One-on-One Meeting Scheduled",
                "Sent to a member when their one-on-one meeting is scheduled or rescheduled.",
                "%MEETINGTITLE%: %WHEN%",
                "<div style=\"font-family:sans-serif;max-width:520px;margin:0 auto\">" +
                "<div style=\"background:#312e81;padding:1.5rem;border-radius:12px 12px 0 0;text-align:center\">" +
                "<h1 style=\"color:white;font-size:1.3rem;margin:0\">✝ %MEETINGTITLE%</h1></div>" +
                "<div style=\"background:#f9fafb;padding:1.5rem;border-radius:0 0 12px 12px;border:1px solid #e5e7eb\">" +
                "<p>Hi <strong>%FIRSTNAME%</strong>,</p><p>%INTRO%</p>" +
                "<div style=\"background:white;border:1px solid #e5e7eb;border-radius:8px;padding:1rem;margin:1rem 0\">" +
                "<p style=\"margin:0;color:#1e1b4b\">📅 <strong>%WHEN%</strong> (%DURATION% min)</p>" +
                "<p style=\"margin:.4rem 0 0;color:#6b7280\">📍 %WHERE%</p>%ATTENDEESROW%</div>" +
                "%STAFFNOTEROW%" +
                "<div style=\"text-align:center;margin:1.5rem 0\">" +
                "<a href=\"%ACTIONLINK%\" style=\"background:#6366f1;color:white;padding:.75rem 2rem;border-radius:8px;text-decoration:none;font-weight:700;display:inline-block\">%ACTIONLABEL%</a></div>" +
                "</div></div>",
                new()
                {
                    ("%FIRSTNAME%", "The member's first name"),
                    ("%MEETINGTITLE%", "The meeting's title"),
                    ("%WHEN%", "Scheduled date/time"),
                    ("%WHERE%", "Online room code, or the in-person location"),
                    ("%DURATION%", "Length of the meeting, in minutes"),
                    ("%ATTENDEESROW%", "Other attendees row — filled in automatically, blank if none"),
                    ("%STAFFNOTEROW%", "Staff note row — filled in automatically, blank if none"),
                    ("%INTRO%", "Intro sentence — \"arranged\" for a new meeting, \"updated\" for a reschedule"),
                    ("%ACTIONLINK%", "Link to join the meeting or open the dashboard"),
                    ("%ACTIONLABEL%", "Button text — \"Join Meeting\" or \"Open my dashboard\"")
                },
                new()
                {
                    ["FIRSTNAME"] = "Jane", ["MEETINGTITLE"] = "One-on-One with Apostle Enock Makoko",
                    ["WHEN"] = "Sunday, 15 November 2026 at 3:00 PM", ["WHERE"] = "Online, room code ABC123",
                    ["DURATION"] = "30",
                    ["ATTENDEESROW"] = "<p style=\"margin:.4rem 0 0;color:#6b7280\">👥 Also attending: John Doe</p>",
                    ["STAFFNOTEROW"] = "<p style=\"color:#4b5563\">Please come with your prayer points.</p>",
                    ["INTRO"] = "Your meeting has been arranged:",
                    ["ACTIONLINK"] = "https://example.org/meeting/room/ABC123", ["ACTIONLABEL"] = "Join Meeting"
                }),

            // The shared header/footer wrapper applied around every email's content.
            // No "Send Test Email" for this one — see MasterLayoutKey.
            new MailTemplateDefinition(
                MasterLayoutKey, "Shared Email Layout (Header/Footer)",
                "The header, footer and branding wrapped around every system email.",
                "",
                "<!DOCTYPE html><html><body style='font-family:sans-serif;max-width:600px;margin:auto;padding:20px'>" +
                "<div style='background:#6366f1;padding:20px;border-radius:8px 8px 0 0;text-align:center'>" +
                "%LOGOHTML%<h1 style='color:white;margin:0'>%CHURCHNAME%</h1></div>" +
                "<div style='border:1px solid #e5e7eb;padding:30px;border-radius:0 0 8px 8px'>" +
                "%BODY%" +
                "<hr style='margin:24px 0'/>" +
                "<p style='color:#6b7280;font-size:12px;text-align:center'>%CHURCHNAME% | %CHURCHADDRESS%<br/>" +
                "<a href='#' style='color:#6366f1'>Unsubscribe</a></p></div></body></html>",
                new()
                {
                    ("%CHURCHNAME%", "The church's name"),
                    ("%CHURCHADDRESS%", "The church's address"),
                    ("%LOGOHTML%", "The church's logo image, if configured"),
                    ("%BODY%", "The email's main content — filled in automatically, don't remove this token")
                },
                new())
        };

        public static MailTemplateDefinition? GetByKey(string key) => All.FirstOrDefault(d => d.Key == key);

        // The one substitution routine used both for real sends (MailTemplateService.RenderAsync)
        // and for "Send Test Email" (which substitutes sample values into the unsaved form content).
        public static string Substitute(string template, IDictionary<string, string> values)
        {
            foreach (var kv in values)
                template = template.Replace($"%{kv.Key}%", kv.Value);
            return template;
        }
    }
}
