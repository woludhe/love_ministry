# Meeting module — multi-person test

Runs 1 host + 9 participants (or `PEOPLE=20` for 20) in headless Chrome against the real
LiveKit media server configured in `appsettings.json`, and checks:
join/leave tiles, audio + video between every pair, camera on/off, host "Mute All",
spotlight, leave/rejoin churn and SignalR reconnect, Live page viewers (guest and member),
and that mute / remove / end meeting are enforced by the media server.

Each person's camera and microphone are generated (an animated 320x180 canvas and a tone),
because Chrome's built-in fake camera is shared by every window on the machine. The video is
small because all test people share this machine's internet connection to the media server.

## Run

1. Start the app with the test accounts enabled (Development only):

   ```bash
   Seed__MeetingTestUsers=true dotnet run          # from the project root
   ```

   This creates `meettest1..19@church.test` / `MeetTest@123` (Member role). The host is the
   seeded `admin@church.org`.

2. In this folder:

   ```bash
   npm install
   npx playwright test            # PEOPLE=20 for 20 people, HEADED=1 to watch the browsers
   ```

   Uses the installed Google Chrome (`PW_CHANNEL=chromium` to use Playwright's own build).
   `BASE_URL` defaults to `https://localhost:53826`.

The Live page step creates a temporary live sermon linked to the test meeting (as the host)
and deletes it at the end.

A summary is printed at the end; the HTML report is in `playwright-report/`.
