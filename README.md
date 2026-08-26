# Love Fellowship Ministry Website
### ASP.NET Core 8 MVC + MySQL

---

## 🚀 Quick Start

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- MySQL 8.0+
- Visual Studio 2022 / VS Code / Rider

---

## ⚙️ Setup Instructions

### 1. Configure Database
Edit `appsettings.json`:
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Port=3306;Database=church_ministry;User=root;Password=YOUR_PASSWORD;"
}
```

### 2. Configure Email (Gmail)
```json
"EmailSettings": {
  "SmtpHost": "smtp.gmail.com",
  "SmtpPort": 587,
  "SmtpUser": "your@gmail.com",
  "SmtpPassword": "your-app-password"
}
```
> Generate an **App Password** in Gmail → Security → 2-Step Verification → App Passwords

### 3. Configure Payments (Flutterwave)
Sign up at [flutterwave.com](https://flutterwave.com) and add your keys:
```json
"Flutterwave": {
  "PublicKey": "FLWPUBK_TEST-...",
  "SecretKey": "FLWSECK_TEST-...",
  "EncryptionKey": "FLWENCK-..."
}
```
Flutterwave supports: **MTN Mobile Money Uganda**, **Airtel Money Uganda**, **M-Pesa Kenya**, **VISA**, **Mastercard** - all in one integration.

### 4. Configure SMS (Africa's Talking)
Sign up at [africastalking.com](https://africastalking.com):
```json
"AfricasTalking": {
  "Username": "your_username",
  "ApiKey": "your_api_key"
}
```

### 5. Run Migrations & Start
```bash
cd ChurchMinistry
dotnet restore
dotnet ef migrations add InitialCreate
dotnet ef database update
dotnet run
```

The app auto-seeds:
- **Roles**: Admin, Pastor, Member
- **Default Admin**: `admin@church.org` / `Admin@123456`

---

## 🌐 Features Overview

| Module | Features |
|--------|----------|
| **Auth** | Register, Login, Forgot Password, Role-based access |
| **Members** | Profile, QR Code, Attendance history, Directory |
| **Attendance** | QR check-in scanner, service sessions, reports |
| **Giving** | MTN MoMo, Airtel, M-Pesa, VISA/MC, recurring giving, receipts |
| **Media** | Live streaming, sermon library, audio/video, series |
| **Community** | Forum, Q&A with Pastor, real-time chat (SignalR) |
| **Prayer Pod** | Prayer wall, prayer counter, testimonies, categories |
| **Newsletter** | Compose, send to all subscribers, archive |
| **Events** | Calendar, event registration, capacity management |
| **Admin** | Dashboard, analytics charts, member management, reports |

---

## 💳 Payment Gateway Details

### Flutterwave (covers all methods)
- `MTN Mobile Money` → USSD push to +256 numbers
- `Airtel Money` → USSD push to +256 numbers  
- `M-Pesa` → STK push to +254 numbers
- `VISA / Mastercard` → Hosted checkout page

**Webhook / Callback URL** (set in Flutterwave dashboard):
```
https://yourdomain.com/Giving/Callback
```

---

## 📁 Project Structure

```
ChurchMinistry/
├── Controllers/         # All MVC controllers
├── Models/              # Domain models & Identity
├── ViewModels/          # Form & display view models
├── Views/               # Razor views (.cshtml)
│   ├── Account/         # Login, Register
│   ├── Admin/           # Admin dashboard, management
│   ├── Community/       # Forum, Chat
│   ├── Events/          # Church events
│   ├── Giving/          # Online giving
│   ├── Home/            # Landing page
│   ├── Media/           # Sermons, live stream
│   ├── Members/         # Member portal
│   ├── Newsletter/      # Newsletter archive
│   └── Prayer/          # Prayer wall
├── Services/            # Email, SMS, Payment, QR, Attendance
├── Hubs/                # SignalR ChatHub
├── Data/                # ApplicationDbContext
├── Migrations/          # database_setup.sql
└── wwwroot/             # CSS, JS, images, uploads
```

---

## 🔐 Default Roles

| Role | Access |
|------|--------|
| `Admin` | Full access, admin dashboard |
| `Pastor` | Admin dashboard, can reply as Pastor in forum |
| `Member` | Member portal, giving, prayer, community |

---

## 📱 Live Streaming Setup
1. Go to Admin → Sermons → Add Sermon
2. Paste your **YouTube Live** or stream URL  
3. Check **Mark as Live**
4. The live banner appears automatically on the homepage

---

## 🛠️ Tech Stack
- **Backend**: ASP.NET Core 8 MVC + C#
- **Database**: MySQL 8 via Pomelo EF Core
- **Auth**: ASP.NET Core Identity
- **Real-time**: SignalR (live chat)
- **Payments**: Flutterwave API
- **Email**: MailKit (SMTP)
- **SMS**: Africa's Talking API
- **QR Codes**: QRCoder
- **Charts**: Chart.js
- **Icons**: Font Awesome 6

---

## 🚧 Deployment (Production)

```bash
dotnet publish -c Release -o ./publish
```

Recommended hosting: **Azure App Service** or **Railway.app** with **PlanetScale** MySQL.

Update `appsettings.json` for production connection strings and set `ASPNETCORE_ENVIRONMENT=Production`.
